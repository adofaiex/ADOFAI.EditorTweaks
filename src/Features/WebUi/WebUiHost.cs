using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ADOFAI.EditorTweaks.Api.Rendering;
using ADOFAI.EditorTweaks.Features.ChartRendering;
using ADOFAI.EditorTweaks.Features.CloudSettings;
using ADOFAI.SteamIntegration;
using GDMiniJSON;
using Steamworks;
using UnityEngine;
using ApiChartRenderResult = ADOFAI.EditorTweaks.Api.Rendering.ChartRenderResult;

namespace ADOFAI.EditorTweaks.Features.WebUi
{
    internal sealed class WebUiHost : MonoBehaviour
    {
        private const int FirstPort = 18080;
        private const int LastPort = 18180;
        private const int CommandTimeoutMilliseconds = 5000;
        private const float StateBroadcastIntervalSeconds = 0.1f;
        private const int SteamOverlayProbeAttempts = 6;
        private const float SteamOverlayProbeIntervalSeconds = 0.2f;

        private static WebUiHost? instance;

        private readonly ConcurrentQueue<MainThreadCommand> commands = new ConcurrentQueue<MainThreadCommand>();
        private readonly WebUiEventHub eventHub = new WebUiEventHub();
        private readonly object stateGate = new object();
        private HttpListener? listener;
        private Thread? listenerThread;
        private string webRoot = string.Empty;
        private string accessToken = string.Empty;
        private string pageUrl = string.Empty;
        private string latestStateJson = "{}";
        private ChartRenderTask? renderTask;
        private Coroutine? openSettingsPageRoutine;
        private float nextStateBroadcastTime;
        private bool stateDirty = true;
        private int shutdownRequested;

        public static bool IsRunning => instance != null && instance.listener != null;

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject("ADOFAI.EditorTweaks.WebUiHost");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<WebUiHost>();
        }

        public static void Destroy()
        {
            if (instance == null)
            {
                return;
            }

            instance.Shutdown();
            UnityEngine.Object.Destroy(instance.gameObject);
            instance = null;
        }

        private void Awake()
        {
            StartHost();
        }

        private void Update()
        {
            ProcessCommands();
            ObserveRenderTask();

            if (WebUiHotkey.IsPressed(Main.Settings.WebUiOpenHotkey))
            {
                if (openSettingsPageRoutine == null)
                {
                    openSettingsPageRoutine = StartCoroutine(OpenSettingsPageRoutine());
                }
            }

            if (ChartRenderService.IsActive && Input.GetKeyDown(KeyCode.Escape))
            {
                ChartRenderApi.CurrentTask?.Cancel();
                Main.Log("[WebUI] Render canceled by Escape.");
            }

            bool renderStateNeedsBroadcast = renderTask != null && !renderTask.IsTerminal;
            if ((stateDirty || renderStateNeedsBroadcast) && Time.unscaledTime >= nextStateBroadcastTime)
            {
                PublishState();
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void StartHost()
        {
            if (Main.Mod == null)
            {
                return;
            }

            webRoot = Path.GetFullPath(Path.Combine(Main.Mod.Path, "Resources", "WebUI"));
            string indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath))
            {
                Main.Log("[WebUI] Resources/WebUI/index.html was not found; HTTP service was not started.");
                return;
            }

            accessToken = Guid.NewGuid().ToString("N");
            if (!TryStartListener())
            {
                Main.Log("[WebUI] Could not bind a loopback HTTP port. The Web settings page is unavailable.");
                return;
            }

            latestStateJson = WebUiStateBuilder.BuildJson(null);
            listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "ADOFAI.EditorTweaks.WebUiHttp"
            };
            listenerThread.Start();
            Main.Log("[WebUI] Listening on 127.0.0.1. Open " + pageUrl);
            stateDirty = false;
        }

        private bool TryStartListener()
        {
            for (int port = FirstPort; port <= LastPort; port++)
            {
                HttpListener candidate = new HttpListener();
                try
                {
                    candidate.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    candidate.Start();
                    listener = candidate;
                    pageUrl = "http://127.0.0.1:" + port + "/?token=" + accessToken;
                    return true;
                }
                catch (Exception)
                {
                    candidate.Close();
                }
            }

            return false;
        }

        private void ListenLoop()
        {
            while (Volatile.Read(ref shutdownRequested) == 0)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = listener?.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (context == null)
                {
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                string path = context.Request.Url?.AbsolutePath ?? "/";
                if (path.Equals("/api/events", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        WriteJson(context, 401, Error("Invalid WebUI token."));
                        return;
                    }

                    HandleEvents(context);
                    return;
                }

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        WriteJson(context, 401, Error("Invalid WebUI token."));
                        return;
                    }

                    HandleApi(context, path);
                    return;
                }

                if (path.Equals("/docs/manual", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        WriteJson(context, 401, Error("Invalid WebUI token."));
                        return;
                    }

                    ServeDocumentFile(context, Path.Combine(Main.Mod!.Path, "Resources", "README.html"));
                    return;
                }

                if (path.Equals("/docs/ffmpeg", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        WriteJson(context, 401, Error("Invalid WebUI token."));
                        return;
                    }

                    ServeDocumentFile(context, Path.Combine(Main.Mod!.Path, "Resources", "FFmpegReference.html"));
                    return;
                }

                ServeStaticFile(context, path);
            }
            catch (Exception exception)
            {
                Main.Log("[WebUI] Request failed: " + exception.Message);
                try
                {
                    WriteJson(context, 500, Error("WebUI request failed."));
                }
                catch
                {
                    context.Response.Close();
                }
            }
        }

        private void HandleApi(HttpListenerContext context, string path)
        {
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(context, 405, Error("Only GET and POST are supported."));
                return;
            }

            Dictionary<string, object> body = ReadJsonBody(context);
            switch (path.ToLowerInvariant())
            {
                case "/api/settings":
                    ExecuteOnMain(() =>
                    {
                        if (ChartRenderService.IsActive)
                        {
                            return new CommandResult(409, Error("渲染进行中，设置暂时锁定，请等待渲染结束。"));
                        }

                        ApplySettings(body.TryGetValue("changes", out object changes) ? changes as Dictionary<string, object> : null);
                        SaveSettings();
                        return StateResult();
                    }, context);
                    return;
                case "/api/settings/reset":
                    ExecuteOnMain(() =>
                    {
                        if (ChartRenderService.IsActive)
                        {
                            return new CommandResult(409, Error("渲染进行中，设置暂时锁定，请等待渲染结束。"));
                        }

                        Main.Settings.ResetAllDefaults(Main.Mod!);
                        SaveSettings();
                        return StateResult();
                    }, context);
                    return;
                case "/api/render/start":
                    ExecuteOnMain(StartRender, context);
                    return;
                case "/api/render/cancel":
                    ExecuteOnMain(CancelRender, context);
                    return;
                case "/api/cloud/upload":
                    ExecuteOnMain(UploadCloud, context);
                    return;
                case "/api/cloud/download":
                    ExecuteOnMain(() =>
                    {
                        if (ChartRenderService.IsActive)
                        {
                            return new CommandResult(409, Error("渲染进行中，设置暂时锁定，请等待渲染结束。"));
                        }

                        return DownloadCloud();
                    }, context);
                    return;
                default:
                    WriteJson(context, 404, Error("Unknown WebUI API route."));
                    return;
            }
        }

        private void HandleEvents(HttpListenerContext context)
        {
            WebUiEventHub.Client client = eventHub.AddClient();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.ContentEncoding = new UTF8Encoding(false);
            context.Response.ProtocolVersion = HttpVersion.Version11;
            context.Response.SendChunked = true;
            context.Response.KeepAlive = true;
            context.Response.Headers["Cache-Control"] = "no-cache, no-transform";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                Main.Log("[WebUI] SSE client connected.");
                using (StreamWriter writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false), 1024, true))
                {
                    writer.AutoFlush = true;
                    WriteSse(writer, "state", GetLatestStateJson());
                    while (Volatile.Read(ref shutdownRequested) == 0)
                    {
                        if (client.TryTake(out string message, TimeSpan.FromSeconds(15)))
                        {
                            writer.Write(message);
                        }
                        else
                        {
                            writer.Write(": ping\r\n\r\n");
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                eventHub.RemoveClient(client);
                Main.Log("[WebUI] SSE client disconnected.");
                try
                {
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }

        private void ServeStaticFile(HttpListenerContext context, string requestPath)
        {
            string relativePath = Uri.UnescapeDataString(requestPath.TrimStart('/'));
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = "index.html";
            }

            if (relativePath.Contains("..", StringComparison.Ordinal))
            {
                WriteJson(context, 403, Error("Path traversal is not allowed."));
                return;
            }

            string root = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string filePath = Path.GetFullPath(Path.Combine(webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            {
                WriteJson(context, 404, Error("Static file not found."));
                return;
            }

            byte[] data = File.ReadAllBytes(filePath);
            context.Response.StatusCode = 200;
            context.Response.ContentType = GetContentType(filePath);
            context.Response.ContentLength64 = data.Length;
            context.Response.OutputStream.Write(data, 0, data.Length);
            context.Response.Close();
        }

        private static void ServeDocumentFile(HttpListenerContext context, string filePath)
        {
            if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(context, 405, Error("Only GET is supported."));
                return;
            }

            if (!File.Exists(filePath))
            {
                WriteJson(context, 404, Error("Documentation file not found."));
                return;
            }

            byte[] data = File.ReadAllBytes(filePath);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = data.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.OutputStream.Write(data, 0, data.Length);
            context.Response.Close();
        }

        private void ExecuteOnMain(Func<CommandResult> action, HttpListenerContext context)
        {
            MainThreadCommand command = new MainThreadCommand(action);
            commands.Enqueue(command);
            if (!command.Completion.Task.Wait(CommandTimeoutMilliseconds))
            {
                WriteJson(context, 503, Error("Unity main thread did not respond in time."));
                return;
            }

            CommandResult result = command.Completion.Task.Result;
            WriteJson(context, result.StatusCode, result.Body);
        }

        private void ProcessCommands()
        {
            for (int i = 0; i < 32 && commands.TryDequeue(out MainThreadCommand? command); i++)
            {
                try
                {
                    command.Completion.TrySetResult(command.Action());
                }
                catch (Exception exception)
                {
                    Main.Log("[WebUI] Main-thread command failed: " + exception.Message);
                    command.Completion.TrySetResult(new CommandResult(500, Error("WebUI operation failed.")));
                }
            }
        }

        private CommandResult StartRender()
        {
            ChartRenderAvailability availability = ChartRenderApi.GetAvailability();
            if (!availability.Available)
            {
                return new CommandResult(409, Error(availability.Message));
            }

            ChartRenderRequest request = ChartRenderApi.CreateRequestFromCurrentSettings();
            request.ShowBuiltInProgressUi = false;
            ChartRenderStartResult result = ChartRenderApi.Start(request);
            if (!result.Success)
            {
                return new CommandResult(409, Error(result.Message));
            }

            ObserveRenderTask();
            PublishState();
            return StateResult();
        }

        private CommandResult CancelRender()
        {
            ChartRenderTask? task = ChartRenderApi.CurrentTask;
            if (task == null)
            {
                // A repeated cancel after the task has reached its terminal state is harmless.
                // Return the final snapshot instead of making the UI report a false failure.
                return renderTask != null ? StateResult() : new CommandResult(409, Error("没有正在运行的渲染任务。"));
            }

            if (!task.Cancel() && !task.IsCancellationRequested)
            {
                return new CommandResult(409, Error("渲染任务无法取消。"));
            }

            PublishState();
            return StateResult();
        }

        private CommandResult UploadCloud()
        {
            SaveSettings();
            bool success = CloudSettingsManager.WriteToCloud(Main.Settings);
            return success
                ? StateResult()
                : new CommandResult(409, Error("云同步上传失败。"));
        }

        private CommandResult DownloadCloud()
        {
            if (!CloudSettingsManager.TryReadFromCloud(Main.Settings))
            {
                return new CommandResult(409, Error("云端没有可用的设置文件，或 Steam 云同步不可用。"));
            }

            SaveSettings();
            return StateResult();
        }

        private void ObserveRenderTask()
        {
            ChartRenderTask? current = ChartRenderService.CurrentTask;
            if (current == null && renderTask != null && renderTask.IsTerminal)
            {
                return;
            }

            if (ReferenceEquals(current, renderTask))
            {
                return;
            }

            if (renderTask != null)
            {
                renderTask.StateChanged -= OnRenderStateChanged;
                renderTask.ProgressChanged -= OnRenderProgressChanged;
                renderTask.Completed -= OnRenderCompleted;
            }

            if (current != null)
            {
                renderTask = current;
                renderTask.StateChanged += OnRenderStateChanged;
                renderTask.ProgressChanged += OnRenderProgressChanged;
                renderTask.Completed += OnRenderCompleted;
            }

            stateDirty = true;
        }

        private void OnRenderStateChanged(ChartRenderTask task)
        {
            stateDirty = true;
        }

        private void OnRenderProgressChanged(ChartRenderTask task)
        {
            stateDirty = true;
        }

        private void OnRenderCompleted(ChartRenderTask task, ApiChartRenderResult result)
        {
            stateDirty = true;
            PublishState();
        }

        private void PublishState()
        {
            string json = WebUiStateBuilder.BuildJson(renderTask);
            lock (stateGate)
            {
                latestStateJson = json;
            }

            eventHub.Publish("state", json);
            stateDirty = false;
            nextStateBroadcastTime = Time.unscaledTime + StateBroadcastIntervalSeconds;
        }

        private void SaveSettings()
        {
            Main.Settings.Normalize();
            Main.Settings.Save(Main.Mod!);
            stateDirty = true;
            PublishState();
        }

        private static void ApplySettings(Dictionary<string, object>? changes)
        {
            if (changes == null)
            {
                return;
            }

            Settings settings = Main.Settings;
            foreach (KeyValuePair<string, object> change in changes)
            {
                switch (change.Key)
                {
                    case "WebUiOpenHotkey":
                        settings.WebUiOpenHotkey = GetString(change.Value, settings.WebUiOpenHotkey);
                        break;
                    case "EnableNumericDrag": settings.EnableNumericDrag = GetBool(change.Value, settings.EnableNumericDrag); break;
                    case "EnableCameraRelativeDecorationDragFix": settings.EnableCameraRelativeDecorationDragFix = GetBool(change.Value, settings.EnableCameraRelativeDecorationDragFix); break;
                    case "EnableDecorationPivotFix": settings.EnableDecorationPivotFix = GetBool(change.Value, settings.EnableDecorationPivotFix); break;
                    case "EnableVideoBackgroundSyncFix": settings.EnableVideoBackgroundSyncFix = GetBool(change.Value, settings.EnableVideoBackgroundSyncFix); break;
                    case "PersistEditorPreferences": settings.PersistEditorPreferences = GetBool(change.Value, settings.PersistEditorPreferences); break;
                    case "LegacyZipEncoding": settings.LegacyZipEncoding = GetString(change.Value, settings.LegacyZipEncoding); break;
                    case "DecorationMoveSnapStep": settings.DecorationMoveSnapStep = GetFloat(change.Value, settings.DecorationMoveSnapStep); break;
                    case "FloatStepPerPixel": settings.FloatStepPerPixel = GetFloat(change.Value, settings.FloatStepPerPixel); break;
                    case "IntStepPerPixel": settings.IntStepPerPixel = GetFloat(change.Value, settings.IntStepPerPixel); break;
                    case "MaxFloatingPoints": settings.MaxFloatingPoints = GetInt(change.Value, settings.MaxFloatingPoints); break;
                    case "ChartRenderWorkspaceDirectory": settings.ChartRenderWorkspaceDirectory = GetString(change.Value, settings.ChartRenderWorkspaceDirectory); break;
                    case "ChartRenderExportDirectory": settings.ChartRenderExportDirectory = GetString(change.Value, settings.ChartRenderExportDirectory); break;
                    case "ChartRenderWidth": settings.ChartRenderWidth = GetInt(change.Value, settings.ChartRenderWidth); break;
                    case "ChartRenderHeight": settings.ChartRenderHeight = GetInt(change.Value, settings.ChartRenderHeight); break;
                    case "ChartRenderFps": settings.ChartRenderFps = GetInt(change.Value, settings.ChartRenderFps); break;
                    case "ChartRenderCrf": settings.ChartRenderCrf = GetInt(change.Value, settings.ChartRenderCrf); break;
                    case "ChartRenderBitrateMbps": settings.ChartRenderBitrateMbps = GetInt(change.Value, settings.ChartRenderBitrateMbps); break;
                    case "ChartRenderPreset": settings.ChartRenderPreset = GetString(change.Value, settings.ChartRenderPreset); break;
                    case "ChartRenderEncoderMode": settings.ChartRenderEncoderMode = GetString(change.Value, settings.ChartRenderEncoderMode); break;
                    case "ChartRenderCaptureFormat": settings.ChartRenderCaptureFormat = GetString(change.Value, settings.ChartRenderCaptureFormat); break;
                    case "ChartRenderCaptureSource": settings.ChartRenderCaptureSource = GetString(change.Value, settings.ChartRenderCaptureSource); break;
                    case "ChartRenderPreviewMode": settings.ChartRenderPreviewMode = GetString(change.Value, settings.ChartRenderPreviewMode); break;
                    case "ChartRenderAudioFormat": settings.ChartRenderAudioFormat = GetString(change.Value, settings.ChartRenderAudioFormat); break;
                    case "ChartRenderVideoFormat": settings.ChartRenderVideoFormat = GetString(change.Value, settings.ChartRenderVideoFormat); break;
                    case "ChartRenderCompletionTailSeconds": settings.ChartRenderCompletionTailSeconds = GetFloat(change.Value, settings.ChartRenderCompletionTailSeconds); break;
                    case "ChartRenderAudioSyncOffsetMs": settings.ChartRenderAudioSyncOffsetMs = GetFloat(change.Value, settings.ChartRenderAudioSyncOffsetMs); break;
                    case "ChartRenderShowHitJudgments": settings.ChartRenderShowHitJudgments = GetBool(change.Value, settings.ChartRenderShowHitJudgments); break;
                    case "ChartRenderUseSelectedRange": settings.ChartRenderUseSelectedRange = GetBool(change.Value, settings.ChartRenderUseSelectedRange); break;
                    case "ChartRenderCustomMuxArgs": settings.ChartRenderCustomMuxArgs = GetString(change.Value, settings.ChartRenderCustomMuxArgs); break;
                }
            }

            settings.Normalize();
        }

        private IEnumerator OpenSettingsPageRoutine()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pageUrl))
                {
                    Main.Log("[WebUI] The local service is not running; cannot open the settings page.");
                    yield break;
                }

                if (!IsSteamInitialized())
                {
                    OpenDefaultBrowser("Steam is not initialized");
                    yield break;
                }

                string failureReason = "Steam Overlay is unavailable.";
                for (int attempt = 0; attempt < SteamOverlayProbeAttempts; attempt++)
                {
                    if (TryOpenSteamOverlay(out failureReason))
                    {
                        yield break;
                    }

                    if (attempt + 1 < SteamOverlayProbeAttempts)
                    {
                        yield return new WaitForSecondsRealtime(SteamOverlayProbeIntervalSeconds);
                    }
                }

                OpenDefaultBrowser(failureReason);
            }
            finally
            {
                openSettingsPageRoutine = null;
            }
        }

        private static bool IsSteamInitialized()
        {
            try
            {
                return SteamController.initialized;
            }
            catch
            {
                return false;
            }
        }

        private bool TryOpenSteamOverlay(out string failureReason)
        {
            try
            {
                if (!SteamController.initialized)
                {
                    failureReason = "Steam is not initialized";
                    return false;
                }

                if (!SteamUtils.IsOverlayEnabled)
                {
                    failureReason = "Steam Overlay is unavailable or still loading";
                    return false;
                }

                SteamFriends.OpenWebOverlay(pageUrl, true);
                Main.Log("[WebUI] Opened the settings page in Steam Overlay at " + pageUrl);
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failureReason = "Steam Overlay could not be opened: " + exception.Message;
                return false;
            }
        }

        private void OpenDefaultBrowser(string reason)
        {
            try
            {
                Process.Start(new ProcessStartInfo(pageUrl)
                {
                    UseShellExecute = true
                });
                Main.Log("[WebUI] " + reason + "; opened the system default browser at " + pageUrl);
            }
            catch (Exception exception)
            {
                Main.Log("[WebUI] Could not open the system default browser: " + exception.Message + ". URL: " + pageUrl);
            }
        }

        private void Shutdown()
        {
            if (Interlocked.Exchange(ref shutdownRequested, 1) != 0)
            {
                return;
            }

            if (openSettingsPageRoutine != null)
            {
                StopCoroutine(openSettingsPageRoutine);
                openSettingsPageRoutine = null;
            }

            if (renderTask != null)
            {
                renderTask.StateChanged -= OnRenderStateChanged;
                renderTask.ProgressChanged -= OnRenderProgressChanged;
                renderTask.Completed -= OnRenderCompleted;
                renderTask = null;
            }

            ChartRenderApi.CurrentTask?.Cancel();
            eventHub.CloseAll();
            listener?.Stop();
            listener?.Close();
            listener = null;
            if (listenerThread != null && listenerThread != Thread.CurrentThread)
            {
                listenerThread.Join(1000);
            }

            listenerThread = null;
            Main.Log("[WebUI] HTTP service stopped.");
        }

        private bool IsAuthorized(HttpListenerContext context)
        {
            string token = context.Request.Headers["X-EditorTweaks-Token"] ?? GetQueryParameter(context.Request.Url, "token");
            return !string.IsNullOrEmpty(token) && string.Equals(token, accessToken, StringComparison.Ordinal);
        }

        private static string GetQueryParameter(Uri? uri, string key)
        {
            if (uri == null || string.IsNullOrEmpty(uri.Query))
            {
                return string.Empty;
            }

            string query = uri.Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                string[] pair = part.Split(new[] { '=' }, 2);
                if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return string.Empty;
        }

        private static Dictionary<string, object> ReadJsonBody(HttpListenerContext context)
        {
            if (context.Request.ContentLength64 > 1024 * 1024)
            {
                throw new InvalidDataException("WebUI request body is too large.");
            }

            using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                return Json.Deserialize(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
        }

        private Dictionary<string, object> GetLatestState()
        {
            lock (stateGate)
            {
                return Json.Deserialize(latestStateJson) as Dictionary<string, object>
                    ?? new Dictionary<string, object>();
            }
        }

        private string GetLatestStateJson()
        {
            lock (stateGate)
            {
                return latestStateJson;
            }
        }

        private Dictionary<string, object> StateResponse()
        {
            return new Dictionary<string, object> { ["state"] = GetLatestState() };
        }

        private CommandResult StateResult()
        {
            PublishState();
            return new CommandResult(200, StateResponse());
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { ["error"] = message };
        }

        private static void WriteJson(HttpListenerContext context, int statusCode, Dictionary<string, object> body)
        {
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = data.Length;
            context.Response.OutputStream.Write(data, 0, data.Length);
            context.Response.Close();
        }

        private static void WriteSse(StreamWriter writer, string eventName, string data)
        {
            writer.Write(WebUiEventHub.FormatEvent(eventName, data));
        }

        private static string GetContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        private static bool GetBool(object value, bool fallback)
        {
            return value is bool boolean ? boolean : fallback;
        }

        private static int GetInt(object value, int fallback)
        {
            if (value is int integer) return integer;
            if (value is long longValue) return (int)longValue;
            if (value is double doubleValue) return (int)doubleValue;
            return fallback;
        }

        private static float GetFloat(object value, float fallback)
        {
            if (value is float single) return single;
            if (value is double doubleValue) return (float)doubleValue;
            if (value is long longValue) return longValue;
            if (value is int integer) return integer;
            return fallback;
        }

        private static string GetString(object value, string fallback)
        {
            return value as string ?? fallback;
        }

        private sealed class MainThreadCommand
        {
            public MainThreadCommand(Func<CommandResult> action)
            {
                Action = action;
                Completion = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Func<CommandResult> Action { get; }

            public TaskCompletionSource<CommandResult> Completion { get; }
        }

        private sealed class CommandResult
        {
            public CommandResult(int statusCode, Dictionary<string, object> body)
            {
                StatusCode = statusCode;
                Body = body;
            }

            public int StatusCode { get; }

            public Dictionary<string, object> Body { get; }
        }
    }
}

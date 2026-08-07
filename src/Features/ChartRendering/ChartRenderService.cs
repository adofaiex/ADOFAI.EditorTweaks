using System;
using System.Collections;
using System.IO;
using System.Threading;
using ADOFAI.EditorTweaks.Api.Rendering;
using ADOFAI.EditorTweaks.Patching;
using UnityEngine;
using ApiChartRenderResult = ADOFAI.EditorTweaks.Api.Rendering.ChartRenderResult;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderService : MonoBehaviour
    {
        private const float ProgressEventIntervalSeconds = 0.1f;
        private static ChartRenderService? instance;
        private ChartRenderSession? session;
        private ChartRenderTask? task;
        private float nextProgressEventTime;

        public static ChartRenderTask? CurrentTask =>
            instance == null || instance.task == null || instance.task.IsTerminal
                ? null
                : instance.task;

        public static bool IsActive => CurrentTask != null;

        public static bool ShowHitJudgments => instance?.task?.Request.ShowHitJudgments ?? Main.Settings.ChartRenderShowHitJudgments;

        public static void Ensure()
        {
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject("ADOFAI.EditorTweaks.ChartRenderService");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ChartRenderService>();
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

        public static ChartRenderAvailability GetAvailability()
        {
            if (Main.Mod == null || instance == null)
            {
                return new ChartRenderAvailability(false, ChartRenderErrorCode.ApiUnavailable, "ADOFAI Editor Tweaks rendering service is not enabled.");
            }

            if (!PatchManager.IsAvailable(PatchFeature.ChartRendering))
            {
                return new ChartRenderAvailability(false, ChartRenderErrorCode.ApiUnavailable, "Chart Rendering is incompatible with the current game version.");
            }

            return new ChartRenderAvailability(true, ChartRenderErrorCode.None, string.Empty);
        }

        public static ChartRenderRequest CreateRequestFromCurrentSettings()
        {
            if (Main.Mod == null)
            {
                return new ChartRenderRequest();
            }

            Settings source = Main.Settings;
            return new ChartRenderRequest
            {
                CaptureSource = ChartRenderOptionValues.NormalizeCaptureSource(source.ChartRenderCaptureSource) == ChartRenderOptionValues.CaptureSourceGameView
                    ? ChartRenderCaptureSource.GameView
                    : ChartRenderCaptureSource.Camera,
                PlaybackMode = ChartRenderPlaybackMode.RendererControlled,
                Range = source.ChartRenderUseSelectedRange
                    ? ChartRenderRangeRequest.CurrentEditorSelection()
                    : ChartRenderRangeRequest.WholeLevel(),
                Width = source.ChartRenderWidth,
                Height = source.ChartRenderHeight,
                FramesPerSecond = source.ChartRenderFps,
                WorkspaceDirectory = source.ChartRenderWorkspaceDirectory,
                OutputDirectory = source.ChartRenderExportDirectory,
                VideoFormat = ParseVideoFormat(source.ChartRenderVideoFormat),
                AudioFormat = ParseAudioFormat(source.ChartRenderAudioFormat),
                EncoderMode = ParseEncoderMode(source.ChartRenderEncoderMode),
                ReadbackFormat = ChartRenderOptionValues.NormalizeCaptureFormat(source.ChartRenderCaptureFormat) == ChartRenderOptionValues.CaptureBgra
                    ? ChartRenderReadbackFormat.Bgra
                    : ChartRenderReadbackFormat.Rgba,
                PreviewMode = ParsePreviewMode(source.ChartRenderPreviewMode),
                Quality = source.ChartRenderCrf,
                BitrateMbps = source.ChartRenderBitrateMbps,
                CustomEncoderPreset = source.ChartRenderPreset,
                CustomMuxArguments = source.ChartRenderCustomMuxArgs,
                CompletionTailSeconds = source.ChartRenderCompletionTailSeconds,
                AudioSyncOffsetMilliseconds = source.ChartRenderAudioSyncOffsetMs,
                ShowHitJudgments = source.ChartRenderShowHitJudgments,
                ShowBuiltInProgressUi = false
            };
        }

        public static ChartRenderStartResult Start(ChartRenderRequest request)
        {
            ChartRenderAvailability availability = GetAvailability();
            if (!availability.Available)
            {
                return new ChartRenderStartResult(availability.ErrorCode, availability.Message);
            }

            if (Thread.CurrentThread.ManagedThreadId != Main.UnityThreadId)
            {
                return new ChartRenderStartResult(ChartRenderErrorCode.WrongThread, "ChartRenderApi.Start must be called from the Unity main thread.");
            }

            if (CurrentTask != null)
            {
                return new ChartRenderStartResult(ChartRenderErrorCode.Busy, "Another chart render task is already running.");
            }

            if (request == null)
            {
                return new ChartRenderStartResult(ChartRenderErrorCode.InvalidRequest, "Render request is null.");
            }

            ChartRenderRequest snapshot = request.Clone();
            if (!TryValidate(snapshot, out ChartRenderRange resolvedRange, out ChartRenderErrorCode errorCode, out string error))
            {
                return new ChartRenderStartResult(errorCode, error);
            }

            ChartRenderTask newTask = new ChartRenderTask(Guid.NewGuid(), snapshot);
            instance!.BeginTask(newTask, CreateConfiguration(snapshot, resolvedRange));
            return new ChartRenderStartResult(newTask);
        }

        private void BeginTask(ChartRenderTask newTask, ChartRenderConfiguration configuration)
        {
            task = newTask;
            session = new ChartRenderSession(Main.Mod!, configuration, newTask);
            nextProgressEventTime = 0f;
            StartCoroutine(RunTaskDeferred());
        }

        private IEnumerator RunTaskDeferred()
        {
            yield return null;
            if (task == null || session == null)
            {
                yield break;
            }

            if (task.IsCancellationRequested)
            {
                ChartRenderTask canceledTask = task;
                canceledTask.CompleteWith(new ApiChartRenderResult(
                    ChartRenderTaskState.Canceled,
                    ChartRenderErrorCode.Canceled,
                    "Canceled.",
                    string.Empty));
                task = null;
                session = null;
                yield break;
            }

            yield return session.Run(OnSessionComplete);
        }

        private void Update()
        {
            if (session == null || task == null || task.IsTerminal)
            {
                return;
            }

            if (session.OutputWidth > 0)
            {
                task.OutputWidth = session.OutputWidth;
                task.OutputHeight = session.OutputHeight;
            }

            if (!string.IsNullOrEmpty(session.OutputPath))
            {
                task.OutputPath = session.OutputPath;
            }

            if (Time.realtimeSinceStartup < nextProgressEventTime)
            {
                return;
            }

            nextProgressEventTime = Time.realtimeSinceStartup + ProgressEventIntervalSeconds;
            PublishProgress();
        }

        private void PublishProgress()
        {
            if (session == null || task == null)
            {
                return;
            }

            task.SetProgress(new ChartRenderProgress(
                session.Progress,
                session.WrittenFrames,
                session.TotalFrames,
                session.DuplicateFrames,
                session.ProcessingFps,
                session.EstimatedRemaining,
                session.StageText,
                session.DetailText,
                session.EncoderName,
                session.MemoryBudgetText,
                session.QueueBudgetText));
        }

        private void OnSessionComplete(ChartRenderResult internalResult)
        {
            if (task == null)
            {
                return;
            }

            PublishProgress();
            task.OutputWidth = session?.OutputWidth ?? task.OutputWidth;
            task.OutputHeight = session?.OutputHeight ?? task.OutputHeight;
            task.OutputPath = internalResult.OutputPath;

            ChartRenderTaskState finalState;
            ChartRenderErrorCode errorCode;
            if (internalResult.Canceled || task.IsCancellationRequested)
            {
                finalState = ChartRenderTaskState.Canceled;
                errorCode = internalResult.ErrorCode == ChartRenderErrorCode.ModDisabled
                    ? ChartRenderErrorCode.ModDisabled
                    : ChartRenderErrorCode.Canceled;
            }
            else if (internalResult.Success)
            {
                finalState = ChartRenderTaskState.Completed;
                errorCode = ChartRenderErrorCode.None;
            }
            else
            {
                finalState = ChartRenderTaskState.Failed;
                errorCode = internalResult.ErrorCode == ChartRenderErrorCode.None
                    ? ChartRenderErrorCode.RenderingFailed
                    : internalResult.ErrorCode;
            }

            ApiChartRenderResult result = new ApiChartRenderResult(finalState, errorCode, internalResult.Message, internalResult.OutputPath);
            ChartRenderTask completedTask = task;
            completedTask.CompleteWith(result);
            task = null;
            session = null;
        }

        private void Shutdown()
        {
            if (task == null || task.IsTerminal)
            {
                StopAllCoroutines();
                return;
            }

            ChartRenderTask shutdownTask = task;
            shutdownTask.Cancel();
            session?.AbortNow(ChartRenderErrorCode.ModDisabled, "ADOFAI Editor Tweaks was disabled.");
            StopAllCoroutines();
            if (!shutdownTask.IsTerminal)
            {
                shutdownTask.CompleteWith(new ApiChartRenderResult(
                    ChartRenderTaskState.Canceled,
                    ChartRenderErrorCode.ModDisabled,
                    "ADOFAI Editor Tweaks was disabled.",
                    string.Empty));
            }

            task = null;
            session = null;
        }

        private static bool TryValidate(
            ChartRenderRequest request,
            out ChartRenderRange resolvedRange,
            out ChartRenderErrorCode errorCode,
            out string error)
        {
            resolvedRange = null!;
            errorCode = ChartRenderErrorCode.InvalidRequest;
            error = string.Empty;

            if (!Enum.IsDefined(typeof(ChartRenderCaptureSource), request.CaptureSource)
                || !Enum.IsDefined(typeof(ChartRenderPlaybackMode), request.PlaybackMode)
                || !Enum.IsDefined(typeof(ChartRenderRangeMode), request.Range.Mode)
                || !Enum.IsDefined(typeof(ChartRenderVideoFormat), request.VideoFormat)
                || !Enum.IsDefined(typeof(ChartRenderAudioFormat), request.AudioFormat)
                || !Enum.IsDefined(typeof(ChartRenderEncoderMode), request.EncoderMode)
                || !Enum.IsDefined(typeof(ChartRenderReadbackFormat), request.ReadbackFormat)
                || !Enum.IsDefined(typeof(ChartRenderPreviewMode), request.PreviewMode))
            {
                error = "Render request contains an unsupported enum value.";
                return false;
            }

            if (request.CaptureSource == ChartRenderCaptureSource.Camera
                && (request.Width < 16 || request.Width > 7680 || request.Height < 16 || request.Height > 4320))
            {
                error = "Camera output size must be between 16x16 and 7680x4320.";
                return false;
            }

            if (request.CaptureSource == ChartRenderCaptureSource.Camera
                && (((request.Width & 1) != 0) || ((request.Height & 1) != 0)))
            {
                error = "Camera output width and height must be even.";
                return false;
            }

            if (request.FramesPerSecond < 1 || request.FramesPerSecond > 240)
            {
                error = "FramesPerSecond must be between 1 and 240.";
                return false;
            }

            if (request.Quality < 0 || request.Quality > 51 || request.BitrateMbps < 0 || request.BitrateMbps > 300)
            {
                error = "Quality or bitrate is outside the supported range.";
                return false;
            }

            if (request.CompletionTailSeconds < 0f
                || request.AudioSyncOffsetMilliseconds < -5000f
                || request.AudioSyncOffsetMilliseconds > 5000f)
            {
                error = "Tail seconds or audio sync offset is outside the supported range.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.WorkspaceDirectory) || string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                error = "WorkspaceDirectory and OutputDirectory are required.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(request.OutputFileName)
                && (request.OutputFileName.IndexOfAny(new[] { '/', '\\' }) >= 0))
            {
                error = "OutputFileName must not contain a directory separator.";
                return false;
            }

            if (!ChartRenderSession.IsPlayableLevelLoaded())
            {
                errorCode = ChartRenderErrorCode.NoPlayableLevel;
                error = "No playable level is loaded.";
                return false;
            }

            if (!ChartRenderSession.HasRenderableAudio())
            {
                errorCode = ChartRenderErrorCode.NoRenderableAudio;
                error = "The loaded level has no renderable audio.";
                return false;
            }

            if (request.PlaybackMode == ChartRenderPlaybackMode.AttachToCurrentPlayback)
            {
                if (request.Range.Mode != ChartRenderRangeMode.CurrentPlaybackToEnd)
                {
                    error = "AttachToCurrentPlayback requires CurrentPlaybackToEnd range.";
                    return false;
                }

                if (!ChartRenderPlaybackController.IsPlaybackScheduled())
                {
                    errorCode = ChartRenderErrorCode.PlaybackNotActive;
                    error = "Playback must already be active before using AttachToCurrentPlayback.";
                    return false;
                }
            }
            else if (request.Range.Mode == ChartRenderRangeMode.CurrentPlaybackToEnd)
            {
                error = "CurrentPlaybackToEnd range requires AttachToCurrentPlayback.";
                return false;
            }

            if (request.Range.Mode == ChartRenderRangeMode.CurrentEditorSelection
                && !ChartRenderRange.TryGetEditorSelectedRange(out _, out _, out _))
            {
                error = "The editor does not have a valid continuous floor selection.";
                return false;
            }

            if (request.Range.Mode == ChartRenderRangeMode.ExplicitFloors
                && (request.Range.StartFloor < 0 || request.Range.EndFloor <= request.Range.StartFloor))
            {
                error = "Explicit floor range is invalid.";
                return false;
            }

            try
            {
                resolvedRange = ChartRenderRange.CreateFromRequest(request.Range);
            }
            catch (Exception exception)
            {
                error = exception.GetBaseException().Message;
                return false;
            }

            if (!File.Exists(ChartRenderPaths.GetFfmpegPath()))
            {
                errorCode = ChartRenderErrorCode.InitializationFailed;
                error = "The bundled FFmpeg executable is missing.";
                return false;
            }

            errorCode = ChartRenderErrorCode.None;
            return true;
        }

        private static ChartRenderConfiguration CreateConfiguration(ChartRenderRequest request, ChartRenderRange resolvedRange)
        {
            Settings settings = new Settings
            {
                ChartRenderWorkspaceDirectory = request.WorkspaceDirectory,
                ChartRenderExportDirectory = request.OutputDirectory,
                ChartRenderWidth = request.Width,
                ChartRenderHeight = request.Height,
                ChartRenderFps = request.FramesPerSecond,
                ChartRenderCrf = request.Quality,
                ChartRenderBitrateMbps = request.BitrateMbps,
                ChartRenderPreset = request.CustomEncoderPreset,
                ChartRenderEncoderMode = ToInternal(request.EncoderMode),
                ChartRenderCaptureFormat = request.ReadbackFormat == ChartRenderReadbackFormat.Bgra
                    ? ChartRenderOptionValues.CaptureBgra
                    : ChartRenderOptionValues.CaptureRgba,
                ChartRenderCaptureSource = request.CaptureSource == ChartRenderCaptureSource.GameView
                    ? ChartRenderOptionValues.CaptureSourceGameView
                    : ChartRenderOptionValues.CaptureSourceCamera,
                ChartRenderPreviewMode = ToInternal(request.PreviewMode),
                ChartRenderAudioFormat = ToInternal(request.AudioFormat),
                ChartRenderVideoFormat = ToInternal(request.VideoFormat),
                ChartRenderCompletionTailSeconds = request.CompletionTailSeconds,
                ChartRenderAudioSyncOffsetMs = request.AudioSyncOffsetMilliseconds,
                ChartRenderShowHitJudgments = request.ShowHitJudgments,
                ChartRenderUseSelectedRange = request.Range.Mode != ChartRenderRangeMode.WholeLevel,
                ChartRenderCustomMuxArgs = request.CustomMuxArguments
            };
            return new ChartRenderConfiguration(request, settings, resolvedRange);
        }

        private static string ToInternal(ChartRenderEncoderMode value)
        {
            switch (value)
            {
                case ChartRenderEncoderMode.Fastest: return ChartRenderOptionValues.EncoderFastest;
                case ChartRenderEncoderMode.Balanced: return ChartRenderOptionValues.EncoderBalanced;
                case ChartRenderEncoderMode.Quality: return ChartRenderOptionValues.EncoderQuality;
                case ChartRenderEncoderMode.CpuCompatibility: return ChartRenderOptionValues.EncoderCpuCompatibility;
                case ChartRenderEncoderMode.Custom: return ChartRenderOptionValues.EncoderCustom;
                default: return ChartRenderOptionValues.EncoderAutoBalanced;
            }
        }

        private static string ToInternal(ChartRenderPreviewMode value)
        {
            switch (value)
            {
                case ChartRenderPreviewMode.Dim: return ChartRenderOptionValues.PreviewDim;
                case ChartRenderPreviewMode.Minimal: return ChartRenderOptionValues.PreviewMinimal;
                default: return ChartRenderOptionValues.PreviewFull;
            }
        }

        private static string ToInternal(ChartRenderAudioFormat value)
        {
            switch (value)
            {
                case ChartRenderAudioFormat.Flac: return ChartRenderOptionValues.AudioFormatFlac;
                case ChartRenderAudioFormat.Alac: return ChartRenderOptionValues.AudioFormatAlac;
                default: return ChartRenderOptionValues.AudioFormatAac;
            }
        }

        private static string ToInternal(ChartRenderVideoFormat value)
        {
            switch (value)
            {
                case ChartRenderVideoFormat.Mkv: return ChartRenderOptionValues.VideoFormatMkv;
                case ChartRenderVideoFormat.Mov: return ChartRenderOptionValues.VideoFormatMov;
                default: return ChartRenderOptionValues.VideoFormatMp4;
            }
        }

        private static ChartRenderEncoderMode ParseEncoderMode(string value)
        {
            switch (ChartRenderOptionValues.NormalizeEncoderMode(value))
            {
                case ChartRenderOptionValues.EncoderFastest: return ChartRenderEncoderMode.Fastest;
                case ChartRenderOptionValues.EncoderBalanced: return ChartRenderEncoderMode.Balanced;
                case ChartRenderOptionValues.EncoderQuality: return ChartRenderEncoderMode.Quality;
                case ChartRenderOptionValues.EncoderCpuCompatibility: return ChartRenderEncoderMode.CpuCompatibility;
                case ChartRenderOptionValues.EncoderCustom: return ChartRenderEncoderMode.Custom;
                default: return ChartRenderEncoderMode.AutoBalanced;
            }
        }

        private static ChartRenderAudioFormat ParseAudioFormat(string value)
        {
            switch (ChartRenderOptionValues.NormalizeAudioFormat(value))
            {
                case ChartRenderOptionValues.AudioFormatFlac: return ChartRenderAudioFormat.Flac;
                case ChartRenderOptionValues.AudioFormatAlac: return ChartRenderAudioFormat.Alac;
                default: return ChartRenderAudioFormat.Aac;
            }
        }

        private static ChartRenderVideoFormat ParseVideoFormat(string value)
        {
            switch (ChartRenderOptionValues.NormalizeVideoFormat(value))
            {
                case ChartRenderOptionValues.VideoFormatMkv: return ChartRenderVideoFormat.Mkv;
                case ChartRenderOptionValues.VideoFormatMov: return ChartRenderVideoFormat.Mov;
                default: return ChartRenderVideoFormat.Mp4;
            }
        }

        private static ChartRenderPreviewMode ParsePreviewMode(string value)
        {
            switch (ChartRenderOptionValues.NormalizePreviewMode(value))
            {
                case ChartRenderOptionValues.PreviewDim: return ChartRenderPreviewMode.Dim;
                case ChartRenderOptionValues.PreviewMinimal: return ChartRenderPreviewMode.Minimal;
                default: return ChartRenderPreviewMode.Full;
            }
        }
    }
}

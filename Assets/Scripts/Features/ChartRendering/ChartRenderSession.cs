using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using EditorTweaks.Api.Rendering;
using EditorTweaks.Features.VideoBackgroundSync;
using UnityEngine;
using UnityModManagerNet;

namespace EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderSession
    {
        private const double CompletionFallbackSeconds = 30.0;

        private readonly UnityModManager.ModEntry modEntry;
        private readonly ChartRenderConfiguration configuration;
        private readonly ChartRenderTask apiTask;
        private readonly Settings settings;
        private readonly ChartRenderProgressModel progress = new ChartRenderProgressModel();
        private readonly ChartRenderPlaybackController playbackController;
        private readonly string captureSource;

        private bool cancelRequested;
        private bool finished;
        private double renderDurationSeconds = 1.0;
        private string tempDirectory = string.Empty;
        private string tempVideoPath = string.Empty;
        private string capturedAudioPath = string.Empty;
        private string outputPath = string.Empty;
        private int outputWidth;
        private int outputHeight;
        private readonly ChartRenderAudioBuffer audioBuffer = new ChartRenderAudioBuffer();
        private ChartUnityAudioCapture? audioCapture;
        private IChartFrameCapture? frameCapture;
        private FfmpegEncoder? encoder;
        private ChartRenderFramePipeline? framePipeline;
        private Action<ChartRenderResult>? completionCallback;
        private ChartRenderRange renderRange = ChartRenderRange.WholeLevel();
        private bool renderAutoPlaybackEnabled;

        public ChartRenderSession(UnityModManager.ModEntry modEntry, ChartRenderConfiguration configuration, ChartRenderTask apiTask)
        {
            this.modEntry = modEntry;
            this.configuration = configuration;
            this.apiTask = apiTask;
            settings = configuration.Settings;
            captureSource = ChartRenderOptionValues.NormalizeCaptureSource(settings.ChartRenderCaptureSource);
            playbackController = new ChartRenderPlaybackController(settings);
        }

        public bool IsActive { get; private set; }

        public static bool IsRendering { get; private set; }

        public static bool IsAutoPlaybackReady { get; private set; }

        public static int AutoPlaybackEndFloor { get; private set; } = int.MaxValue;

        public float Progress => progress.Progress;

        public string StageText { get; private set; } = Settings.Text("chartRendererRendering");

        public string DetailText { get; private set; } = string.Empty;

        public string EncoderName { get; private set; } = string.Empty;

        public int WrittenFrames => progress.WrittenFrames;

        public int TotalFrames => progress.TotalFrames;

        public int DuplicateFrames => progress.DuplicateFrames;

        public float DuplicateRatio => progress.DuplicateRatio;

        public double ProcessingFps => progress.ProcessingFps;

        public TimeSpan EstimatedRemaining => progress.EstimatedRemaining;

        public string SmoothnessText => progress.SmoothnessText;

        public string MemoryBudgetText { get; private set; } = string.Empty;

        public string QueueBudgetText { get; private set; } = string.Empty;

        public bool CapturesGameView => captureSource == ChartRenderOptionValues.CaptureSourceGameView;

        public int OutputWidth => outputWidth;

        public int OutputHeight => outputHeight;

        public string OutputPath => outputPath;

        public void Cancel()
        {
            cancelRequested = true;
        }

        private bool IsCancellationRequested => cancelRequested || apiTask.IsCancellationRequested;

        public IEnumerator Run(Action<ChartRenderResult> onComplete)
        {
            completionCallback = onComplete;
            IsActive = true;
            IsRendering = true;
            IsAutoPlaybackReady = false;
            AutoPlaybackEndFloor = int.MaxValue;
            renderAutoPlaybackEnabled = false;
            ChartRenderCustomFrameRate.Begin();
            progress.Reset();
            MemoryBudgetText = string.Empty;
            QueueBudgetText = string.Empty;

            ChartRenderResult result = new ChartRenderResult();
            Exception? failure;

            if (!TryPrepare(result, out failure))
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.InitializationFailed;
                result.Message = failure?.Message ?? result.Message;
                Finish(onComplete, result);
                yield break;
            }

            if (!Try(() => audioBuffer.Begin(), out failure))
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.InitializationFailed;
                result.Message = failure?.Message ?? "Failed to configure render audio.";
                Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            yield return null;

            apiTask.SetState(ChartRenderTaskState.WaitingForPlayback);
            if (!TryStartPlayback(out failure))
            {
                result.Success = false;
                result.ErrorCode = configuration.PlaybackMode == ChartRenderPlaybackMode.AttachToCurrentPlayback
                    ? ChartRenderErrorCode.PlaybackNotActive
                    : ChartRenderErrorCode.InitializationFailed;
                result.Message = failure?.Message ?? "Failed to start level playback.";
                Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            int waitFrames = 0;
            while (!IsCancellationRequested && waitFrames < settings.ChartRenderFps * 10)
            {
                if (ADOBase.conductor != null && IsPlaybackScheduled())
                {
                    break;
                }

                waitFrames++;
                DetailText = "Waiting for level playback...";
                yield return null;
            }

            if (IsCancellationRequested)
            {
                result.Success = false;
                result.Message = "Canceled.";
                result.Canceled = true;
                result.ErrorCode = ChartRenderErrorCode.Canceled;
                Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            if (ADOBase.conductor == null || !IsPlaybackScheduled())
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.PlaybackNotActive;
                result.Message = "Timed out while waiting for level playback.";
                Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            if (renderRange.IsPartial)
            {
                int readyWaitFrames = 0;
                int readyWaitLimit = settings.ChartRenderFps * 20;
                while (!IsCancellationRequested && readyWaitFrames < readyWaitLimit && !IsPartialRangeCaptureReady())
                {
                    readyWaitFrames++;
                    StageText = Settings.Text("chartRendererWaitingRangeStart");
                    DetailText = renderRange.DisplayText;
                    yield return null;
                }

                if (IsCancellationRequested)
                {
                    result.Success = false;
                    result.Message = "Canceled.";
                    result.Canceled = true;
                    result.ErrorCode = ChartRenderErrorCode.Canceled;
                    Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                    Finish(onComplete, result);
                    yield break;
                }

                if (!IsPartialRangeCaptureReady())
                {
                    result.Success = false;
                    result.ErrorCode = ChartRenderErrorCode.InitializationFailed;
                    result.Message = Settings.Text("chartRendererRangeStartTimeout");
                    Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                    Finish(onComplete, result);
                    yield break;
                }

                StageText = Settings.Text("chartRendererRendering");
                DetailText = Path.GetFileName(outputPath);
                WriteLog("Partial range capture begins. state=" + (ADOBase.controller == null ? "null" : ADOBase.controller.state.ToString())
                    + " songposition=" + Number(ADOBase.conductor == null ? double.NaN : ADOBase.conductor.songposition_minusi)
                    + " currentSeq=" + (ADOBase.controller == null ? -1 : ADOBase.controller.currentSeqID)
                    + " floor=" + GetPrimaryPlayerFloor() + ".");
            }

            Time.captureFramerate = Math.Max(1, settings.ChartRenderFps);
            if (VideoBackgroundSyncPatches.HasActiveVideoBackground())
            {
                Application.targetFrameRate = Math.Max(1, settings.ChartRenderFps);
                WriteLog("Video background detected. Render processing capped to "
                    + Application.targetFrameRate + " FPS for continuous decoding.");
            }
            else
            {
                Application.targetFrameRate = Math.Max(1000, settings.ChartRenderFps * 4);
            }

            int fps = Math.Max(1, settings.ChartRenderFps);
            double completionTailSeconds = GetEffectiveCompletionTailSeconds();
            int completionTailFrames = Mathf.Max(0, Mathf.CeilToInt((float)(completionTailSeconds * fps)));
            if (!Try(() =>
            {
                bool showPreview = captureSource == ChartRenderOptionValues.CaptureSourceCamera
                    && ChartRenderOptionValues.NormalizePreviewMode(settings.ChartRenderPreviewMode) != ChartRenderOptionValues.PreviewMinimal;
                frameCapture = ChartFrameCaptureFactory.Create(settings, captureSource, showPreview);
                outputWidth = frameCapture.Width;
                outputHeight = frameCapture.Height;
                ChartRenderMemoryBudget budget = ChartRenderMemoryBudget.Create(frameCapture.Width, frameCapture.Height);
                MemoryBudgetText = budget.DisplaySummary;
                QueueBudgetText = budget.QueueSummary;
                renderDurationSeconds = CalculateTotalDuration();
                int totalFrames = Math.Max(1, Mathf.CeilToInt((float)(renderDurationSeconds * fps)));
                progress.SetTotalFrames(totalFrames);
                audioCapture = new ChartUnityAudioCapture(capturedAudioPath, fps);
                audioCapture.Begin();
                framePipeline = new ChartRenderFramePipeline(budget);
                encoder = new FfmpegEncoder(
                    ChartRenderPaths.GetFfmpegPath(),
                    tempVideoPath,
                    outputPath,
                    frameCapture.Width,
                    frameCapture.Height,
                    settings.ChartRenderFps,
                    settings.ChartRenderCrf,
                    settings.ChartRenderBitrateMbps,
                    settings.ChartRenderEncoderMode,
                    settings.ChartRenderPreset,
                    frameCapture.PixelFormatName,
                    frameCapture.RequiresVerticalFlip,
                    budget.MaxEncoderQueueFrames,
                    settings.ChartRenderAudioSyncOffsetMs,
                    settings.ChartRenderAudioFormat,
                    settings.ChartRenderCustomMuxArgs);
                encoder.BeginVideo();
                EncoderName = encoder.EncoderName;
                BeginForcedVisualClock();
                SetForcedFrameTimeFromAudioCursor(0);
                AutoPlaybackEndFloor = renderRange.AutoPlaybackEndFloor;
                if (configuration.PlaybackMode == ChartRenderPlaybackMode.RendererControlled)
                {
                    EnableRenderAutoPlayback();
                }
                else if (configuration.PlaybackMode == ChartRenderPlaybackMode.RestartWithoutAutoPlay)
                {
                    RDC.auto = false;
                    IsAutoPlaybackReady = false;
                    renderAutoPlaybackEnabled = false;
                    WriteLog("Internal auto playback disabled for RestartWithoutAutoPlay.");
                }
                else
                {
                    IsAutoPlaybackReady = false;
                    renderAutoPlaybackEnabled = false;
                    WriteLog("Attached to current playback without changing its auto-play state.");
                }

                apiTask.SetState(ChartRenderTaskState.Rendering);
                ChartRenderDiagnostics.LogFrame(0, 0);
            }, out failure))
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.InitializationFailed;
                result.Message = failure?.Message ?? "Failed to initialize renderer.";
                Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            int requestedFrames = 0;
            int completionFrame = -1;
            int manualCompletionTailFrames = Mathf.Max(0, Mathf.CeilToInt(Math.Max(0f, settings.ChartRenderCompletionTailSeconds) * fps));
            int fallbackExtraFrames = Math.Max(completionTailFrames, Mathf.CeilToInt((float)(CompletionFallbackSeconds * fps)));
            int renderFrameLimit = progress.TotalFrames + fallbackExtraFrames;
            bool estimateExpandedToFallback = false;
            WriteLog("Render estimate: " + progress.TotalFrames + " frames, fallback limit: " + renderFrameLimit + " frames. " + MemoryBudgetText + "; " + QueueBudgetText);
            DetailText = Path.GetFileName(outputPath);
            yield return null;

            while (requestedFrames < renderFrameLimit && !IsCancellationRequested && failure == null)
            {
                ChartRenderDiagnostics.SetFrame(requestedFrames);
                if (!Try(() => framePipeline!.WaitForPendingSlot(encoder!, () => IsCancellationRequested), out failure))
                {
                    break;
                }

                if (failure != null || IsCancellationRequested)
                {
                    break;
                }

                yield return new WaitForEndOfFrame();

                if (!Try(() =>
                {
                    audioCapture!.CaptureFrame();
                    if (ChartRenderCustomFrameRate.ShouldCaptureFrame())
                    {
                        framePipeline!.RequestFrame(frameCapture!, requestedFrames);
                    }
                    else
                    {
                        framePipeline!.RepeatLastFrame(requestedFrames);
                    }

                    requestedFrames++;
                    SetForcedFrameTimeFromAudioCursor(requestedFrames);
                    ChartRenderDiagnostics.LogFrame(requestedFrames, renderFrameLimit);
                    DrainReadyFrames(framePipeline!, encoder!);
                    if (completionFrame < 0 && apiTask.IsFinishRequested)
                    {
                        completionFrame = requestedFrames;
                        DisableRenderAutoPlayback(resetEndFloor: false);
                        renderFrameLimit = completionFrame + manualCompletionTailFrames;
                        progress.SetTotalFrames(renderFrameLimit);
                        WriteLog("Caller requested completion at frame " + completionFrame + "; rendering tail to frame " + renderFrameLimit + ".");
                    }
                    else if (completionFrame < 0 && HasReachedLevelEnd())
                    {
                        completionFrame = requestedFrames;
                        if (renderRange.IsPartial)
                        {
                            PauseRenderAutoPlaybackForTail();
                        }
                        else
                        {
                            DisableRenderAutoPlayback(resetEndFloor: false);
                        }

                        renderFrameLimit = completionFrame + completionTailFrames;
                        progress.SetTotalFrames(renderFrameLimit);
                        WriteLog("Level end detected at frame " + completionFrame + "; rendering tail to frame " + renderFrameLimit + ".");
                    }
                    else if (completionFrame < 0 && requestedFrames >= progress.TotalFrames)
                    {
                        if (!estimateExpandedToFallback)
                        {
                            estimateExpandedToFallback = true;
                            progress.SetTotalFrames(renderFrameLimit);
                            WriteLog("Render estimate reached before end detection; using fallback frame limit " + renderFrameLimit + ".");
                        }
                    }
                }, out failure))
                {
                    break;
                }

                DetailText = Path.GetFileName(outputPath);
            }

            while (framePipeline != null && framePipeline.PendingCount > 0 && !IsCancellationRequested && failure == null)
            {
                if (!Try(() => DrainReadyFrames(framePipeline!, encoder!), out failure))
                {
                    break;
                }

                yield return null;
            }

            frameCapture?.Dispose();
            frameCapture = null;
            audioCapture?.Complete();
            if (audioCapture != null)
            {
                double videoSeconds = requestedFrames / (double)Math.Max(1, settings.ChartRenderFps);
                WriteLog("Audio capture complete. samples=" + audioCapture.CapturedSampleFrames
                    + " audioSeconds=" + Number(audioCapture.CapturedSeconds)
                    + " videoFrames=" + requestedFrames
                    + " videoSeconds=" + Number(videoSeconds)
                    + " deltaAudioMinusVideo=" + Number(audioCapture.CapturedSeconds - videoSeconds) + ".");
            }

            audioCapture?.Dispose();
            audioBuffer.Restore();
            ChartRenderVisualClock.End();
            RestoreState();

            if (IsCancellationRequested)
            {
                result.Success = false;
                result.Message = "Canceled.";
                result.Canceled = true;
                result.ErrorCode = ChartRenderErrorCode.Canceled;
                Cleanup(frameCapture, encoder, restoreEditor: false, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            if (failure != null)
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.RenderingFailed;
                result.Message = failure.Message;
                Cleanup(frameCapture, encoder, restoreEditor: false, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            if (audioCapture == null || audioCapture.CapturedSampleFrames <= 0)
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.RenderingFailed;
                result.Message = "Unity AudioRenderer captured no audio samples.";
                Cleanup(frameCapture, encoder, restoreEditor: false, deleteTemp: true);
                Finish(onComplete, result);
                yield break;
            }

            StageText = "Finalizing video";
            apiTask.SetState(ChartRenderTaskState.Finalizing);
            DetailText = Path.GetFileName(tempVideoPath);
            bool backgroundOk = false;
            yield return RunBackground(() => encoder!.CompleteVideo(), encoder, result, deleteTempOnCancel: true, ok => backgroundOk = ok);
            if (!backgroundOk)
            {
                Finish(onComplete, result);
                yield break;
            }

            StageText = Settings.Text("chartRendererMuxing");
            DetailText = Path.GetFileName(outputPath);
            backgroundOk = false;
            yield return RunBackground(() => encoder!.MuxAudioFile(capturedAudioPath), encoder, result, deleteTempOnCancel: true, deleteTempOnFailure: false, onDone: ok => backgroundOk = ok);
            if (!backgroundOk)
            {
                Finish(onComplete, result);
                yield break;
            }

            Cleanup(frameCapture, encoder, restoreEditor: false, deleteTemp: false);
            result.Success = true;
            result.OutputPath = outputPath;
            result.Message = "OK";
            WriteLog("Render complete: " + outputPath);
            Finish(onComplete, result);
        }

        private IEnumerator RunBackground(Action action, FfmpegEncoder? encoder, ChartRenderResult result, bool deleteTempOnCancel, Action<bool> onDone)
        {
            return RunBackground(action, encoder, result, deleteTempOnCancel, deleteTempOnFailure: true, onDone);
        }

        private IEnumerator RunBackground(Action action, FfmpegEncoder? encoder, ChartRenderResult result, bool deleteTempOnCancel, bool deleteTempOnFailure, Action<bool> onDone)
        {
            Exception? backgroundFailure = null;
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    backgroundFailure = ex;
                }
                finally
                {
                    done.Set();
                }
            })
            {
                IsBackground = true,
                Name = "EditorTweaks.ChartRenderWorker"
            };

            thread.Start();
            bool canceled = false;
            while (!done.IsSet)
            {
                if (!canceled && IsCancellationRequested)
                {
                    canceled = true;
                    encoder?.Dispose();
                    result.Success = false;
                    result.Message = "Canceled.";
                    result.Canceled = true;
                    result.ErrorCode = ChartRenderErrorCode.Canceled;
                }

                yield return null;
            }

            thread.Join();
            done.Dispose();
            if (canceled)
            {
                if (deleteTempOnCancel)
                {
                    DeleteTempDirectory();
                }

                onDone(false);
                yield break;
            }

            if (backgroundFailure != null)
            {
                result.Success = false;
                result.ErrorCode = ChartRenderErrorCode.RenderingFailed;
                result.Message = backgroundFailure.Message;
                Cleanup(null, encoder, restoreEditor: false, deleteTemp: deleteTempOnFailure);
                onDone(false);
                yield break;
            }

            onDone(true);
        }

        private void Finish(Action<ChartRenderResult> onComplete, ChartRenderResult result)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            DisableRenderAutoPlayback(resetEndFloor: true);
            audioBuffer.Restore();
            IsActive = false;
            VideoBackgroundSyncPatches.RestoreRenderSettings();
            IsRendering = false;
            ChartRenderCustomFrameRate.End();
            ChartRenderVisualClock.End();
            RestoreState();
            ChartRenderDiagnostics.End();
            onComplete(result);
        }

        private void DrainReadyFrames(ChartRenderFramePipeline framePipeline, FfmpegEncoder encoder)
        {
            framePipeline.DrainReadyFrames(encoder, () => IsCancellationRequested);
            progress.UpdateFrames(framePipeline.WrittenFrames, framePipeline.DuplicateFrames);
        }

        private bool TryPrepare(ChartRenderResult result, out Exception? failure)
        {
            failure = null;
            return Try(() =>
            {
                settings.EnsureDefaults(modEntry);
                string workspace = ChartRenderPaths.GetWorkspaceDirectory(settings);
                string export = ChartRenderPaths.GetExportDirectory(settings);
                Directory.CreateDirectory(workspace);
                Directory.CreateDirectory(export);

                tempDirectory = Path.Combine(workspace, "CurrentRender");
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }

                Directory.CreateDirectory(tempDirectory);
                tempVideoPath = Path.Combine(tempDirectory, "temp_video.mp4");
                capturedAudioPath = Path.Combine(tempDirectory, "audio.wav");
                ChartRenderDiagnostics.Begin(Path.Combine(tempDirectory, "render.log"));
                renderRange = configuration.Range;
                WriteLog("Capture source locked for this render: " + captureSource + ".");

                string extension = ChartRenderOptionValues.GetVideoFormatExtension(settings.ChartRenderVideoFormat);
                string requestedName = configuration.OutputFileName;
                string baseName = string.IsNullOrWhiteSpace(requestedName)
                    ? ChartRenderPaths.MakeSafeFileName(GetLevelName())
                        + renderRange.FileNameSuffix
                        + "_"
                        + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                    : ChartRenderPaths.MakeSafeFileName(Path.GetFileNameWithoutExtension(requestedName));
                outputPath = GetUniqueOutputPath(export, baseName, extension);
                result.OutputPath = outputPath;
                WriteLog("Render range: " + renderRange.DisplayText
                    + " start=" + renderRange.StartFloor
                    + " end=" + renderRange.EndFloor
                    + " selectedCount=" + renderRange.FloorCount + ".");
            }, out failure);
        }

        private bool TryStartPlayback(out Exception? failure)
        {
            return Try(() => playbackController.StartPlayback(renderRange, configuration.PlaybackMode), out failure);
        }

        public void AbortNow(ChartRenderErrorCode errorCode, string message)
        {
            if (finished)
            {
                return;
            }

            cancelRequested = true;
            ChartRenderResult result = new ChartRenderResult
            {
                Success = false,
                Canceled = true,
                ErrorCode = errorCode,
                Message = message ?? "Canceled.",
                OutputPath = string.Empty
            };
            Cleanup(frameCapture, encoder, restoreEditor: true, deleteTemp: true);
            Finish(completionCallback ?? (_ => { }), result);
        }

        private void EnableRenderAutoPlayback()
        {
            RDC.auto = true;
            IsAutoPlaybackReady = true;
            renderAutoPlaybackEnabled = true;
            WriteLog("Render auto playback enabled after visual clock anchor. songposition="
                + Number(ADOBase.conductor == null ? double.NaN : ADOBase.conductor.songposition_minusi)
                + " currentSeq=" + (ADOBase.controller == null ? -1 : ADOBase.controller.currentSeqID)
                + " floor=" + GetPrimaryPlayerFloor() + ".");
        }

        private void DisableRenderAutoPlayback(bool resetEndFloor)
        {
            if (renderAutoPlaybackEnabled)
            {
                RDC.auto = false;
            }

            renderAutoPlaybackEnabled = false;
            IsAutoPlaybackReady = false;
            if (resetEndFloor)
            {
                AutoPlaybackEndFloor = int.MaxValue;
            }
        }

        private void PauseRenderAutoPlaybackForTail()
        {
            IsAutoPlaybackReady = false;
        }

        internal static bool IsPlayableLevelLoaded()
        {
            return ChartRenderPlaybackController.IsPlayableLevelLoaded();
        }

        internal static bool HasRenderableAudio()
        {
            return ChartRenderPlaybackController.HasRenderableAudio();
        }

        private double CalculateTotalDuration()
        {
            return renderRange.EstimateDurationSeconds(GetEffectiveCompletionTailSeconds());
        }

        private double GetEffectiveCompletionTailSeconds()
        {
            return renderRange.IsPartial ? 0.0 : Math.Max(0f, settings.ChartRenderCompletionTailSeconds);
        }

        private static bool IsPlaybackScheduled()
        {
            return ChartRenderPlaybackController.IsPlaybackScheduled();
        }

        private bool IsPartialRangeCaptureReady()
        {
            if (!renderRange.IsPartial)
            {
                return true;
            }

            if (renderRange.StartFloor <= 0)
            {
                return IsPlaybackScheduled();
            }

            scrController controller = ADOBase.controller;
            scrConductor conductor = ADOBase.conductor;
            if (controller == null || conductor == null || !conductor.hasSongStarted)
            {
                return false;
            }

            if (controller.state != States.PlayerControl)
            {
                return false;
            }

            scrFloor? floor = controller.playerOne == null ? null : controller.playerOne.currFloor;
            if (floor == null)
            {
                return false;
            }

            return floor.seqID >= Math.Max(0, renderRange.StartFloor - 1);
        }

        private bool HasReachedLevelEnd()
        {
            return renderRange.HasReachedEnd();
        }

        private double GetFrameTime(int index)
        {
            return index / (double)Math.Max(1, settings.ChartRenderFps);
        }

        private void SetForcedFrameTimeFromAudioCursor(int index)
        {
            double seconds = audioCapture == null ? GetFrameTime(index) : audioCapture.CapturedSeconds;
            if (seconds <= 0.0 && index > 0)
            {
                seconds = GetFrameTime(index);
            }

            ChartRenderVisualClock.SetFrameTime(seconds, GetRenderPitch());
        }

        private void BeginForcedVisualClock()
        {
            scrConductor conductor = ADOBase.conductor;
            double rawStartSongPosition = conductor == null ? 0.0 : conductor.songposition_minusi;
            double dspStartSongPosition = GetDspSongPosition(conductor);
            double startSongPosition = playbackController.PlaybackStartsAtBeginning
                ? SanitizeBeginningSongPosition(conductor, dspStartSongPosition)
                : dspStartSongPosition;
            ChartRenderVisualClock.Begin(startSongPosition);

            int inputOffsetMs = 0;
            try
            {
                inputOffsetMs = scrConductor.currentPreset.inputOffset;
            }
            catch
            {
            }

            double addOffset = conductor == null ? 0.0 : conductor.addoffset;
            WriteLog("Visual clock anchored to audio DSP at songposition=" + Number(startSongPosition)
                + " raw=" + Number(rawStartSongPosition)
                + " dspFormula=" + Number(dspStartSongPosition)
                + " dspTime=" + Number(conductor == null ? double.NaN : conductor.dspTime)
                + " dspTimeSong=" + Number(conductor == null ? double.NaN : conductor.dspTimeSong)
                + " pitch=" + Number(GetRenderPitch())
                + " addoffset=" + Number(addOffset)
                + " audioSampleRate=" + (audioCapture == null ? -1 : audioCapture.SampleRate)
                + " audioChannels=" + (audioCapture == null ? -1 : audioCapture.ChannelCount)
                + " currentSeq=" + (ADOBase.controller == null ? -1 : ADOBase.controller.currentSeqID)
                + " floor=" + GetPrimaryPlayerFloor()
                + " inputOffsetMs=" + inputOffsetMs + ".");
        }

        private static double GetDspSongPosition(scrConductor? conductor)
        {
            if (conductor == null || conductor.song == null)
            {
                return 0.0;
            }

            return (conductor.dspTime - conductor.dspTimeSong - GetInputOffsetSeconds()) * conductor.song.pitch - conductor.addoffset;
        }

        private static double SanitizeBeginningSongPosition(scrConductor? conductor, double rawStartSongPosition)
        {
            double fallback = GetBeginningSongPosition(conductor);
            if (conductor == null)
            {
                return fallback;
            }

            double upperBound = GetBeginningAnchorUpperBound();
            int floor = GetPrimaryPlayerFloor();
            bool staleTimeline = rawStartSongPosition > upperBound
                || (ADOBase.controller != null && ADOBase.controller.currentSeqID > 1)
                || floor > 1;
            if (!staleTimeline)
            {
                return rawStartSongPosition;
            }

            ChartRenderDiagnostics.Log("Beginning playback had stale songposition=" + Number(rawStartSongPosition)
                + " upperBound=" + Number(upperBound)
                + " currentSeq=" + (ADOBase.controller == null ? -1 : ADOBase.controller.currentSeqID)
                + " floor=" + floor
                + "; using fallback=" + Number(fallback) + ".");
            return fallback;
        }

        private static double GetBeginningSongPosition(scrConductor? conductor)
        {
            if (conductor == null)
            {
                return 0.0;
            }

            double countdownOffset = 0.0;
            try
            {
                countdownOffset = conductor.separateCountdownTime
                    ? conductor.crotchetAtStart * conductor.adjustedCountdownTicks
                    : conductor.addoffset;
            }
            catch
            {
                countdownOffset = conductor.addoffset;
            }

            if (countdownOffset <= 0.000001)
            {
                countdownOffset = conductor.addoffset;
            }

            double pitch = conductor.song == null ? 1.0 : conductor.song.pitch;
            return -Math.Max(0.0, countdownOffset + GetInputOffsetSeconds() * pitch);
        }

        private static double GetInputOffsetSeconds()
        {
            try
            {
                return scrConductor.currentPreset.inputOffset * 0.001;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetBeginningAnchorUpperBound()
        {
            double upperBound = 1.0;
            try
            {
                List<scrFloor>? floors = ADOBase.lm == null ? null : ADOBase.lm.listFloors;
                if (floors != null && floors.Count > 1 && floors[0].nextfloor != null)
                {
                    upperBound = Math.Max(upperBound, floors[0].nextfloor.entryTime + 0.5);
                }
            }
            catch
            {
            }

            return upperBound;
        }

        private static int GetPrimaryPlayerFloor()
        {
            return ChartRenderPlaybackController.GetPrimaryPlayerFloor();
        }

        private float GetRenderPitch()
        {
            return ADOBase.conductor == null || ADOBase.conductor.song == null
                ? 1f
                : ADOBase.conductor.song.pitch;
        }

        private void Cleanup(IChartFrameCapture? frameCapture, FfmpegEncoder? encoder, bool restoreEditor, bool deleteTemp)
        {
            DisableRenderAutoPlayback(resetEndFloor: true);
            frameCapture?.Dispose();
            encoder?.Dispose();
            if (ReferenceEquals(this.frameCapture, frameCapture))
            {
                this.frameCapture = null;
            }

            if (ReferenceEquals(this.encoder, encoder))
            {
                this.encoder = null;
            }

            framePipeline = null;
            audioCapture?.Dispose();
            audioCapture = null;
            audioBuffer.Restore();
            ChartRenderCustomFrameRate.End();
            ChartRenderVisualClock.End();
            ChartRenderDiagnostics.End();

            if (restoreEditor)
            {
                RestoreState();
            }

            if (deleteTemp)
            {
                DeleteTempDirectory();
            }
        }

        private void DeleteTempDirectory()
        {
            if (string.IsNullOrEmpty(tempDirectory) || !Directory.Exists(tempDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
            }
        }

        private static string GetUniqueOutputPath(string directory, string baseName, string extension)
        {
            string candidate = Path.Combine(directory, baseName + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            for (int suffix = 1; suffix < 10000; suffix++)
            {
                candidate = Path.Combine(directory, baseName + "_" + suffix + extension);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("Unable to choose a unique output file name.");
        }

        private void RestoreState()
        {
            playbackController.RestoreState();
        }

        private string GetLevelName()
        {
            scnGame level = ADOBase.editor != null ? ADOBase.editor.customLevel : ADOBase.customLevel;
            if (level != null && level.levelData != null)
            {
                string title = level.levelData.artist + " - " + level.levelData.song;
                if (!string.IsNullOrWhiteSpace(title.Trim(' ', '-')))
                {
                    return title;
                }

                if (!string.IsNullOrWhiteSpace(level.levelPath))
                {
                    return Path.GetFileNameWithoutExtension(level.levelPath);
                }
            }

            scrController controller = ADOBase.controller;
            if (controller != null)
            {
                if (!string.IsNullOrWhiteSpace(controller.caption))
                {
                    return controller.caption;
                }

                if (!string.IsNullOrWhiteSpace(controller.levelName))
                {
                    return controller.levelName;
                }
            }

            if (!string.IsNullOrWhiteSpace(ADOBase.sceneName))
            {
                return ADOBase.sceneName;
            }

            return "ADOFAI_Render";
        }

        private void WriteLog(string message)
        {
            ChartRenderDiagnostics.Log(message);
        }

        private static string Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static bool Try(Action action, out Exception? failure)
        {
            try
            {
                action();
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                Main.Log("Chart render error: " + ex);
                failure = ex;
                return false;
            }
        }
    }
}

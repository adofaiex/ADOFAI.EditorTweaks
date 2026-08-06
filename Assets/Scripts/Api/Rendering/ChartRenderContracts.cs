using System;
using System.Threading;

namespace EditorTweaks.Api.Rendering
{
    public enum ChartRenderCaptureSource
    {
        Camera = 0,
        GameView = 1
    }

    public enum ChartRenderPlaybackMode
    {
        RendererControlled = 0,
        RestartWithoutAutoPlay = 1,
        AttachToCurrentPlayback = 2
    }

    public enum ChartRenderRangeMode
    {
        WholeLevel = 0,
        CurrentEditorSelection = 1,
        ExplicitFloors = 2,
        CurrentPlaybackToEnd = 3
    }

    public enum ChartRenderVideoFormat
    {
        Mp4 = 0,
        Mkv = 1,
        Mov = 2
    }

    public enum ChartRenderAudioFormat
    {
        Aac = 0,
        Flac = 1,
        Alac = 2
    }

    public enum ChartRenderEncoderMode
    {
        AutoBalanced = 0,
        Fastest = 1,
        Balanced = 2,
        Quality = 3,
        CpuCompatibility = 4,
        Custom = 5
    }

    public enum ChartRenderReadbackFormat
    {
        Rgba = 0,
        Bgra = 1
    }

    public enum ChartRenderPreviewMode
    {
        Full = 0,
        Dim = 1,
        Minimal = 2
    }

    public enum ChartRenderTaskState
    {
        Preparing = 0,
        WaitingForPlayback = 1,
        Rendering = 2,
        Finalizing = 3,
        Completed = 4,
        Failed = 5,
        Canceled = 6
    }

    public enum ChartRenderErrorCode
    {
        None = 0,
        ApiUnavailable = 1,
        WrongThread = 2,
        Busy = 3,
        InvalidRequest = 4,
        NoPlayableLevel = 5,
        NoRenderableAudio = 6,
        PlaybackNotActive = 7,
        InitializationFailed = 8,
        RenderingFailed = 9,
        Canceled = 10,
        ModDisabled = 11
    }

    public sealed class ChartRenderRangeRequest
    {
        public ChartRenderRangeMode Mode { get; set; } = ChartRenderRangeMode.WholeLevel;

        public int StartFloor { get; set; }

        public int EndFloor { get; set; }

        public static ChartRenderRangeRequest WholeLevel()
        {
            return new ChartRenderRangeRequest { Mode = ChartRenderRangeMode.WholeLevel };
        }

        public static ChartRenderRangeRequest CurrentEditorSelection()
        {
            return new ChartRenderRangeRequest { Mode = ChartRenderRangeMode.CurrentEditorSelection };
        }

        public static ChartRenderRangeRequest Floors(int startFloor, int endFloor)
        {
            return new ChartRenderRangeRequest
            {
                Mode = ChartRenderRangeMode.ExplicitFloors,
                StartFloor = startFloor,
                EndFloor = endFloor
            };
        }

        public static ChartRenderRangeRequest CurrentPlaybackToEnd()
        {
            return new ChartRenderRangeRequest { Mode = ChartRenderRangeMode.CurrentPlaybackToEnd };
        }

        internal ChartRenderRangeRequest Clone()
        {
            return new ChartRenderRangeRequest
            {
                Mode = Mode,
                StartFloor = StartFloor,
                EndFloor = EndFloor
            };
        }
    }

    public sealed class ChartRenderRequest
    {
        public ChartRenderCaptureSource CaptureSource { get; set; } = ChartRenderCaptureSource.Camera;

        public ChartRenderPlaybackMode PlaybackMode { get; set; } = ChartRenderPlaybackMode.RendererControlled;

        public ChartRenderRangeRequest Range { get; set; } = ChartRenderRangeRequest.WholeLevel();

        public int Width { get; set; } = 1920;

        public int Height { get; set; } = 1080;

        public int FramesPerSecond { get; set; } = 60;

        public string WorkspaceDirectory { get; set; } = string.Empty;

        public string OutputDirectory { get; set; } = string.Empty;

        public string OutputFileName { get; set; } = string.Empty;

        public ChartRenderVideoFormat VideoFormat { get; set; } = ChartRenderVideoFormat.Mp4;

        public ChartRenderAudioFormat AudioFormat { get; set; } = ChartRenderAudioFormat.Aac;

        public ChartRenderEncoderMode EncoderMode { get; set; } = ChartRenderEncoderMode.AutoBalanced;

        public ChartRenderReadbackFormat ReadbackFormat { get; set; } = ChartRenderReadbackFormat.Rgba;

        public ChartRenderPreviewMode PreviewMode { get; set; } = ChartRenderPreviewMode.Full;

        public int Quality { get; set; } = 18;

        public int BitrateMbps { get; set; }

        public string CustomEncoderPreset { get; set; } = "veryfast";

        public string CustomMuxArguments { get; set; } = string.Empty;

        public float CompletionTailSeconds { get; set; } = 5f;

        public float AudioSyncOffsetMilliseconds { get; set; }

        public bool ShowHitJudgments { get; set; } = true;

        public bool ShowBuiltInProgressUi { get; set; } = true;

        internal ChartRenderRequest Clone()
        {
            return new ChartRenderRequest
            {
                CaptureSource = CaptureSource,
                PlaybackMode = PlaybackMode,
                Range = (Range ?? ChartRenderRangeRequest.WholeLevel()).Clone(),
                Width = Width,
                Height = Height,
                FramesPerSecond = FramesPerSecond,
                WorkspaceDirectory = WorkspaceDirectory ?? string.Empty,
                OutputDirectory = OutputDirectory ?? string.Empty,
                OutputFileName = OutputFileName ?? string.Empty,
                VideoFormat = VideoFormat,
                AudioFormat = AudioFormat,
                EncoderMode = EncoderMode,
                ReadbackFormat = ReadbackFormat,
                PreviewMode = PreviewMode,
                Quality = Quality,
                BitrateMbps = BitrateMbps,
                CustomEncoderPreset = CustomEncoderPreset ?? string.Empty,
                CustomMuxArguments = CustomMuxArguments ?? string.Empty,
                CompletionTailSeconds = CompletionTailSeconds,
                AudioSyncOffsetMilliseconds = AudioSyncOffsetMilliseconds,
                ShowHitJudgments = ShowHitJudgments,
                ShowBuiltInProgressUi = ShowBuiltInProgressUi
            };
        }
    }

    public sealed class ChartRenderAvailability
    {
        internal ChartRenderAvailability(bool available, ChartRenderErrorCode errorCode, string message)
        {
            Available = available;
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
        }

        public bool Available { get; }

        public ChartRenderErrorCode ErrorCode { get; }

        public string Message { get; }
    }

    public sealed class ChartRenderProgress
    {
        internal ChartRenderProgress(
            float value,
            int writtenFrames,
            int totalFrames,
            int duplicateFrames,
            double processingFramesPerSecond,
            TimeSpan estimatedRemaining,
            string stage,
            string detail,
            string encoderName,
            string memoryBudget,
            string queueBudget)
        {
            Value = value;
            WrittenFrames = writtenFrames;
            TotalFrames = totalFrames;
            DuplicateFrames = duplicateFrames;
            ProcessingFramesPerSecond = processingFramesPerSecond;
            EstimatedRemaining = estimatedRemaining;
            Stage = stage ?? string.Empty;
            Detail = detail ?? string.Empty;
            EncoderName = encoderName ?? string.Empty;
            MemoryBudget = memoryBudget ?? string.Empty;
            QueueBudget = queueBudget ?? string.Empty;
        }

        public float Value { get; }

        public int WrittenFrames { get; }

        public int TotalFrames { get; }

        public int DuplicateFrames { get; }

        public float DuplicateRatio => WrittenFrames <= 0 ? 0f : DuplicateFrames / (float)WrittenFrames;

        public double ProcessingFramesPerSecond { get; }

        public TimeSpan EstimatedRemaining { get; }

        public string Stage { get; }

        public string Detail { get; }

        public string EncoderName { get; }

        public string MemoryBudget { get; }

        public string QueueBudget { get; }

        internal static ChartRenderProgress Empty()
        {
            return new ChartRenderProgress(0f, 0, 1, 0, 0d, TimeSpan.Zero, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    public sealed class ChartRenderResult
    {
        internal ChartRenderResult(ChartRenderTaskState state, ChartRenderErrorCode errorCode, string message, string outputPath)
        {
            State = state;
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
            OutputPath = outputPath ?? string.Empty;
        }

        public bool Success => State == ChartRenderTaskState.Completed;

        public ChartRenderTaskState State { get; }

        public ChartRenderErrorCode ErrorCode { get; }

        public string Message { get; }

        public string OutputPath { get; }
    }

    public sealed class ChartRenderStartResult
    {
        internal ChartRenderStartResult(ChartRenderTask task)
        {
            Success = true;
            ErrorCode = ChartRenderErrorCode.None;
            Message = string.Empty;
            Task = task;
        }

        internal ChartRenderStartResult(ChartRenderErrorCode errorCode, string message)
        {
            Success = false;
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }

        public ChartRenderErrorCode ErrorCode { get; }

        public string Message { get; }

        public ChartRenderTask? Task { get; }
    }

    public sealed class ChartRenderTask
    {
        private int cancelRequested;
        private int finishRequested;
        private ChartRenderProgress progress = ChartRenderProgress.Empty();
        private int state = (int)ChartRenderTaskState.Preparing;

        internal ChartRenderTask(Guid id, ChartRenderRequest request)
        {
            Id = id;
            Request = request;
            CaptureSource = request.CaptureSource;
            ShowBuiltInProgressUi = request.ShowBuiltInProgressUi;
        }

        public event Action<ChartRenderTask>? StateChanged;

        public event Action<ChartRenderTask>? ProgressChanged;

        public event Action<ChartRenderTask, ChartRenderResult>? Completed;

        public Guid Id { get; }

        public ChartRenderTaskState State => (ChartRenderTaskState)Volatile.Read(ref state);

        public ChartRenderProgress Progress => Volatile.Read(ref progress);

        public ChartRenderResult? Result { get; private set; }

        public ChartRenderCaptureSource CaptureSource { get; internal set; }

        public int OutputWidth { get; internal set; }

        public int OutputHeight { get; internal set; }

        public string OutputPath { get; internal set; } = string.Empty;

        public bool ShowBuiltInProgressUi { get; }

        public bool IsTerminal
        {
            get
            {
                ChartRenderTaskState current = State;
                return current == ChartRenderTaskState.Completed
                    || current == ChartRenderTaskState.Failed
                    || current == ChartRenderTaskState.Canceled;
            }
        }

        internal ChartRenderRequest Request { get; }

        internal bool IsCancellationRequested => Volatile.Read(ref cancelRequested) != 0;

        internal bool IsFinishRequested => Volatile.Read(ref finishRequested) != 0;

        public bool Cancel()
        {
            if (IsTerminal)
            {
                return false;
            }

            Interlocked.Exchange(ref cancelRequested, 1);
            return true;
        }

        public bool RequestFinish()
        {
            if (State != ChartRenderTaskState.Rendering)
            {
                return false;
            }

            Interlocked.Exchange(ref finishRequested, 1);
            return true;
        }

        internal void SetState(ChartRenderTaskState next)
        {
            if (State == next || IsTerminal)
            {
                return;
            }

            Volatile.Write(ref state, (int)next);
            InvokeSafely(StateChanged, handler => handler(this), "StateChanged");
        }

        internal void SetProgress(ChartRenderProgress next)
        {
            Volatile.Write(ref progress, next);
            InvokeSafely(ProgressChanged, handler => handler(this), "ProgressChanged");
        }

        internal void CompleteWith(ChartRenderResult result)
        {
            if (IsTerminal)
            {
                return;
            }

            Result = result;
            OutputPath = result.OutputPath;
            Volatile.Write(ref state, (int)result.State);
            InvokeSafely(StateChanged, handler => handler(this), "StateChanged");
            InvokeSafely(Completed, handler => handler(this, result), "Completed");
        }

        private static void InvokeSafely<TDelegate>(TDelegate? handlers, Action<TDelegate> invoke, string eventName)
            where TDelegate : Delegate
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate rawHandler in handlers.GetInvocationList())
            {
                try
                {
                    invoke((TDelegate)rawHandler);
                }
                catch (Exception exception)
                {
                    Main.Log("[ChartRenderApi] Subscriber failed in " + eventName + ": " + exception);
                }
            }
        }
    }
}

namespace EditorTweaks.Features.ChartRendering
{
    internal static class ChartRenderOptionValues
    {
        public const string EncoderAutoBalanced = "auto-balanced";
        public const string EncoderFastest = "fastest";
        public const string EncoderBalanced = "balanced";
        public const string EncoderQuality = "quality";
        public const string EncoderCpuCompatibility = "cpu-compatibility";
        public const string EncoderCustom = "custom";

        public const string CaptureRgba = "rgba";
        public const string CaptureBgra = "bgra";

        public const string CaptureSourceCamera = "camera";
        public const string CaptureSourceGameView = "game-view";

        public const string VideoFormatMp4 = "mp4";
        public const string VideoFormatMkv = "mkv";
        public const string VideoFormatMov = "mov";

        public const string AudioFormatAac = "aac";
        public const string AudioFormatFlac = "flac";
        public const string AudioFormatAlac = "alac";

        public const string PreviewFull = "full";
        public const string PreviewDim = "dim";
        public const string PreviewMinimal = "minimal";

        public static readonly string[] EncoderModes =
        {
            EncoderAutoBalanced,
            EncoderFastest,
            EncoderBalanced,
            EncoderQuality,
            EncoderCpuCompatibility,
            EncoderCustom
        };

        public static readonly string[] CaptureFormats =
        {
            CaptureRgba,
            CaptureBgra
        };

        public static readonly string[] CaptureSources =
        {
            CaptureSourceCamera,
            CaptureSourceGameView
        };

        public static readonly string[] AudioFormats =
        {
            AudioFormatAac,
            AudioFormatFlac,
            AudioFormatAlac
        };

        public static readonly string[] VideoFormats =
        {
            VideoFormatMp4,
            VideoFormatMkv,
            VideoFormatMov
        };

        public static readonly string[] PreviewModes =
        {
            PreviewFull,
            PreviewDim,
            PreviewMinimal
        };

        public static string NormalizeEncoderMode(string? value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case EncoderFastest:
                    return EncoderFastest;
                case EncoderBalanced:
                    return EncoderBalanced;
                case EncoderQuality:
                    return EncoderQuality;
                case EncoderCpuCompatibility:
                    return EncoderCpuCompatibility;
                case EncoderCustom:
                    return EncoderCustom;
                default:
                    return EncoderAutoBalanced;
            }
        }

        public static string NormalizeCaptureFormat(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() == CaptureBgra
                ? CaptureBgra
                : CaptureRgba;
        }

        public static string NormalizeCaptureSource(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() == CaptureSourceGameView
                ? CaptureSourceGameView
                : CaptureSourceCamera;
        }

        public static string NormalizePreviewMode(string? value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case PreviewDim:
                    return PreviewDim;
                case PreviewMinimal:
                    return PreviewMinimal;
                default:
                    return PreviewFull;
            }
        }

        public static string NormalizeAudioFormat(string? value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case AudioFormatFlac:
                    return AudioFormatFlac;
                case AudioFormatAlac:
                    return AudioFormatAlac;
                default:
                    return AudioFormatAac;
            }
        }

        public static string NormalizeVideoFormat(string? value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case VideoFormatMkv:
                    return VideoFormatMkv;
                case VideoFormatMov:
                    return VideoFormatMov;
                default:
                    return VideoFormatMp4;
            }
        }

        public static string GetVideoFormatExtension(string format)
        {
            return "." + NormalizeVideoFormat(format);
        }
    }
}

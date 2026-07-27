using System;

namespace ADOFAI.EditorTweaks.Features.ArchiveIo
{
    internal enum LegacyZipEncodingMode
    {
        Auto,
        CP949,
        GB18030,
        ShiftJIS,
        CP437
    }

    internal static class LegacyZipEncodingModes
    {
        public const string Auto = "Auto";
        public const string CP949 = "CP949";
        public const string GB18030 = "GB18030";
        public const string ShiftJIS = "ShiftJIS";
        public const string CP437 = "CP437";

        public static readonly string[] Values =
        {
            Auto,
            CP949,
            GB18030,
            ShiftJIS,
            CP437
        };

        public static LegacyZipEncodingMode Parse(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out LegacyZipEncodingMode mode)
                ? mode
                : LegacyZipEncodingMode.Auto;
        }

        public static string Normalize(string? value)
        {
            return Parse(value).ToString();
        }
    }
}

using HarmonyLib;

namespace ADOFAI.EditorTweaks.Features.ArchiveIo
{
    internal static class ArchiveIoPatches
    {
        [HarmonyPatch(typeof(ZipUtils), nameof(ZipUtils.Unzip))]
        private static class UnzipPatch
        {
            [HarmonyPrepare]
            private static void Prepare()
            {
                ArchiveService.Initialize(Main.Mod?.Path
                    ?? throw new System.InvalidOperationException("Mod path is unavailable."));
            }

            [HarmonyPrefix]
            private static bool Prefix(string sourceArchiveFileName, string destinationDirectoryName)
            {
                ArchiveService.Extract(sourceArchiveFileName, destinationDirectoryName);
                return false;
            }
        }

        [HarmonyPatch(typeof(ZipUtils), nameof(ZipUtils.Zip))]
        private static class ZipPatch
        {
            [HarmonyPrepare]
            private static void Prepare()
            {
                ArchiveService.Initialize(Main.Mod?.Path
                    ?? throw new System.InvalidOperationException("Mod path is unavailable."));
            }

            [HarmonyPrefix]
            private static bool Prefix(string zipFileName, string[] files)
            {
                ArchiveService.CreateZip(zipFileName, files);
                return false;
            }
        }
    }
}

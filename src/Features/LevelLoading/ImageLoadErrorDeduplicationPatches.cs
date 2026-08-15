using System.Collections.Generic;
using ADOFAI;
using HarmonyLib;

namespace ADOFAI.EditorTweaks.Features.LevelLoading
{
    internal static class ImageLoadErrorDeduplicationPatches
    {
        [HarmonyPatch(typeof(scnEditor), nameof(scnEditor.UpdateImageLoadResult))]
        private static class UpdateImageLoadResultPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                scnEditor __instance,
                string name,
                LoadResult loadResult,
                Dictionary<string, string> ___errorImageResult,
                ref bool ___isUnauthorizedAccess)
            {
                if (!Main.Settings.EnableImageLoadErrorDeduplication)
                {
                    return true;
                }

                if (!__instance.isLoading || !IsImageLoadError(loadResult))
                {
                    return true;
                }

                if (!___errorImageResult.ContainsKey(name))
                {
                    return true;
                }

                ___errorImageResult[name] = loadResult.ToString();
                if (loadResult == LoadResult.UnauthorizedAccess)
                {
                    ___isUnauthorizedAccess = true;
                }

                return false;
            }
        }

        private static bool IsImageLoadError(LoadResult loadResult)
        {
            return loadResult == LoadResult.UnauthorizedAccess
                || loadResult == LoadResult.MissingFile
                || loadResult == LoadResult.Error;
        }
    }
}

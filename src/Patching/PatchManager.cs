using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ADOFAI.EditorTweaks.Patching
{
    internal enum PatchFeature
    {
        NumericDrag,
        CameraRelativeDecorationDrag,
        DecorationMoveSnap,
        DecorationPivot,
        VideoBackgroundSync,
        EditorPreferences,
        ImageLoadErrorDeduplication
    }

    internal enum PatchGroupState { Inactive, Active, Failed }

    internal sealed class PatchGroupStatus
    {
        public PatchGroupStatus(PatchFeature feature, string displayName) { Feature = feature; DisplayName = displayName; }
        public PatchFeature Feature { get; }
        public string DisplayName { get; }
        public PatchGroupState State { get; internal set; }
        public int PatchCount { get; internal set; }
        public string Reason { get; internal set; } = string.Empty;
    }

    internal sealed class PatchGroupDefinition
    {
        public PatchGroupDefinition(PatchFeature feature, string id, string displayName, params Type[] roots)
        { Feature = feature; Id = id; DisplayName = displayName; Roots = roots; }
        public PatchFeature Feature { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public Type[] Roots { get; }
    }

    internal static class PatchManager
    {
        private static readonly PatchGroupDefinition[] definitions =
        {
            new PatchGroupDefinition(PatchFeature.NumericDrag, "numeric-drag", "Numeric drag", typeof(Features.NumericDrag.NumericDragPatches)),
            new PatchGroupDefinition(PatchFeature.CameraRelativeDecorationDrag, "camera-relative-decoration-drag", "Camera-relative decoration drag", typeof(Features.DecorationSelection.CameraRelativeDecorationDragPatches)),
            new PatchGroupDefinition(PatchFeature.DecorationMoveSnap, "decoration-move-snap", "Decoration move snapping", typeof(Features.DecorationSelection.DecorationMoveSnapPatches)),
            new PatchGroupDefinition(PatchFeature.DecorationPivot, "decoration-pivot", "Decoration pivot", typeof(Features.DecorationSelection.DecorationPivotPatches)),
            new PatchGroupDefinition(PatchFeature.VideoBackgroundSync, "video-background-sync", "Video background sync", typeof(Features.VideoBackgroundSync.VideoBackgroundSyncPatches)),
            new PatchGroupDefinition(PatchFeature.EditorPreferences, "editor-preferences", "Editor preference persistence", typeof(Features.EditorPreferences.EditorPreferencesPersistencePatches)),
            new PatchGroupDefinition(PatchFeature.ImageLoadErrorDeduplication, "image-load-error-deduplication", "Duplicate missing-image load protection", typeof(Features.LevelLoading.ImageLoadErrorDeduplicationPatches))
        };

        private static readonly Dictionary<PatchFeature, PatchGroupStatus> statuses = new Dictionary<PatchFeature, PatchGroupStatus>();
        private static readonly Dictionary<PatchFeature, Harmony> activeHarmonies = new Dictionary<PatchFeature, Harmony>();
        private static readonly Dictionary<PatchFeature, string> activeHarmonyIds = new Dictionary<PatchFeature, string>();

        public static IReadOnlyList<PatchGroupStatus> Statuses => definitions.Select(GetOrCreateStatus).ToArray();

        public static IReadOnlyList<PatchGroupStatus> ApplyAll(string modId)
        {
            ClearActivePatches();
            statuses.Clear();
            foreach (PatchGroupDefinition definition in definitions)
            {
                PatchGroupStatus status = GetOrCreateStatus(definition);
                List<Type> patchTypes = new List<Type>();
                CollectPatchTypes(definition.Roots, patchTypes);
                status.PatchCount = patchTypes.Count;
                string harmonyId = modId + "." + definition.Id;
                Harmony harmony = new Harmony(harmonyId);
                try
                {
                    foreach (Type patchType in patchTypes.OrderBy(type => type.FullName, StringComparer.Ordinal))
                    {
                        harmony.CreateClassProcessor(patchType).Patch();
                    }
                    activeHarmonies[definition.Feature] = harmony;
                    activeHarmonyIds[definition.Feature] = harmonyId;
                    status.State = PatchGroupState.Active;
                    Main.Log("[PatchManager] Group '" + definition.Id + "' active (" + patchTypes.Count + " patches).");
                }
                catch (Exception exception)
                {
                    try { harmony.UnpatchAll(harmonyId); } catch { }
                    status.State = PatchGroupState.Failed;
                    status.Reason = exception.GetBaseException().Message;
                    Main.Mod?.Logger.Error("[PatchManager] Group '" + definition.Id + "' failed: " + exception);
                }
            }

            Main.Log("[PatchManager] " + statuses.Values.Count(item => item.State == PatchGroupState.Active)
                + "/" + definitions.Length + " groups active.");
            return Statuses;
        }

        public static void UnpatchAll()
        {
            ClearActivePatches();
            foreach (PatchGroupStatus status in statuses.Values)
            {
                status.State = PatchGroupState.Inactive;
                status.Reason = string.Empty;
            }
        }

        public static bool IsAvailable(PatchFeature feature)
        {
            return statuses.TryGetValue(feature, out PatchGroupStatus status) && status.State == PatchGroupState.Active;
        }

        public static string GetSummary()
        {
            return statuses.Values.Count(item => item.State == PatchGroupState.Active) + "/" + definitions.Length + " 功能组可用";
        }

        private static PatchGroupStatus GetOrCreateStatus(PatchGroupDefinition definition)
        {
            if (!statuses.TryGetValue(definition.Feature, out PatchGroupStatus status))
            {
                status = new PatchGroupStatus(definition.Feature, definition.DisplayName);
                statuses[definition.Feature] = status;
            }
            return status;
        }

        private static void CollectPatchTypes(IEnumerable<Type> roots, ICollection<Type> result)
        {
            foreach (Type root in roots) CollectPatchTypes(root, result, new HashSet<Type>());
        }

        private static void CollectPatchTypes(Type type, ICollection<Type> result, ISet<Type> visited)
        {
            if (!visited.Add(type)) return;
            if (type.IsDefined(typeof(HarmonyPatch), true)) result.Add(type);
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                CollectPatchTypes(nested, result, visited);
        }

        private static void ClearActivePatches()
        {
            foreach (KeyValuePair<PatchFeature, Harmony> entry in activeHarmonies)
            {
                if (activeHarmonyIds.TryGetValue(entry.Key, out string harmonyId))
                {
                    try { entry.Value.UnpatchAll(harmonyId); }
                    catch (Exception exception) { Main.Mod?.Logger.Error(exception.ToString()); }
                }
            }
            activeHarmonies.Clear();
            activeHarmonyIds.Clear();
        }
    }
}

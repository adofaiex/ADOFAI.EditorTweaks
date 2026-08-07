using System;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI.EditorTweaks.Api.Rendering;
using ADOFAI.EditorTweaks.Features.CloudSettings;
using ADOFAI.EditorTweaks.Patching;
using GDMiniJSON;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.WebUi
{
    internal static class WebUiStateBuilder
    {
        private static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fixes"] = "编辑器行为修复与偏好保存",
            ["numeric"] = "增强数字输入体验与精度",
            ["decoration"] = "优化装饰物选择、移动和吸附",
            ["render"] = "将谱面画面和声音输出为视频",
            ["cloud"] = "Steam 云端设置同步",
            ["tools"] = "压缩包和编辑器诊断工具"
        };

        private static readonly ModuleDefinition[] modules =
        {
            new ModuleDefinition("fixes", "编辑器修复", new[] { PatchFeature.VideoBackgroundSync, PatchFeature.EditorPreferences }),
            new ModuleDefinition("numeric", "数值拖动", new[] { PatchFeature.NumericDrag }),
            new ModuleDefinition("decoration", "装饰移动", new[] { PatchFeature.CameraRelativeDecorationDrag, PatchFeature.DecorationMoveSnap, PatchFeature.DecorationPivot }),
            new ModuleDefinition("render", "谱面渲染", new[] { PatchFeature.RenderInputGuard, PatchFeature.ChartRendering }),
            new ModuleDefinition("cloud", "云同步", Array.Empty<PatchFeature>()),
            new ModuleDefinition("tools", "工具", new[] { PatchFeature.ArchiveIo, PatchFeature.ImageLoadErrorDeduplication })
        };

        public static string BuildJson(ChartRenderTask? renderTask)
        {
            return Json.Serialize(Build(renderTask));
        }

        public static Dictionary<string, object> Build(ChartRenderTask? renderTask)
        {
            IReadOnlyList<PatchGroupStatus> statuses = PatchManager.Statuses;
            Dictionary<PatchFeature, PatchGroupStatus> statusMap = new Dictionary<PatchFeature, PatchGroupStatus>();
            foreach (PatchGroupStatus status in statuses)
            {
                statusMap[status.Feature] = status;
            }

            List<object> patchItems = new List<object>();
            int activeCount = 0;
            foreach (ModuleDefinition module in modules)
            {
                PatchGroupState moduleState = GetModuleState(module, statusMap);
                if (moduleState == PatchGroupState.Active)
                {
                    activeCount++;
                }

                string state = GetPatchState(moduleState);
                string reason = GetModuleReason(module, statusMap, moduleState);
                patchItems.Add(new Dictionary<string, object>
                {
                    ["id"] = module.Id,
                    ["name"] = module.Name,
                    ["description"] = descriptions[module.Id],
                    ["state"] = state,
                    ["patchCount"] = GetModulePatchCount(module, statusMap),
                    ["reason"] = reason
                });
            }

            return new Dictionary<string, object>
            {
                ["server"] = new Dictionary<string, object>
                {
                    ["connected"] = true,
                    ["version"] = Main.Mod?.Info?.Version ?? "0.0.0"
                },
                ["compatibility"] = new Dictionary<string, object>
                {
                    ["gameVersion"] = Application.version ?? string.Empty,
                    ["editorVersion"] = Application.version ?? string.Empty,
                    ["modVersion"] = Main.Mod?.Info?.Version ?? "0.0.0"
                },
                ["patches"] = patchItems,
                ["patchSummary"] = new Dictionary<string, object>
                {
                    ["active"] = activeCount,
                    ["total"] = modules.Length,
                    ["registrationError"] = PatchManager.HasRegistrationErrors
                },
                ["cloud"] = new Dictionary<string, object>
                {
                    ["available"] = CloudSettingsManager.IsSteamAvailable,
                    ["hasFile"] = CloudSettingsManager.HasCloudFile()
                },
                ["settings"] = BuildSettings(Main.Settings),
                ["render"] = BuildRenderState(renderTask)
            };
        }

        private static Dictionary<string, object> BuildSettings(Settings settings)
        {
            return new Dictionary<string, object>
            {
                ["WebUiOpenHotkey"] = settings.WebUiOpenHotkey,
                ["EnableNumericDrag"] = settings.EnableNumericDrag,
                ["EnableCameraRelativeDecorationDragFix"] = settings.EnableCameraRelativeDecorationDragFix,
                ["EnableDecorationPivotFix"] = settings.EnableDecorationPivotFix,
                ["EnableVideoBackgroundSyncFix"] = settings.EnableVideoBackgroundSyncFix,
                ["PersistEditorPreferences"] = settings.PersistEditorPreferences,
                ["LegacyZipEncoding"] = settings.LegacyZipEncoding,
                ["DecorationMoveSnapStep"] = settings.DecorationMoveSnapStep,
                ["FloatStepPerPixel"] = settings.FloatStepPerPixel,
                ["IntStepPerPixel"] = settings.IntStepPerPixel,
                ["MaxFloatingPoints"] = settings.MaxFloatingPoints,
                ["ChartRenderWorkspaceDirectory"] = settings.ChartRenderWorkspaceDirectory ?? string.Empty,
                ["ChartRenderExportDirectory"] = settings.ChartRenderExportDirectory ?? string.Empty,
                ["ChartRenderWidth"] = settings.ChartRenderWidth,
                ["ChartRenderHeight"] = settings.ChartRenderHeight,
                ["ChartRenderFps"] = settings.ChartRenderFps,
                ["ChartRenderCrf"] = settings.ChartRenderCrf,
                ["ChartRenderBitrateMbps"] = settings.ChartRenderBitrateMbps,
                ["ChartRenderPreset"] = settings.ChartRenderPreset ?? string.Empty,
                ["ChartRenderEncoderMode"] = settings.ChartRenderEncoderMode ?? string.Empty,
                ["ChartRenderCaptureFormat"] = settings.ChartRenderCaptureFormat ?? string.Empty,
                ["ChartRenderCaptureSource"] = settings.ChartRenderCaptureSource ?? string.Empty,
                ["ChartRenderPreviewMode"] = settings.ChartRenderPreviewMode ?? string.Empty,
                ["ChartRenderAudioFormat"] = settings.ChartRenderAudioFormat ?? string.Empty,
                ["ChartRenderVideoFormat"] = settings.ChartRenderVideoFormat ?? string.Empty,
                ["ChartRenderCompletionTailSeconds"] = settings.ChartRenderCompletionTailSeconds,
                ["ChartRenderAudioSyncOffsetMs"] = settings.ChartRenderAudioSyncOffsetMs,
                ["ChartRenderShowHitJudgments"] = settings.ChartRenderShowHitJudgments,
                ["ChartRenderUseSelectedRange"] = settings.ChartRenderUseSelectedRange,
                ["ChartRenderCustomMuxArgs"] = settings.ChartRenderCustomMuxArgs ?? string.Empty
            };
        }

        private static Dictionary<string, object> BuildRenderState(ChartRenderTask? task)
        {
            ChartRenderProgress progress = task?.Progress ?? ChartRenderProgress.Empty();
            ChartRenderResult? result = task?.Result;
            return new Dictionary<string, object>
            {
                ["active"] = task != null && !task.IsTerminal,
                ["taskId"] = task?.Id.ToString() ?? string.Empty,
                ["state"] = task?.State.ToString() ?? "Idle",
                ["cancelRequested"] = task != null && task.IsCancellationRequested,
                ["captureSource"] = task?.CaptureSource.ToString() ?? string.Empty,
                ["outputPath"] = task?.OutputPath ?? string.Empty,
                ["message"] = result?.Message ?? string.Empty,
                ["progress"] = new Dictionary<string, object>
                {
                    ["value"] = progress.Value,
                    ["writtenFrames"] = progress.WrittenFrames,
                    ["totalFrames"] = progress.TotalFrames,
                    ["duplicateFrames"] = progress.DuplicateFrames,
                    ["duplicateRatio"] = progress.DuplicateRatio,
                    ["processingFramesPerSecond"] = progress.ProcessingFramesPerSecond,
                    ["estimatedRemaining"] = progress.EstimatedRemaining.ToString(),
                    ["stage"] = progress.Stage,
                    ["detail"] = progress.Detail,
                    ["encoderName"] = progress.EncoderName,
                    ["memoryBudget"] = progress.MemoryBudget,
                    ["queueBudget"] = progress.QueueBudget
                }
            };
        }

        private static string GetPatchState(PatchGroupState state)
        {
            switch (state)
            {
                case PatchGroupState.Active:
                    return "active";
                case PatchGroupState.Failed:
                    return "failed";
                case PatchGroupState.Blocked:
                    return "blocked";
                default:
                    return "inactive";
            }
        }

        private static PatchGroupState GetModuleState(ModuleDefinition module, Dictionary<PatchFeature, PatchGroupStatus> statusMap)
        {
            if (module.Features.Length == 0)
            {
                return CloudSettingsManager.IsSteamAvailable ? PatchGroupState.Active : PatchGroupState.Inactive;
            }

            bool hasInactive = false;
            foreach (PatchFeature feature in module.Features)
            {
                if (!statusMap.TryGetValue(feature, out PatchGroupStatus status))
                {
                    hasInactive = true;
                    continue;
                }

                if (status.State == PatchGroupState.Failed)
                {
                    return PatchGroupState.Failed;
                }

                if (status.State == PatchGroupState.Blocked)
                {
                    return PatchGroupState.Blocked;
                }

                if (status.State != PatchGroupState.Active)
                {
                    hasInactive = true;
                }
            }

            return hasInactive ? PatchGroupState.Inactive : PatchGroupState.Active;
        }

        private static int GetModulePatchCount(ModuleDefinition module, Dictionary<PatchFeature, PatchGroupStatus> statusMap)
        {
            int count = 0;
            foreach (PatchFeature feature in module.Features)
            {
                if (statusMap.TryGetValue(feature, out PatchGroupStatus status))
                {
                    count += status.PatchCount;
                }
            }

            return count;
        }

        private static string GetModuleReason(ModuleDefinition module, Dictionary<PatchFeature, PatchGroupStatus> statusMap, PatchGroupState moduleState)
        {
            if (moduleState == PatchGroupState.Active || module.Features.Length == 0)
            {
                return string.Empty;
            }

            foreach (PatchFeature feature in module.Features)
            {
                if (!statusMap.TryGetValue(feature, out PatchGroupStatus status))
                {
                    continue;
                }

                if (status.State == PatchGroupState.Failed)
                {
                    return CompactReason(status.Reason);
                }

                if (status.State == PatchGroupState.Blocked && status.BlockedBy.HasValue)
                {
                    return "依赖 " + status.BlockedBy.Value;
                }
            }

            return string.Empty;
        }

        private static string CompactReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return string.Empty;
            }

            string compact = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return compact.Length <= 180 ? compact : compact.Substring(0, 179) + "…";
        }

        private sealed class ModuleDefinition
        {
            public ModuleDefinition(string id, string name, PatchFeature[] features)
            {
                Id = id;
                Name = name;
                Features = features;
            }

            public string Id { get; }

            public string Name { get; }

            public PatchFeature[] Features { get; }
        }
    }
}

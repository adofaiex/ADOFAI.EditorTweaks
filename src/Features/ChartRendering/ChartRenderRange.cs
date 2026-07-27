using System;
using System.Collections.Generic;
using ADOFAI.EditorTweaks.Api.Rendering;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderRange
    {
        private ChartRenderRange(bool isPartial, bool isCurrentPlayback, int startFloor, int endFloor, int floorCount)
        {
            IsPartial = isPartial;
            IsCurrentPlayback = isCurrentPlayback;
            StartFloor = startFloor;
            EndFloor = endFloor;
            FloorCount = floorCount;
        }

        public bool IsPartial { get; }

        public bool IsCurrentPlayback { get; }

        public int StartFloor { get; }

        public int EndFloor { get; }

        public int FloorCount { get; }

        public int AutoPlaybackEndFloor => IsPartial ? EndFloor : int.MaxValue;

        public string FileNameSuffix => IsCurrentPlayback
            ? "_current"
            : IsPartial ? "_f" + StartFloor + "-f" + EndFloor : string.Empty;

        public string DisplayText => IsCurrentPlayback
            ? "Current playback to end"
            : IsPartial
            ? Settings.Text("chartRenderSelectedRangeActive") + " " + StartFloor + " - " + EndFloor
            : Settings.Text("chartRenderSelectedRangeWholeLevel");

        public static ChartRenderRange WholeLevel()
        {
            int count = GetPlayableFloorCount();
            return new ChartRenderRange(isPartial: false, isCurrentPlayback: false, startFloor: 0, endFloor: Math.Max(0, count - 1), floorCount: count);
        }

        public static ChartRenderRange CreateFromRequest(ChartRenderRangeRequest request)
        {
            request = request ?? ChartRenderRangeRequest.WholeLevel();
            switch (request.Mode)
            {
                case ChartRenderRangeMode.CurrentEditorSelection:
                    return CreateFromEditorSelection();
                case ChartRenderRangeMode.ExplicitFloors:
                    return CreateExplicit(request.StartFloor, request.EndFloor);
                case ChartRenderRangeMode.CurrentPlaybackToEnd:
                    return CreateCurrentPlaybackToEnd();
                default:
                    return WholeLevel();
            }
        }

        public static ChartRenderRange CreateFromSettings(Settings settings)
        {
            if (!settings.ChartRenderUseSelectedRange)
            {
                return WholeLevel();
            }

            return CreateFromEditorSelection();
        }

        private static ChartRenderRange CreateFromEditorSelection()
        {
            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                throw new InvalidOperationException(Settings.Text("chartRendererSelectedRangeEditorOnly"));
            }

            if (!TryGetEditorSelectedRange(out int startFloor, out int endFloor, out int selectedCount))
            {
                throw new InvalidOperationException(Settings.Text("chartRendererSelectedRangeMissing"));
            }

            int floorCount = editor.floors == null ? 0 : editor.floors.Count;
            startFloor = Mathf.Clamp(startFloor, 0, Math.Max(0, floorCount - 1));
            endFloor = Mathf.Clamp(endFloor, 0, Math.Max(0, floorCount - 1));
            if (endFloor <= startFloor)
            {
                throw new InvalidOperationException(Settings.Text("chartRendererSelectedRangeMissing"));
            }

            return new ChartRenderRange(isPartial: true, isCurrentPlayback: false, startFloor, endFloor, Math.Max(0, selectedCount));
        }

        private static ChartRenderRange CreateExplicit(int startFloor, int endFloor)
        {
            int floorCount = GetPlayableFloorCount();
            if (startFloor < 0 || endFloor <= startFloor || endFloor >= floorCount)
            {
                throw new InvalidOperationException("The explicit floor range is outside the loaded level.");
            }

            return new ChartRenderRange(
                isPartial: true,
                isCurrentPlayback: false,
                startFloor,
                endFloor,
                endFloor - startFloor + 1);
        }

        private static ChartRenderRange CreateCurrentPlaybackToEnd()
        {
            int floorCount = GetPlayableFloorCount();
            int startFloor = Mathf.Clamp(ChartRenderPlaybackController.GetPrimaryPlayerFloor(), 0, Math.Max(0, floorCount - 1));
            return new ChartRenderRange(
                isPartial: false,
                isCurrentPlayback: true,
                startFloor,
                Math.Max(0, floorCount - 1),
                Math.Max(0, floorCount - startFloor));
        }

        public static bool TryGetEditorSelectedRange(out int startFloor, out int endFloor, out int selectedCount)
        {
            startFloor = 0;
            endFloor = 0;
            selectedCount = 0;

            scnEditor editor = ADOBase.editor;
            List<scrFloor>? selectedFloors = editor == null ? null : editor.selectedFloors;
            if (selectedFloors == null || selectedFloors.Count < 2)
            {
                return false;
            }

            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (scrFloor floor in selectedFloors)
            {
                if (floor == null)
                {
                    continue;
                }

                selectedCount++;
                min = Math.Min(min, floor.seqID);
                max = Math.Max(max, floor.seqID);
            }

            if (selectedCount < 2 || min == int.MaxValue || max <= min)
            {
                return false;
            }

            startFloor = min;
            endFloor = max;
            return true;
        }

        public double EstimateDurationSeconds(double tailSeconds)
        {
            List<scrFloor>? floors = ADOBase.lm == null ? null : ADOBase.lm.listFloors;
            if (floors == null || floors.Count <= 1)
            {
                return 1.0;
            }

            int safeStart = Mathf.Clamp(StartFloor, 0, floors.Count - 1);
            int safeEnd = Mathf.Clamp(EndFloor, safeStart, floors.Count - 1);
            if (!IsPartial)
            {
                if (IsCurrentPlayback)
                {
                    double currentSongPosition = ADOBase.conductor == null ? 0.0 : ADOBase.conductor.songposition_minusi;
                    double currentPlaybackEndTime = floors[floors.Count - 1].entryTimePitchAdj;
                    return Math.Max(1.0, currentPlaybackEndTime - currentSongPosition + Math.Max(0.0, tailSeconds));
                }

                return Math.Max(1.0, floors[floors.Count - 1].entryTimePitchAdj + Math.Max(0.0, tailSeconds));
            }

            double startTime = floors[safeStart].entryTimePitchAdj;
            double endTime = floors[safeEnd].entryTimePitchAdj;
            return Math.Max(1.0, endTime - startTime + Math.Max(0.0, tailSeconds));
        }

        public bool HasReachedEnd()
        {
            if (!IsPartial)
            {
                return HasReachedWholeLevelEnd();
            }

            scrController controller = ADOBase.controller;
            if (controller == null)
            {
                return false;
            }

            if (controller.currentSeqID >= EndFloor)
            {
                return true;
            }

            scrFloor currentFloor = controller.currFloor;
            if (currentFloor != null && currentFloor.seqID >= EndFloor)
            {
                return true;
            }

            scrFloor? playerFloor = ADOBase.controller?.playerOne?.currFloor;
            return playerFloor != null && playerFloor.seqID >= EndFloor;
        }

        private static bool HasReachedWholeLevelEnd()
        {
            scrController controller = ADOBase.controller;
            if (controller == null || ADOBase.conductor == null || !ADOBase.conductor.hasSongStarted)
            {
                return false;
            }

            if (controller.state == States.Won)
            {
                return true;
            }

            List<scrFloor>? floors = ADOBase.lm == null ? null : ADOBase.lm.listFloors;
            if (floors == null || floors.Count <= 1)
            {
                return false;
            }

            int lastSeqId = floors.Count - 1;
            if (controller.currentSeqID >= lastSeqId)
            {
                return true;
            }

            scrFloor currentFloor = controller.currFloor;
            return currentFloor != null && currentFloor.seqID >= lastSeqId;
        }

        private static int GetPlayableFloorCount()
        {
            if (ADOBase.editor != null && ADOBase.editor.floors != null)
            {
                return ADOBase.editor.floors.Count;
            }

            return ADOBase.lm == null || ADOBase.lm.listFloors == null ? 0 : ADOBase.lm.listFloors.Count;
        }

    }
}

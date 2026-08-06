using System;
using System.Collections.Generic;
using System.Reflection;
using EditorTweaks.Api.Rendering;
using UnityEngine;

namespace EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderPlaybackController
    {
        private static readonly FieldInfo? WaitForStartCoCallCountField =
            typeof(scrController).GetField("waitForStartCoCallCount", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Settings settings;
        private RenderState? savedState;
        private bool ownsPlayback;

        public ChartRenderPlaybackController(Settings settings)
        {
            this.settings = settings;
        }

        public bool PlaybackStartsAtBeginning { get; private set; }

        public void StartPlayback(ChartRenderRange renderRange, ChartRenderPlaybackMode playbackMode)
        {
            savedState = RenderState.Capture();
            Time.captureFramerate = Math.Max(1, settings.ChartRenderFps);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Math.Max(1000, settings.ChartRenderFps * 4);
            if (Persistence.skipIntroBehavior != SkipIntroBehavior.Off)
            {
                ChartRenderDiagnostics.Log("Temporarily disabling skip intro for render. oldValue=" + Persistence.skipIntroBehavior + ".");
                Persistence.skipIntroBehavior = SkipIntroBehavior.Off;
            }

            PlaybackStartsAtBeginning = false;
            ownsPlayback = playbackMode != ChartRenderPlaybackMode.AttachToCurrentPlayback;
            if (!ownsPlayback)
            {
                if (!IsPlaybackScheduled())
                {
                    throw new InvalidOperationException("Playback is not active.");
                }

                ChartRenderDiagnostics.Log("Attaching renderer to current playback without restarting it.");
                return;
            }

            if (ADOBase.editor != null)
            {
                StartEditorPlayback(renderRange.StartFloor);
                PlaybackStartsAtBeginning = renderRange.StartFloor <= 0;
                return;
            }

            PlaybackStartsAtBeginning = StartGameScenePlayback(renderRange.StartFloor);
        }

        public void RestoreState()
        {
            if (savedState == null)
            {
                return;
            }

            try
            {
                scnEditor editor = ADOBase.editor;
                if (ownsPlayback && editor != null && (editor.playMode || !editor.inStrictlyEditingMode))
                {
                    editor.SwitchToEditMode();
                }
            }
            catch (Exception ex)
            {
                Main.Log("Failed to switch back to edit mode: " + ex.Message);
            }

            savedState.Restore();
        }

        public static bool IsPlayableLevelLoaded()
        {
            if (ADOBase.editor != null)
            {
                scnEditor editor = ADOBase.editor;
                scnGame? editorLevel = editor.customLevel;
                return !editor.isLoading
                    && editorLevel != null
                    && editorLevel.levelData != null
                    && editor.floors != null
                    && editor.floors.Count > 1;
            }

            scrController controller = ADOBase.controller;
            List<scrFloor>? floors = ADOBase.lm == null ? null : ADOBase.lm.listFloors;
            if (controller == null || !controller.gameworld || floors == null || floors.Count <= 1)
            {
                return false;
            }

            scnGame gameLevel = ADOBase.customLevel;
            return gameLevel == null || (!gameLevel.isLoading && gameLevel.levelData != null);
        }

        public static bool HasRenderableAudio()
        {
            return ADOBase.conductor != null
                && ADOBase.conductor.song != null
                && ADOBase.conductor.song.clip != null;
        }

        public static bool IsPlaybackScheduled()
        {
            if (ADOBase.conductor == null)
            {
                return false;
            }

            if (ADOBase.conductor.hasSongStarted)
            {
                return true;
            }

            return ADOBase.controller != null
                && (ADOBase.controller.state == States.Countdown || ADOBase.controller.state == States.PlayerControl);
        }

        public static int GetPrimaryPlayerFloor()
        {
            try
            {
                scrFloor? floor = ADOBase.controller?.playerOne?.currFloor;
                return floor == null ? -1 : floor.seqID;
            }
            catch
            {
                return -1;
            }
        }

        private static void StartEditorPlayback(int startFloor)
        {
            ChartRenderDiagnostics.Log("Starting official editor playback for render.");
            scnEditor editor = ADOBase.editor;
            if (editor == null || editor.floors == null || editor.floors.Count <= 1)
            {
                throw new InvalidOperationException("The editor has no playable level loaded.");
            }

            int floor = Mathf.Clamp(startFloor, 0, editor.floors.Count - 1);
            editor.SelectFloor(editor.floors[floor], cameraJump: false);
            GCS.checkpointNum = floor;
            RDC.auto = false;
            editor.Play();
            RDC.auto = false;
            ChartRenderDiagnostics.Log("Editor playback requested from floor " + floor + ". checkpoint=" + GCS.checkpointNum
                + " currentSeq=" + (ADOBase.controller == null ? -1 : ADOBase.controller.currentSeqID)
                + " playerFloor=" + GetPrimaryPlayerFloor()
                + " auto=" + RDC.auto + ".");
        }

        private static bool StartGameScenePlayback(int startFloor)
        {
            scrController controller = ADOBase.controller;
            if (controller == null || !IsPlayableLevelLoaded())
            {
                throw new InvalidOperationException("No playable game level is loaded.");
            }

            ChartRenderDiagnostics.Log("Starting game scene playback for render. scene=" + ADOBase.sceneName
                + " level=" + (controller.levelName ?? string.Empty)
                + " scnGame=" + (ADOBase.customLevel != null)
                + " state=" + controller.state);

            RDC.auto = false;
            List<scrFloor>? floors = ADOBase.lm == null ? null : ADOBase.lm.listFloors;
            int lastFloor = floors == null ? 0 : Math.Max(0, floors.Count - 1);
            GCS.checkpointNum = Mathf.Clamp(startFloor, 0, lastFloor);
            AbortWaitingForStartCoroutine(controller);
            HidePressToStart();
            scrUIController.instance?.txtCountdown?.GetComponent<scrCountdown>()?.ShowGetReady();
            ADOBase.conductor.Rewind();
            ADOBase.conductor.Start();

            int checkpoint = Math.Max(0, GCS.checkpointNum);
            controller.Start_Rewind(checkpoint);

            scnGame? gameLevel = ADOBase.customLevel;
            if (gameLevel != null)
            {
                gameLevel.FinishCustomLevelLoading(checkpoint);
            }

            return checkpoint <= 0;
        }

        private static void AbortWaitingForStartCoroutine(scrController controller)
        {
            if (WaitForStartCoCallCountField == null)
            {
                ChartRenderDiagnostics.Log("waitForStartCoCallCount field was not found; continuing without canceling the waiting coroutine.");
                return;
            }

            object raw = WaitForStartCoCallCountField.GetValue(controller);
            int value = raw is int intValue ? intValue : 0;
            WaitForStartCoCallCountField.SetValue(controller, value + 1);
        }

        private static void HidePressToStart()
        {
            try
            {
                scrUIController.instance?.txtPressToStart?.GetComponent<scrPressToStart>()?.HideText();
            }
            catch (Exception ex)
            {
                ChartRenderDiagnostics.Log("Failed to hide press-to-start text: " + ex.Message);
            }
        }

        private sealed class RenderState
        {
            private readonly bool auto;
            private readonly int checkpoint;
            private readonly int captureFramerate;
            private readonly int targetFrameRate;
            private readonly int vSyncCount;
            private readonly SkipIntroBehavior skipIntroBehavior;
            private readonly List<int> selectedFloors;

            private RenderState(bool auto, int checkpoint, int captureFramerate, int targetFrameRate, int vSyncCount, SkipIntroBehavior skipIntroBehavior, List<int> selectedFloors)
            {
                this.auto = auto;
                this.checkpoint = checkpoint;
                this.captureFramerate = captureFramerate;
                this.targetFrameRate = targetFrameRate;
                this.vSyncCount = vSyncCount;
                this.skipIntroBehavior = skipIntroBehavior;
                this.selectedFloors = selectedFloors;
            }

            public static RenderState Capture()
            {
                List<int> floors = new List<int>();
                if (ADOBase.editor != null && ADOBase.editor.selectedFloors != null)
                {
                    foreach (scrFloor floor in ADOBase.editor.selectedFloors)
                    {
                        if (floor != null)
                        {
                            floors.Add(floor.seqID);
                        }
                    }
                }

                floors.Sort();
                return new RenderState(RDC.auto, GCS.checkpointNum, Time.captureFramerate, Application.targetFrameRate, QualitySettings.vSyncCount, Persistence.skipIntroBehavior, floors);
            }

            public void Restore()
            {
                RDC.auto = auto;
                GCS.checkpointNum = checkpoint;
                Time.captureFramerate = captureFramerate;
                Application.targetFrameRate = targetFrameRate;
                QualitySettings.vSyncCount = vSyncCount;
                Persistence.skipIntroBehavior = skipIntroBehavior;

                if (ADOBase.editor == null || ADOBase.editor.floors == null || selectedFloors.Count == 0)
                {
                    return;
                }

                int firstSeqId = Mathf.Clamp(selectedFloors[0], 0, ADOBase.editor.floors.Count - 1);
                if (selectedFloors.Count == 1)
                {
                    ADOBase.editor.SelectFloor(ADOBase.editor.floors[firstSeqId], cameraJump: false);
                    return;
                }

                int lastSeqId = Mathf.Clamp(selectedFloors[selectedFloors.Count - 1], 0, ADOBase.editor.floors.Count - 1);
                ADOBase.editor.MultiSelectFloors(ADOBase.editor.floors[firstSeqId], ADOBase.editor.floors[lastSeqId], setSelectPoint: true);
            }
        }
    }
}

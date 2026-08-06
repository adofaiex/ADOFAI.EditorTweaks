using System.Collections.Generic;
using EditorTweaks.Features.ChartRendering;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace EditorTweaks.Features.VideoBackgroundSync
{
    internal static class VideoBackgroundSyncPatches
    {
        private const double SoftDesyncSeconds = 0.08;
        private const double HardDesyncSeconds = 0.35;
        private const int StartupSyncFrames = 150;
        private const int MaxStartupSeekAttempts = 12;
        private const float StartupSeekCooldown = 0.04f;
        private const float RuntimeSeekCooldown = 0.25f;

        private static readonly Dictionary<int, SyncState> States = new Dictionary<int, SyncState>();

        [HarmonyPatch(typeof(scrVfxPlus), "Reset")]
        private static class ScrVfxPlusResetPatch
        {
            private static void Postfix(scrVfxPlus __instance)
            {
                if (States.TryGetValue(__instance.GetInstanceID(), out SyncState state))
                {
                    state.RestoreRenderSettings();
                }

                States.Remove(__instance.GetInstanceID());
            }
        }

        [HarmonyPatch(typeof(scrVfxPlus), "Update")]
        private static class ScrVfxPlusUpdatePatch
        {
            private static void Prefix(scrVfxPlus __instance)
            {
                if (ChartRenderSession.IsRendering)
                {
                    ConfigureForRender(__instance);
                }
                else if (States.TryGetValue(__instance.GetInstanceID(), out SyncState state))
                {
                    state.RestoreRenderSettings();
                }
            }

            private static void Postfix(scrVfxPlus __instance)
            {
                if (!Main.Settings.EnableVideoBackgroundSyncFix)
                {
                    return;
                }

                TrySynchronize(__instance);
            }
        }

        private static void TrySynchronize(scrVfxPlus vfx)
        {
            VideoPlayer video = vfx.videoBG;
            scrConductor conductor = ADOBase.conductor;
            scrController controller = ADOBase.controller;
            if (video == null
                || conductor == null
                || controller == null
                || controller.paused
                || !conductor.hasSongStarted
                || !video.gameObject.activeSelf
                || !video.isPrepared)
            {
                return;
            }

            if (Persistence.visualEffects != VisualEffects.Full)
            {
                return;
            }

            if (!TryGetTargetVideoTime(vfx, conductor, video, out double targetTime))
            {
                return;
            }

            int id = vfx.GetInstanceID();
            if (!States.TryGetValue(id, out SyncState state))
            {
                state = new SyncState();
                States[id] = state;
            }

            bool rendering = ChartRenderSession.IsRendering;

            bool justStarted = !state.WasPlaying && video.isPlaying;
            bool justMarkedPlayed = !state.WasMarkedPlayed && vfx.hasPlayed;
            if (justStarted || justMarkedPlayed)
            {
                state.StartupFramesLeft = StartupSyncFrames;
                state.StartupSeekAttempts = 0;
            }

            if (!video.isPlaying && vfx.hasPlayed && state.StartupFramesLeft > 0)
            {
                video.playbackSpeed = conductor.song.pitch;
                if (!rendering)
                {
                    video.time = targetTime;
                }

                video.Play();
                state.StartupFramesLeft = StartupSyncFrames;
                state.StartupSeekAttempts = 0;
            }

            video.playbackSpeed = conductor.song.pitch;
            if (rendering && !state.RenderInitialSyncApplied && video.isPlaying)
            {
                state.RenderInitialSyncApplied = true;
                if (!justStarted && !justMarkedPlayed)
                {
                    double actualBeforeSeek = video.time;
                    video.time = targetTime;
                    ChartRenderDiagnostics.Log("Applied one-time video render sync. target="
                        + targetTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        + " actualBeforeSeek=" + actualBeforeSeek.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ".");
                }
                else
                {
                    ChartRenderDiagnostics.Log("Using game's initial video sync for render. target="
                        + targetTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ".");
                }
            }

            double error = Mathf.Abs((float)(video.time - targetTime));
            bool inStartupWindow = state.StartupFramesLeft > 0;
            bool shouldSeek =
                (inStartupWindow && error > SoftDesyncSeconds && state.StartupSeekAttempts < MaxStartupSeekAttempts)
                || error > HardDesyncSeconds;

            if (rendering)
            {
                // The render path performs at most one initial seek. Repeated time assignments,
                // playback-speed correction and skip-on-drop all produce visibly uneven motion
                // with WindowsVideoMedia, so the video runs continuously after startup.
                shouldSeek = false;
            }

            if (shouldSeek)
            {
                float cooldown = inStartupWindow ? StartupSeekCooldown : RuntimeSeekCooldown;
                if (Time.unscaledTime - state.LastSeekRealtime >= cooldown)
                {
                    video.time = targetTime;
                    state.LastSeekRealtime = Time.unscaledTime;
                    state.StartupSeekAttempts++;
                }
            }

            if (inStartupWindow)
            {
                state.StartupFramesLeft--;
            }

            state.WasPlaying = video.isPlaying;
            state.WasMarkedPlayed = vfx.hasPlayed;
        }

        internal static void RestoreRenderSettings()
        {
            foreach (SyncState state in States.Values)
            {
                state.RestoreRenderSettings();
            }
        }

        internal static bool HasActiveVideoBackground()
        {
            scrVfxPlus? vfx = scrVfxPlus.instance;
            VideoPlayer? video = vfx == null ? null : vfx.videoBG;
            return video != null
                && video.gameObject.activeSelf
                && video.isPrepared
                && Persistence.visualEffects == VisualEffects.Full;
        }

        private static void ConfigureForRender(scrVfxPlus vfx)
        {
            VideoPlayer video = vfx.videoBG;
            if (video == null)
            {
                return;
            }

            int id = vfx.GetInstanceID();
            if (!States.TryGetValue(id, out SyncState state))
            {
                state = new SyncState();
                States[id] = state;
            }

            if (state.RenderConfigurationAttempted && state.RenderVideo == video)
            {
                return;
            }

            state.RestoreRenderSettings();
            state.RenderVideo = video;
            state.RenderConfigurationAttempted = true;
            state.RenderInitialSyncApplied = false;
            state.WasPlaying = video.isPlaying;
            state.WasMarkedPlayed = vfx.hasPlayed;
            state.StartupFramesLeft = StartupSyncFrames;
            state.StartupSeekAttempts = 0;

            if (video.canSetTimeUpdateMode)
            {
                state.OriginalTimeUpdateMode = video.timeUpdateMode;
                state.HasOriginalTimeUpdateMode = true;
                video.timeUpdateMode = VideoTimeUpdateMode.GameTime;
            }
            else
            {
                ChartRenderDiagnostics.Log("VideoPlayer does not allow changing timeUpdateMode during render.");
            }

            if (video.canSetSkipOnDrop)
            {
                state.OriginalSkipOnDrop = video.skipOnDrop;
                state.HasOriginalSkipOnDrop = true;
                video.skipOnDrop = false;
            }
            else
            {
                ChartRenderDiagnostics.Log("VideoPlayer does not allow changing skipOnDrop during render.");
            }

            ChartRenderDiagnostics.Log("Video capture mode configured. timeUpdateMode="
                + (video.canSetTimeUpdateMode ? video.timeUpdateMode.ToString() : "unchanged")
                + " skipOnDrop=" + (video.canSetSkipOnDrop ? video.skipOnDrop.ToString() : "unchanged") + ".");
        }

        private static bool TryGetTargetVideoTime(scrVfxPlus vfx, scrConductor conductor, VideoPlayer video, out double targetTime)
        {
            double countdownOffset = conductor.separateCountdownTime
                ? conductor.crotchetAtStart * conductor.adjustedCountdownTicks
                : 0.0;

            targetTime = conductor.songposition_minusi - countdownOffset + vfx.vidOffset;
            if (targetTime < 0.0)
            {
                return false;
            }

            double length = video.length;
            if (length <= 0.0)
            {
                return true;
            }

            if (video.isLooping)
            {
                targetTime %= length;
            }
            else
            {
                targetTime = System.Math.Min(targetTime, System.Math.Max(0.0, length - 0.001));
            }

            return true;
        }

        private sealed class SyncState
        {
            public bool WasPlaying;

            public bool WasMarkedPlayed;

            public int StartupFramesLeft;

            public int StartupSeekAttempts;

            public float LastSeekRealtime = -100f;

            public bool RenderConfigurationAttempted;

            public VideoPlayer? RenderVideo;

            public bool HasOriginalTimeUpdateMode;

            public VideoTimeUpdateMode OriginalTimeUpdateMode;

            public bool HasOriginalSkipOnDrop;

            public bool OriginalSkipOnDrop;

            public bool RenderInitialSyncApplied;

            public void RestoreRenderSettings()
            {
                if (RenderVideo != null)
                {
                    if (HasOriginalTimeUpdateMode && RenderVideo.canSetTimeUpdateMode)
                    {
                        RenderVideo.timeUpdateMode = OriginalTimeUpdateMode;
                    }

                    if (HasOriginalSkipOnDrop && RenderVideo.canSetSkipOnDrop)
                    {
                        RenderVideo.skipOnDrop = OriginalSkipOnDrop;
                    }
                }

                RenderConfigurationAttempted = false;
                RenderVideo = null;
                HasOriginalTimeUpdateMode = false;
                HasOriginalSkipOnDrop = false;
                RenderInitialSyncApplied = false;
            }
        }
    }
}

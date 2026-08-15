using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace ADOFAI.EditorTweaks.Features.VideoBackgroundSync
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
            private static void Postfix(scrVfxPlus __instance) { States.Remove(__instance.GetInstanceID()); }
        }

        [HarmonyPatch(typeof(scrVfxPlus), "Update")]
        private static class ScrVfxPlusUpdatePatch
        {
            private static void Postfix(scrVfxPlus __instance)
            {
                if (Main.Settings.EnableVideoBackgroundSyncFix) TrySynchronize(__instance);
            }
        }

        private static void TrySynchronize(scrVfxPlus vfx)
        {
            VideoPlayer video = vfx.videoBG;
            scrConductor conductor = ADOBase.conductor;
            scrController controller = ADOBase.controller;
            if (video == null || conductor == null || controller == null || controller.paused
                || !conductor.hasSongStarted || !video.gameObject.activeSelf || !video.isPrepared
                || Persistence.visualEffects != VisualEffects.Full)
                return;

            if (!TryGetTargetVideoTime(vfx, conductor, video, out double targetTime)) return;
            int id = vfx.GetInstanceID();
            if (!States.TryGetValue(id, out SyncState state))
            {
                state = new SyncState();
                States[id] = state;
            }

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
                video.time = targetTime;
                video.Play();
                state.StartupFramesLeft = StartupSyncFrames;
                state.StartupSeekAttempts = 0;
            }

            video.playbackSpeed = conductor.song.pitch;
            double error = Mathf.Abs((float)(video.time - targetTime));
            bool startup = state.StartupFramesLeft > 0;
            bool shouldSeek = (startup && error > SoftDesyncSeconds && state.StartupSeekAttempts < MaxStartupSeekAttempts)
                || error > HardDesyncSeconds;
            if (shouldSeek)
            {
                float cooldown = startup ? StartupSeekCooldown : RuntimeSeekCooldown;
                if (Time.unscaledTime - state.LastSeekRealtime >= cooldown)
                {
                    video.time = targetTime;
                    state.LastSeekRealtime = Time.unscaledTime;
                    state.StartupSeekAttempts++;
                }
            }

            if (startup) state.StartupFramesLeft--;
            state.WasPlaying = video.isPlaying;
            state.WasMarkedPlayed = vfx.hasPlayed;
        }

        private static bool TryGetTargetVideoTime(scrVfxPlus vfx, scrConductor conductor, VideoPlayer video, out double targetTime)
        {
            double countdownOffset = conductor.separateCountdownTime
                ? conductor.crotchetAtStart * conductor.adjustedCountdownTicks : 0.0;
            targetTime = conductor.songposition_minusi - countdownOffset + vfx.vidOffset;
            if (targetTime < 0.0) return false;
            double length = video.length;
            if (length <= 0.0) return true;
            if (video.isLooping) targetTime %= length;
            else targetTime = System.Math.Min(targetTime, System.Math.Max(0.0, length - 0.001));
            return true;
        }

        private sealed class SyncState
        {
            public bool WasPlaying;
            public bool WasMarkedPlayed;
            public int StartupFramesLeft;
            public int StartupSeekAttempts;
            public float LastSeekRealtime = -100f;
        }
    }
}

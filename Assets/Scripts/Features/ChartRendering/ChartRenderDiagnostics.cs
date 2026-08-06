using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EditorTweaks.Features.ChartRendering
{
    internal static class ChartRenderDiagnostics
    {
        private static readonly object Sync = new object();
        private static string logPath = string.Empty;
        private static bool active;
        private static int currentFrame;
        private static double lastSongPosition = double.NaN;
        private static int autoHits;
        private static int failedAutoHits;
        private static int floorJumps;
        private static int suppressedAsyncAdjusts;

        public static void Begin(string path)
        {
            lock (Sync)
            {
                logPath = path;
                currentFrame = 0;
                lastSongPosition = double.NaN;
                autoHits = 0;
                failedAutoHits = 0;
                floorJumps = 0;
                suppressedAsyncAdjusts = 0;
                active = true;

                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? string.Empty);
                File.WriteAllText(logPath, "Chart render diagnostics started " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine, Encoding.UTF8);
            }
        }

        public static void End()
        {
            if (!active)
            {
                return;
            }

            Log("Diagnostics ended. autoHits=" + autoHits + " failedAutoHits=" + failedAutoHits + " floorJumps=" + floorJumps + " suppressedAsyncAdjusts=" + suppressedAsyncAdjusts);
            active = false;
        }

        public static void SetFrame(int frame)
        {
            currentFrame = Math.Max(0, frame);
        }

        public static void Log(string message)
        {
            if (!active || string.IsNullOrEmpty(logPath))
            {
                return;
            }

            lock (Sync)
            {
                File.AppendAllText(
                    logPath,
                    DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " [frame " + currentFrame + "] " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
        }

        public static void LogFrame(int frame, int frameLimit)
        {
            SetFrame(frame);
            if (!active)
            {
                return;
            }

            scrConductor? conductor = ADOBase.conductor;
            scrController? controller = ADOBase.controller;
            double songPosition = conductor == null ? double.NaN : conductor.songposition_minusi;
            bool songMovedBackward = conductor != null
                && conductor.hasSongStarted
                && !double.IsNaN(lastSongPosition)
                && songPosition + 0.000001 < lastSongPosition;
            bool failed = IsFailureState(controller);
            lastSongPosition = songPosition;

            if (!songMovedBackward && !failed)
            {
                return;
            }

            LogState(frameLimit, songMovedBackward ? "SONG_MOVED_BACKWARD" : "PLAYER_FAILED", conductor, controller);
        }

        public static void LogAutoHit(scrPlayer player, scrFloor beforeFloor, double songPosition, bool success, int hitIndex)
        {
            if (!active)
            {
                return;
            }

            scrFloor afterFloor = player.currFloor;
            PlanetarySystem planetarySystem = player.planetarySystem;
            scrPlanet planet = planetarySystem.chosenPlanet;
            int beforeSeq = beforeFloor == null ? -1 : beforeFloor.seqID;
            int afterSeq = afterFloor == null ? -1 : afterFloor.seqID;
            bool jumped = afterSeq - beforeSeq > 1;
            autoHits++;

            if (!success)
            {
                failedAutoHits++;
            }

            if (jumped)
            {
                floorJumps++;
            }

            if (success && !jumped)
            {
                return;
            }

            Log(
                "AUTO_HIT hitIndex=" + hitIndex
                + " success=" + success
                + " player=" + player.playerID
                + " before=" + beforeSeq
                + " after=" + afterSeq
                + " targetTime=" + Number(beforeFloor?.nextfloor == null ? double.NaN : beforeFloor.nextfloor.entryTime)
                + " song=" + Number(songPosition)
                + " lastHit=" + Number(player.lastHit)
                + " speed=" + Number(planetarySystem.speed)
                + " isCW=" + planetarySystem.isCW
                + " angle=" + Number(planet.angle)
                + " target=" + Number(planet.targetExitAngle)
                + (jumped ? " FLOOR_JUMP" : string.Empty));
        }

        public static void RecordAsyncAdjustSuppressed()
        {
            if (!active)
            {
                return;
            }

            suppressedAsyncAdjusts++;
        }

        private static void LogState(int frameLimit, string reason, scrConductor? conductor, scrController? controller)
        {
            scrPlayer? player = controller?.playerOne;
            PlanetarySystem? planetarySystem = player?.planetarySystem;
            scrPlanet? planet = planetarySystem?.chosenPlanet;
            scrFloor? floor = player?.currFloor;
            scrFloor? nextFloor = floor?.nextfloor;
            double songPosition = conductor == null ? double.NaN : conductor.songposition_minusi;

            Log(
                "STATE reason=" + reason
                + " limit=" + frameLimit
                + " state=" + (controller == null ? "null" : controller.state.ToString())
                + " song=" + Number(songPosition)
                + " delta=" + Number(conductor == null ? double.NaN : conductor.deltaSongPos)
                + " started=" + (conductor != null && conductor.hasSongStarted)
                + " currentSeq=" + (controller == null ? -1 : controller.currentSeqID)
                + " player=" + (player == null ? -1 : player.playerID)
                + " alive=" + (player != null && player.alive)
                + " floor=" + (floor == null ? -1 : floor.seqID)
                + " next=" + (nextFloor == null ? -1 : nextFloor.seqID)
                + " nextTime=" + Number(nextFloor == null ? double.NaN : nextFloor.entryTime)
                + " lastHit=" + Number(player == null ? double.NaN : player.lastHit)
                + " actualLastHit=" + Number(player == null ? double.NaN : player.actualLastHit)
                + " speed=" + Number(planetarySystem == null ? double.NaN : planetarySystem.speed)
                + " isCW=" + (planetarySystem != null && planetarySystem.isCW)
                + " angle=" + Number(planet == null ? double.NaN : planet.angle)
                + " target=" + Number(planet == null ? double.NaN : planet.targetExitAngle)
                + " snapped=" + Number(planet == null ? double.NaN : planet.snappedLastAngle));
        }

        private static bool IsFailureState(scrController? controller)
        {
            if (controller == null)
            {
                return false;
            }

            string state = controller.state.ToString();
            return state == "Fail" || state == "Fail2";
        }

        private static string Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}

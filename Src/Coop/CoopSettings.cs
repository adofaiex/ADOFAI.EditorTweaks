#nullable disable
using System;
using System.IO;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Coop
{
    internal enum MicMode
    {
        Off = 0,
        Open = 1,
        PushToTalk = 2,
    }

    /// <summary>多人制谱的设置（存在 Mod 的 Settings.xml 里，改了立刻保存）。</summary>
    internal static class CoopSettings
    {
        private static Settings S => Main.Settings;

        public static MicMode Mic
        {
            get => S == null ? MicMode.Off : (MicMode)Mathf.Clamp(S.VoiceMode, 0, 2);
            set
            {
                if (S == null) return;
                S.VoiceMode = (int)value;
                if (value != MicMode.Off) S.VoiceLastMode = (int)value;
                Save();
            }
        }

        /// <summary>聊天页的麦克风按钮：关 ↔ 上次用的模式。</summary>
        public static void ToggleMic()
        {
            if (S == null) return;
            Mic = Mic == MicMode.Off ? (MicMode)Mathf.Clamp(S.VoiceLastMode, 1, 2) : MicMode.Off;
        }

        public static KeyCode PushKey
        {
            get => S?.VoicePushKey ?? KeyCode.Mouse3;
            set => Set(() => S.VoicePushKey = value);
        }

        public static float Volume
        {
            get => S == null ? 1f : Mathf.Clamp(S.VoiceVolume, 0f, 2f);
            set => Set(() => S.VoiceVolume = Mathf.Clamp(value, 0f, 2f));
        }

        public static bool ChatNotify
        {
            get => S == null || S.CoopChatNotify;
            set => Set(() => S.CoopChatNotify = value);
        }

        public static bool InviteOnly
        {
            get => S != null && S.CoopInviteOnly;
            set
            {
                Set(() => S.CoopInviteOnly = value);
                CoopSession.ApplyRoomVisibility();
            }
        }

        public static int MaxMembers
        {
            get => S == null ? 8 : Mathf.Clamp(S.CoopMaxMembers, 2, 8);
            set => Set(() => S.CoopMaxMembers = Mathf.Clamp(value, 2, 8));
        }

        public static bool AutoVerify
        {
            get => S == null || S.CoopAutoVerify;
            set => Set(() => S.CoopAutoVerify = value);
        }

        /// <summary>自定义的下载目录；没设或者目录不存在时是 null（用默认目录）。</summary>
        public static string DownloadDir
        {
            get
            {
                string dir = S?.CoopDownloadDir;
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
            }
            set => Set(() => S.CoopDownloadDir = value ?? string.Empty);
        }

        public static KeyCode PanelKey
        {
            get => S?.CoopPanelKey ?? KeyCode.F9;
            set => Set(() => S.CoopPanelKey = value);
        }

        /// <summary>按键在界面上怎么显示。</summary>
        public static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "未设置";
                case KeyCode.Mouse2: return "鼠标中键";
                case KeyCode.Mouse3: return "鼠标侧键 1";
                case KeyCode.Mouse4: return "鼠标侧键 2";
                case KeyCode.Mouse5: return "鼠标键 6";
                case KeyCode.Mouse6: return "鼠标键 7";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Space: return "空格";
                case KeyCode.LeftControl: return "左 Ctrl";
                case KeyCode.RightControl: return "右 Ctrl";
                case KeyCode.LeftAlt: return "左 Alt";
                case KeyCode.RightAlt: return "右 Alt";
                case KeyCode.LeftShift: return "左 Shift";
                case KeyCode.RightShift: return "右 Shift";
                case KeyCode.CapsLock: return "Caps Lock";
                default:
                    string name = key.ToString();
                    return name.StartsWith("Alpha", StringComparison.Ordinal) ? name.Substring(5) : name;
            }
        }

        private static void Set(Action apply)
        {
            if (S == null) return;
            apply();
            Save();
        }

        private static void Save()
        {
            if (Main.Mod != null) S.Save(Main.Mod);
        }
    }
}

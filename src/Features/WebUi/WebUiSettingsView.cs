using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.WebUi
{
    internal static class WebUiSettingsView
    {
        private static bool capturing;

        public static void Draw(Settings settings)
        {
            if (capturing && WebUiHotkey.TryCapture(Event.current, out string hotkey))
            {
                settings.WebUiOpenHotkey = hotkey;
                settings.Save(Main.Mod!);
                capturing = false;
                Event.current.Use();
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Web 设置页面");
            GUILayout.BeginHorizontal();
            GUILayout.Label("打开 Web 设置页面快捷键", GUILayout.Width(220f));
            if (capturing)
            {
                GUILayout.Label("请按下组合键…", GUILayout.Width(160f));
            }
            else if (GUILayout.Button("录入快捷键", GUILayout.Width(120f)))
            {
                capturing = true;
            }

            GUILayout.Label(settings.WebUiOpenHotkey, GUILayout.Width(140f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("服务仅监听本机地址，启用 Mod 后按下快捷键打开设置页面。", GUI.skin.label);
            GUILayout.EndVertical();
        }
    }
}

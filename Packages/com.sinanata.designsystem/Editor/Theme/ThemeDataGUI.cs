using System.Collections.Generic;
using System.Reflection;
using DesignSystem.Runtime.Theme.Data;
using UnityEditor;
using UnityEngine;

namespace DesignSystem.Editor.Theming
{
    /// <summary>
    /// The ThemeData inspector body, in one place.
    ///
    /// Both hosts that draw a theme — the custom inspector and the Theme Configurator window —
    /// used to carry their own verbatim copy of the [Header] grouping, the foldout state, the
    /// per-type field drawing and the save / revert dance. Around a hundred duplicated lines,
    /// which is a hundred lines to forget to update in one of the two places.
    /// </summary>
    internal static class ThemeDataGUI
    {
        private const string ScopeHeader   = "Scope";
        private const string DefaultHeader = "General";

        /// <summary>
        /// True when it is safe to put a modal dialog in front of the user.
        ///
        /// `OnDisable` and `OnDestroy` do not only fire when someone closes a window: they also
        /// fire on domain reload, on recompile, and on entering play mode. An "unsaved changes"
        /// dialog raised from there interrupts a Ctrl+R or a Play click with a question the user
        /// never asked, and a modal during a reload can wedge the editor outright.
        /// </summary>
        public static bool CanPrompt =>
            !EditorApplication.isCompiling &&
            !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        /// Draws every token field, grouped by its <c>[Header]</c>. Returns true if the user
        /// changed something this frame. <paramref name="foldouts"/> is the host's own state, so
        /// the inspector and the window each remember their own expansion.
        /// </summary>
        public static bool Draw(SerializedObject so, IDictionary<string, bool> foldouts)
        {
            so.Update();
            EditorGUI.BeginChangeCheck();

            foreach (var group in Group(so))
            {
                var expanded = !foldouts.TryGetValue(group.Key, out var remembered) || remembered;
                expanded = EditorGUILayout.Foldout(expanded, group.Key, true);
                foldouts[group.Key] = expanded;

                if (expanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var prop in group.Value) DrawProperty(prop);
                    if (group.Key == ScopeHeader) DrawScopeHint();
                    EditorGUI.indentLevel--;
                }

                GUILayout.Space(4);
            }

            var changed = EditorGUI.EndChangeCheck();
            so.ApplyModifiedProperties();
            return changed;
        }

        /// <summary>
        /// The bake-state banner. Shared so the two hosts cannot disagree on what "this theme is
        /// out of date" means — the values a designer sees and the stylesheet the runtime uses
        /// are two different things, and the gap between them is invisible without this.
        ///
        /// Drawn in EVERY state, including the healthy one. IMGUI hands out control IDs off a
        /// running per-window counter, and a HelpBox spends one of them — so a banner that only
        /// appears when it has something to say shifts every field below it on the frame it shows
        /// up. A colour picker opened before the shift keeps writing to the ID it captured, which
        /// now belongs to nobody: you pick a second colour and the field does not move. A banner
        /// that is always here cannot shift anything.
        /// </summary>
        public static void DrawBakeState(ThemeData theme, bool dirty)
        {
            var message = "Baked and up to date. The stylesheet matches the values below.";
            var type    = MessageType.Info;

            if (!theme)
            {
                message = "No theme asset here, so there is nothing to bake.";
                type    = MessageType.Error;
            }
            else if (!theme.StyleSheet)
            {
                message = "This theme has never been baked, so it paints nothing at runtime. Press Save.";
                type    = MessageType.Warning;
            }
            else if (dirty)
            {
                message = "Unsaved changes. The baked stylesheet still holds the LAST SAVED values, so the " +
                          "runtime is showing something other than what you see here. Press Save to re-bake.";
                type    = MessageType.Info;
            }

            EditorGUILayout.HelpBox(message, type);
        }

        private static void DrawScopeHint()
        {
            EditorGUILayout.HelpBox(
                ":root paints the whole panel. One theme at a time.\n\n" +
                ".theme-x paints only a subtree carrying that class, so two themes can live in one " +
                "panel and the applier adds the class for you. The design system's own Light theme " +
                "is scoped this way.",
                MessageType.None);
        }

        private static void DrawProperty(SerializedProperty prop)
        {
            var label = new GUIContent(prop.displayName);

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Color:
                    prop.colorValue = EditorGUILayout.ColorField(label, prop.colorValue);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = EditorGUILayout.FloatField(label, prop.floatValue);
                    break;
                default:
                    EditorGUILayout.PropertyField(prop, label, true);
                    break;
            }
        }

        // Walks the fields in declaration order and starts a new group at every [Header], which
        // is exactly how Unity's own default inspector reads the same attributes — so the
        // grouping here always matches what a plain ThemeData would look like.
        private static List<KeyValuePair<string, List<SerializedProperty>>> Group(SerializedObject so)
        {
            var groups  = new List<KeyValuePair<string, List<SerializedProperty>>>();
            var header  = DefaultHeader;
            List<SerializedProperty> current = null;

            foreach (var field in typeof(ThemeData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var prop = so.FindProperty(field.Name);
                if (prop == null) continue;

                var attr = field.GetCustomAttribute<HeaderAttribute>();
                if (attr != null) header = attr.header;

                if (current == null || groups[groups.Count - 1].Key != header)
                {
                    current = new List<SerializedProperty>();
                    groups.Add(new KeyValuePair<string, List<SerializedProperty>>(header, current));
                }

                current.Add(prop.Copy());
            }

            return groups;
        }
    }
}

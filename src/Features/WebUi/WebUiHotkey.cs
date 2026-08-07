using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.WebUi
{
    internal static class WebUiHotkey
    {
        public const string Default = "Ctrl+Shift+E";

        private static readonly Dictionary<string, KeyCode> aliases = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = KeyCode.Escape,
            ["Escape"] = KeyCode.Escape,
            ["Enter"] = KeyCode.Return,
            ["Space"] = KeyCode.Space,
            ["Tab"] = KeyCode.Tab,
            ["Backspace"] = KeyCode.Backspace,
            ["Delete"] = KeyCode.Delete,
            ["Insert"] = KeyCode.Insert,
            ["Left"] = KeyCode.LeftArrow,
            ["Right"] = KeyCode.RightArrow,
            ["Up"] = KeyCode.UpArrow,
            ["Down"] = KeyCode.DownArrow,
            ["PageUp"] = KeyCode.PageUp,
            ["PageDown"] = KeyCode.PageDown,
            ["Home"] = KeyCode.Home,
            ["End"] = KeyCode.End,
            ["Plus"] = KeyCode.Plus,
            ["Minus"] = KeyCode.Minus,
            ["Equals"] = KeyCode.Equals,
        };

        public static bool IsPressed(string value)
        {
            if (!TryParse(value, out ModifierFlags modifiers, out KeyCode key))
            {
                return false;
            }

            if (!Input.GetKeyDown(key))
            {
                return false;
            }

            return IsModifierPressed(modifiers, ModifierFlags.Control, KeyCode.LeftControl, KeyCode.RightControl)
                && IsModifierPressed(modifiers, ModifierFlags.Shift, KeyCode.LeftShift, KeyCode.RightShift)
                && IsModifierPressed(modifiers, ModifierFlags.Alt, KeyCode.LeftAlt, KeyCode.RightAlt)
                && IsModifierPressed(modifiers, ModifierFlags.Command, KeyCode.LeftCommand, KeyCode.RightCommand);
        }

        public static bool TryCapture(Event currentEvent, out string normalized)
        {
            normalized = string.Empty;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown || currentEvent.keyCode == KeyCode.None)
            {
                return false;
            }

            if (IsModifierKey(currentEvent.keyCode))
            {
                return false;
            }

            ModifierFlags modifiers = ModifierFlags.None;
            if (currentEvent.control)
            {
                modifiers |= ModifierFlags.Control;
            }

            if (currentEvent.shift)
            {
                modifiers |= ModifierFlags.Shift;
            }

            if (currentEvent.alt)
            {
                modifiers |= ModifierFlags.Alt;
            }

            if (currentEvent.command)
            {
                modifiers |= ModifierFlags.Command;
            }

            normalized = Format(modifiers, currentEvent.keyCode);
            return true;
        }

        public static string NormalizeOrDefault(string value)
        {
            return TryParse(value, out ModifierFlags modifiers, out KeyCode key)
                ? Format(modifiers, key)
                : Default;
        }

        private static bool TryParse(string value, out ModifierFlags modifiers, out KeyCode key)
        {
            modifiers = ModifierFlags.None;
            key = KeyCode.None;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierFlags.Control;
                    continue;
                }

                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierFlags.Shift;
                    continue;
                }

                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Option", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierFlags.Alt;
                    continue;
                }

                if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Command", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Cmd", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierFlags.Command;
                    continue;
                }

                if (key != KeyCode.None || !TryParseKey(part, out key))
                {
                    return false;
                }
            }

            return key != KeyCode.None && !IsModifierKey(key);
        }

        private static bool TryParseKey(string value, out KeyCode key)
        {
            if (aliases.TryGetValue(value, out key))
            {
                return true;
            }

            if (value.Length == 1 && char.IsLetterOrDigit(value[0]))
            {
                string candidate = char.ToUpperInvariant(value[0]).ToString();
                if (Enum.TryParse(candidate, true, out key))
                {
                    return true;
                }
            }

            return Enum.TryParse(value, true, out key);
        }

        private static string Format(ModifierFlags modifiers, KeyCode key)
        {
            List<string> parts = new List<string>();
            if ((modifiers & ModifierFlags.Control) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((modifiers & ModifierFlags.Alt) != 0)
            {
                parts.Add("Alt");
            }

            if ((modifiers & ModifierFlags.Shift) != 0)
            {
                parts.Add("Shift");
            }

            if ((modifiers & ModifierFlags.Command) != 0)
            {
                parts.Add("Win");
            }

            parts.Add(FormatKey(key));
            return string.Join("+", parts);
        }

        private static string FormatKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Return:
                    return "Enter";
                case KeyCode.LeftArrow:
                    return "Left";
                case KeyCode.RightArrow:
                    return "Right";
                case KeyCode.UpArrow:
                    return "Up";
                case KeyCode.DownArrow:
                    return "Down";
                case KeyCode.LeftBracket:
                    return "[";
                case KeyCode.RightBracket:
                    return "]";
                default:
                    return key.ToString();
            }
        }

        private static bool IsModifierKey(KeyCode key)
        {
            return key == KeyCode.LeftControl
                || key == KeyCode.RightControl
                || key == KeyCode.LeftShift
                || key == KeyCode.RightShift
                || key == KeyCode.LeftAlt
                || key == KeyCode.RightAlt
                || key == KeyCode.LeftCommand
                || key == KeyCode.RightCommand;
        }

        private static bool IsModifierPressed(ModifierFlags configured, ModifierFlags flag, KeyCode left, KeyCode right)
        {
            if ((configured & flag) == 0)
            {
                return !Input.GetKey(left) && !Input.GetKey(right);
            }

            return Input.GetKey(left) || Input.GetKey(right);
        }

        [Flags]
        private enum ModifierFlags
        {
            None = 0,
            Control = 1,
            Shift = 2,
            Alt = 4,
            Command = 8
        }
    }
}

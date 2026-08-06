using ADOFAI;
using ADOFAI.Editor.Components;
using ADOFAI.LevelEditor.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace EditorTweaks.Features.NumericDrag
{
    internal static class NumericDragFeature
    {
        public static void Attach(PropertyControl_Text control)
        {
            if (!Main.Settings.EnableNumericDrag || control == null || control.inputField == null || control.propertyInfo == null)
            {
                return;
            }

            PropertyType type = control.propertyInfo.type;
            if (type != PropertyType.Int && type != PropertyType.Float && type != PropertyType.Tile)
            {
                return;
            }

            bool integer = type == PropertyType.Int || type == PropertyType.Tile;
            Attach(
                control.inputField,
                control.propertyInfo,
                () => control.inputField.onEndEdit.Invoke(control.inputField.text),
                () => LiveApply(control),
                integer);
        }

        public static void Attach(PropertyControl_Vector2 control)
        {
            if (!Main.Settings.EnableNumericDrag || control == null || control.propertyInfo == null)
            {
                return;
            }

            Attach(
                control.inputX,
                control.propertyInfo,
                () => control.inputX.onEndEdit.Invoke(control.inputX.text),
                () => LiveApply(control),
                integer: false,
                min: control.propertyInfo.minVec.x,
                max: control.propertyInfo.maxVec.x);
            Attach(
                control.inputY,
                control.propertyInfo,
                () => control.inputY.onEndEdit.Invoke(control.inputY.text),
                () => LiveApply(control),
                integer: false,
                min: control.propertyInfo.minVec.y,
                max: control.propertyInfo.maxVec.y);
        }

        private static void Attach(TMP_InputField field, PropertyInfo propertyInfo, System.Action commit, System.Action liveApply, bool integer, float? min = null, float? max = null)
        {
            if (field == null || field.gameObject.GetComponent<EditorTweaksNumericDragMarker>() != null)
            {
                return;
            }

            EditorTweaksNumericDragMarker marker = field.gameObject.AddComponent<EditorTweaksNumericDragMarker>();
            marker.Commit = commit;
            marker.LiveApply = liveApply;

            DraggableNumberInputField drag = field.gameObject.AddComponent<DraggableNumberInputField>();
            drag.field = field;
            drag.arrows = new GameObject[0];
            drag.onDrag = new UnityEvent();
            drag.axis = DraggableNumberInputField.Axis.Horizontal;
            drag.clamp = !propertyInfo.ignoreRange;
            drag.stepPerPixel = integer ? Main.Settings.IntStepPerPixel : Main.Settings.FloatStepPerPixel;
            drag.maxFloatingPoints = integer ? 0 : Main.Settings.MaxFloatingPoints;

            if (integer)
            {
                drag.min = propertyInfo.type == PropertyType.Tile ? propertyInfo.int_min : GetIntMin(propertyInfo);
                drag.max = propertyInfo.type == PropertyType.Tile ? propertyInfo.int_max : GetIntMax(propertyInfo);
            }
            else
            {
                drag.min = min ?? propertyInfo.float_min;
                drag.max = max ?? propertyInfo.float_max;
            }

            drag.onDrag.AddListener(marker.ApplyLive);
        }

        private static void LiveApply(PropertyControl_Text control)
        {
            try
            {
                if (control == null || control.propertyInfo == null || control.inputField == null)
                {
                    return;
                }

                control.ValidateInput();

                ADOFAI.LevelEvent selectedEvent = control.propertiesPanel.inspectorPanel.selectedEvent;
                string propertyName = control.propertyInfo.name;
                object? value = ParseTextValue(control, selectedEvent);
                if (value == null)
                {
                    return;
                }

                if (propertyName == "floor")
                {
                    selectedEvent.floor = (int)value;
                }
                else
                {
                    selectedEvent[propertyName] = value;
                }

                control.ToggleOthersEnabled();
                if (control.propertyInfo.slider && control.parentControl is PropertyControl_Slider slider)
                {
                    slider.UpdateSliderValue(value);
                }

                RefreshAfterTextChange(control, selectedEvent);
            }
            catch
            {
                // The official input control can temporarily hold invalid text while editing.
            }
        }

        private static object? ParseTextValue(PropertyControl_Text control, ADOFAI.LevelEvent selectedEvent)
        {
            string text = control.inputField.text;
            switch (control.propertyInfo.type)
            {
                case PropertyType.Int:
                    return int.Parse(text);
                case PropertyType.Float:
                    return float.Parse(text);
                case PropertyType.Tile:
                    System.Tuple<int, TileRelativeTo>? tile = selectedEvent[control.propertyInfo.name] as System.Tuple<int, TileRelativeTo>;
                    return new System.Tuple<int, TileRelativeTo>(int.Parse(text), tile?.Item2 ?? TileRelativeTo.ThisTile);
                default:
                    return null;
            }
        }

        private static void RefreshAfterTextChange(PropertyControl_Text control, ADOFAI.LevelEvent selectedEvent)
        {
            if (selectedEvent.eventType == LevelEventType.BackgroundSettings)
            {
                ADOBase.customLevel.SetBackground();
            }
            else if (selectedEvent.IsDecoration)
            {
                ADOBase.editor.UpdateDecorationObject(selectedEvent);
            }

            control.ApplyTileChanges();
            if (ADOBase.editor.SelectionIsSingle())
            {
                ADOBase.editor.ShowEventIndicators(ADOBase.editor.selectedFloors[0]);
            }

            control.OnValueChange();
        }

        private static void LiveApply(PropertyControl_Vector2 control)
        {
            try
            {
                if (control == null || control.propertyInfo == null || control.inputX == null || control.inputY == null)
                {
                    return;
                }

                control.ValidateInput();

                ADOFAI.LevelEvent selectedEvent = control.propertiesPanel.inspectorPanel.selectedEvent;
                float x = float.Parse(ConvertEmptyToNaN(control.inputX.text));
                float y = float.Parse(ConvertEmptyToNaN(control.inputY.text));
                selectedEvent[control.propertyInfo.name] = new Vector2(x, y);

                control.ToggleOthersEnabled();
                RefreshAfterVectorChange(control, selectedEvent);
            }
            catch
            {
                // The official input control can temporarily hold invalid text while editing.
            }
        }

        private static void RefreshAfterVectorChange(PropertyControl_Vector2 control, ADOFAI.LevelEvent selectedEvent)
        {
            if (selectedEvent.eventType == LevelEventType.BackgroundSettings)
            {
                ADOBase.customLevel.SetBackground();
            }
            else if (selectedEvent.IsDecoration)
            {
                ADOBase.editor.UpdateDecorationObject(selectedEvent);
            }

            if (selectedEvent.eventType == LevelEventType.PositionTrack
                || selectedEvent.eventType == LevelEventType.FreeRoam
                || selectedEvent.eventType == LevelEventType.FreeRoamTwirl
                || selectedEvent.eventType == LevelEventType.FreeRoamRemove
                || selectedEvent.eventType == LevelEventType.FreeRoamWarning)
            {
                ADOBase.editor.ApplyEventsToFloors();
                if (ADOBase.editor.SelectionIsSingle())
                {
                    ADOBase.editor.floorButtonCanvas.transform.position = ADOBase.editor.selectedFloors[0].transform.position;
                }
            }

            control.OnValueChange();
        }

        private static string ConvertEmptyToNaN(string value)
        {
            return value == string.Empty ? "NaN" : value;
        }

        private static int GetIntMin(PropertyInfo propertyInfo)
        {
            return propertyInfo.name == "floor" ? 0 : propertyInfo.int_min;
        }

        private static int GetIntMax(PropertyInfo propertyInfo)
        {
            if (propertyInfo.name != "floor" || scnGame.instance == null || scnGame.instance.levelMaker == null)
            {
                return propertyInfo.int_max;
            }

            return scnGame.instance.levelMaker.listFloors.Count - 1;
        }
    }
}

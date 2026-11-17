#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using ADOFAI;
using GDMiniJSON;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ADOFAI.EditorTweaks.Coop
{
    /// <summary>和游戏编辑器打交道的部分：读当前谱面、把收到的整份谱面换进编辑器、刷新素材。</summary>
    internal static class CoopEditor
    {
        private static readonly System.Reflection.PropertyInfo UnsavedChanges = AccessTools.Property(typeof(scnEditor), "unsavedChanges");

        /// <summary>编辑器场景里的编辑器；不在编辑器里是 null。</summary>
        public static scnEditor Editor
        {
            get
            {
                scnEditor editor = scnEditor.instance;
                return editor != null && editor.customLevel != null ? editor : null;
            }
        }

        /// <summary>编辑器里打开的谱面文件；没打开（新建未保存）是 null。</summary>
        public static string LevelPath
        {
            get
            {
                scnEditor editor = Editor;
                if (editor == null) return null;
                string path = editor.customLevel.levelPath;
                return string.IsNullOrEmpty(path) ? null : path;
            }
        }

        /// <summary>现在能不能把别人的修改换进来：不在加载、不在试玩、没按着鼠标、没在输入框里打字、不在一次编辑中途。</summary>
        public static bool CanApplyNow
        {
            get
            {
                scnEditor editor = Editor;
                if (editor == null || editor.isLoading || editor.changingState != 0 || !editor.initialized) return false;
                if (editor.playMode) return false;
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) return false;
                return !IsTyping();
            }
        }

        /// <summary>当前谱面编码成 .adofai 的 JSON（和保存时写进文件的一样）。</summary>
        public static string EncodeLevel()
        {
            return Editor?.levelData.Encode();
        }

        public static void Notify(string text)
        {
            try
            {
                Editor?.ShowNotification(text);
            }
            catch (Exception)
            {
                // 编辑器界面还没准备好
            }
        }

        /// <summary>
        /// 把一整份谱面换进编辑器（做法照游戏自己的撤销：替换 levelData 后重建路径和装饰，再尽量恢复选择）。
        /// 换进来之后本地的撤销记录会清空（它们是旧谱面的整份拷贝，撤销会把别人的修改也撤掉）。
        /// </summary>
        public static bool ApplyLevelJson(string json, out string error)
        {
            error = null;
            scnEditor editor = Editor;
            if (editor == null)
            {
                error = "不在编辑器里";
                return false;
            }

            if (!(Json.Deserialize(json) is Dictionary<string, object> dict))
            {
                error = "谱面数据解析失败";
                return false;
            }

            var data = new LevelData();
            data.Decode(dict, out LoadResult status);
            if (status != LoadResult.Successful)
            {
                error = "谱面数据读取失败：" + status;
                return false;
            }

            // 记下当前选择，换完再选回来
            var floors = new List<int>();
            foreach (scrFloor floor in editor.selectedFloors)
                if (floor != null) floors.Add(floor.seqID);
            var decorations = new List<int>();
            foreach (LevelEvent decoration in editor.selectedDecorations)
            {
                int index = editor.levelData.decorations.IndexOf(decoration);
                if (index >= 0) decorations.Add(index);
            }

            LevelEventType settingsType = editor.settingsPanel.selectedEventType;
            LevelEventType floorEventType = editor.levelEventsPanel.selectedEventType;
            int floorEventIndex = editor.levelEventsPanel.EventNumOfTab(floorEventType);

            editor.changingState++;
            try
            {
                editor.customLevel.levelData = data;
                editor.DeselectFloors();
                editor.RemakePath();
                editor.DeselectAllDecorations();
                editor.UpdateDecorationObjects();
                editor.UpdateSongAndLevelSettings();

                foreach (int index in decorations)
                {
                    if (index < data.decorations.Count)
                        editor.SelectDecoration(data.decorations[index], jumpToDecoration: false, showPanel: false, ignoreDeselection: true);
                }

                if (!editor.SelectionDecorationIsEmpty())
                {
                    LevelEvent last = editor.selectedDecorations[editor.selectedDecorations.Count - 1];
                    editor.levelEventsPanel.ShowInspector(show: true, forceAction: true);
                    editor.levelEventsPanel.ShowPanel(last.eventType);
                }
                else if (editor.particleEditor != null && editor.particleEditor.gameObject.activeSelf)
                {
                    editor.HideParticleEditor();
                }

                editor.propertyControlDecorationsList.RefreshItemsList(forceRefreshAll: true);

                List<scrFloor> list = editor.floors;
                floors.RemoveAll(seq => seq < 0 || seq >= list.Count);
                if (floors.Count > 1)
                {
                    editor.MultiSelectFloors(list[floors[0]], list[floors[floors.Count - 1]]);
                }
                else if (floors.Count == 1)
                {
                    editor.SelectFloor(list[floors[0]], cameraJump: false);
                    editor.levelEventsPanel.ShowPanel(floorEventType, floorEventIndex);
                }

                editor.settingsPanel.ShowPanel(settingsType);
                RefreshLevelAssets(editor, force: false);
            }
            finally
            {
                editor.changingState--;
            }

            editor.undoStates.Clear();
            editor.redoStates.Clear();
            MarkUnsaved(editor);
            return true;
        }

        /// <summary>
        /// 把别人的一组修改应用到编辑器（第二阶段的增量同步）：改 levelData 和记录，再只刷新受影响的部分，
        /// 尽量保持选中的砖块（按砖块 ID 找回）和装饰，检查器正在显示的对象被改了就重新显示。
        /// </summary>
        public static bool ApplyOps(CoopModel model, List<CoopOp> ops, out string error)
        {
            error = null;
            scnEditor editor = Editor;
            if (editor == null)
            {
                error = "不在编辑器里";
                return false;
            }

            var selectedTiles = new List<long>();
            foreach (scrFloor floor in editor.selectedFloors)
                if (floor != null) selectedTiles.Add(model.TileOf(floor.seqID));
            LevelEvent shownEvent = editor.levelEventsPanel.selectedEvent;
            LevelEventType settingsType = editor.settingsPanel.selectedEventType;

            CoopModel.ApplyResult result;
            editor.changingState++;
            try
            {
                result = model.Apply(editor.levelData, ops);
                if (!result.Any) return true;

                if (result.TilesChanged)
                {
                    editor.DeselectFloors();
                    editor.RemakePath();
                    ReselectFloors(editor, model, selectedTiles);
                }
                else if (result.EventsChanged)
                {
                    editor.ApplyEventsToFloors();
                }

                bool decorationRemoved = false;
                foreach (LevelEvent decoration in editor.selectedDecorations)
                    if (result.Removed.Contains(decoration)) decorationRemoved = true;
                if (decorationRemoved)
                {
                    var keep = new List<LevelEvent>(editor.selectedDecorations);
                    keep.RemoveAll(result.Removed.Contains);
                    editor.DeselectAllDecorations();
                    foreach (LevelEvent decoration in keep)
                        editor.SelectDecoration(decoration, jumpToDecoration: false, showPanel: false, ignoreDeselection: true);
                }

                if (result.DecorationsRebuilt)
                {
                    editor.UpdateDecorationObjects();
                    editor.propertyControlDecorationsList.RefreshItemsList(forceRefreshAll: true);
                }
                else if (result.DecorationsChanged.Count > 0)
                {
                    foreach (LevelEvent decoration in result.DecorationsChanged) editor.UpdateDecorationObject(decoration);
                    editor.propertyControlDecorationsList.RefreshItemsList();
                }

                if (result.SettingsChanged)
                {
                    editor.UpdateSongAndLevelSettings();
                    editor.settingsPanel.ShowPanel(settingsType);
                    RefreshLevelAssets(editor, force: false);
                }

                // 检查器正显示的事件被别人改了 / 删了：重新显示（删了就收起）
                if (shownEvent != null && (result.Touched.Contains(shownEvent) || result.Removed.Contains(shownEvent)))
                {
                    if (result.Removed.Contains(shownEvent)) editor.levelEventsPanel.HideAllInspectorTabs();
                    else if (shownEvent.IsDecoration) editor.levelEventsPanel.ShowPanel(shownEvent.eventType);
                    else if (editor.selectedFloors.Count == 1) editor.levelEventsPanel.ShowPanelOfEvent(shownEvent);
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Main.Log("[Coop] Applying ops failed: " + exception);
                return false;
            }
            finally
            {
                editor.changingState--;
            }

            // 撤销记录是整份谱面的拷贝，撤销会把别人的修改也撤掉，所以清空（各自撤销在第三阶段做）
            editor.undoStates.Clear();
            editor.redoStates.Clear();
            MarkUnsaved(editor);
            return true;
        }

        private static void ReselectFloors(scnEditor editor, CoopModel model, List<long> tiles)
        {
            List<scrFloor> floors = editor.floors;
            var indices = new List<int>();
            foreach (long tile in tiles)
            {
                int index = model.FloorOf(tile);
                if (index >= 0 && index < floors.Count) indices.Add(index);
            }

            if (indices.Count == 0) return;
            indices.Sort();
            if (indices.Count > 1) editor.MultiSelectFloors(floors[indices[0]], floors[indices[indices.Count - 1]]);
            else editor.SelectFloor(floors[indices[0]], cameraJump: false);
        }

        /// <summary>整份谱面 JSON 的校验值（房主和客人比较用）。</summary>
        public static string HashLevel()
        {
            string json = EncodeLevel();
            if (json == null) return string.Empty;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return FileHashes.ToHex(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json)));
        }

        /// <summary>
        /// 新文件到了之后刷新图片、音频、视频，再重建装饰物体（新的装饰图片要重新读）。
        /// 不能用游戏的 ReloadAssets(force: true, reloadDecorations: true)：它会清空所有装饰物体却不重建，
        /// 装饰全部消失，装饰列表还会每帧报空引用。
        /// </summary>
        public static void ReloadAssets()
        {
            scnEditor editor = Editor;
            if (editor == null || editor.isLoading) return;
            try
            {
                RefreshLevelAssets(editor, force: true);
                editor.UpdateDecorationObjects();
                editor.propertyControlDecorationsList.RefreshItemsList(forceRefreshAll: true);
            }
            catch (Exception exception)
            {
                Main.Log("[Coop] ReloadAssets failed: " + exception.Message);
            }
        }

        /// <summary>
        /// 刷新歌曲、背景、轨道贴图、视频、自定义音效（相当于游戏 ReloadAssets 去掉"标记所有图片未使用再卸载"那一步：
        /// 那一步会把装饰正在用的图片也卸掉，装饰变成空白）。装饰物体不动。
        /// </summary>
        private static void RefreshLevelAssets(scnEditor editor, bool force)
        {
            scnGame level = editor.customLevel;
            level.ReloadSong(force);
            level.UpdateBackgroundSprites();
            level.UpdateFloorSprites();
            level.SetBackground();
            if (force) level.UpdateVideo();
            level.ReloadCustomSounds(force);
        }

        /// <summary>读一个 .adofai 文件的"谱面身份"（作者 + 艺术家 + 曲名），用来判断本地文件夹是不是同一张谱。</summary>
        public static string ReadIdentity(string levelPath)
        {
            try
            {
                if (!File.Exists(levelPath)) return null;
                if (!(Json.Deserialize(File.ReadAllText(levelPath)) is Dictionary<string, object> dict)) return null;
                if (!(dict.TryGetValue("settings", out object raw) && raw is Dictionary<string, object> settings)) return null;
                return Identity(Field(settings, "author"), Field(settings, "artist"), Field(settings, "song"));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>当前打开的谱面的身份。</summary>
        public static string CurrentIdentity()
        {
            LevelData data = Editor?.levelData;
            if (data == null) return string.Empty;
            return Identity(data.author ?? string.Empty, data.artist ?? string.Empty, data.song ?? string.Empty);
        }

        public static string CurrentSongName()
        {
            LevelData data = Editor?.levelData;
            if (data == null) return string.Empty;
            string artist = data.artist ?? string.Empty;
            string song = data.song ?? string.Empty;
            string name = StripTags(artist.Length > 0 && song.Length > 0 ? artist + " - " + song : artist + song);
            return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(LevelPath ?? "未命名");
        }

        private static string Identity(string author, string artist, string song)
        {
            return (author + "\n" + artist + "\n" + song).Trim();
        }

        private static string Field(Dictionary<string, object> settings, string key)
        {
            return settings.TryGetValue(key, out object value) && value != null ? value.ToString() : string.Empty;
        }

        private static string StripTags(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", string.Empty).Trim();
        }

        private static void MarkUnsaved(scnEditor editor)
        {
            try
            {
                UnsavedChanges?.SetValue(editor, true);
            }
            catch (Exception)
            {
                // 标题栏的"未保存"星号而已
            }
        }

        private static bool IsTyping()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return false;
            TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            InputField input = selected.GetComponent<InputField>();
            return input != null && input.isFocused;
        }
    }
}

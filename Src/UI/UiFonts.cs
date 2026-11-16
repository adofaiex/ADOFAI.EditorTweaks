#nullable disable
using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ADOFAI.EditorTweaks.UI
{
    /// <summary>
    /// 界面字体：用系统里的中文字体（微软雅黑等）动态生成，不往 Mod 里塞几十 MB 的字体文件。
    /// UI Toolkit 默认字体没有汉字，所以必须指定。
    /// </summary>
    public static class UiFonts
    {
        private static readonly string[] Families =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Source Han Sans SC", "WenQuanYi Micro Hei"
        };

        private static FontAsset _asset;
        private static bool _tried;

        /// <summary>给元素（及其全部子元素）设上界面字体。找不到合适的系统字体就保持默认。</summary>
        public static void Apply(VisualElement root)
        {
            FontAsset asset = Resolve();
            if (asset != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(asset);
        }

        private static FontAsset Resolve()
        {
            if (_asset != null || _tried) return _asset;
            _tried = true;
            foreach (string family in Families)
            {
                try
                {
                    FontAsset asset = FontAsset.CreateFontAsset(family, "Regular");
                    if (asset == null) continue;
                    _asset = asset;
                    break;
                }
                catch (Exception)
                {
                    // 这个字体系统里没有，试下一个
                }
            }

            return _asset;
        }
    }
}

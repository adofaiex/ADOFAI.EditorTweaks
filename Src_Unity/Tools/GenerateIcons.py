"""
生成设置界面用的图标（白色线条图标，128x128，透明底，运行时用 USS 着色）。

用法：python Tools/GenerateIcons.py
输出：Assets/EditorTweaksUI/Icons/*.png（带 .meta）、Assets/EditorTweaksUI/Styles/Icons.uss

图标在 24x24 的网格里设计，线宽 2，圆头圆角，先放大 8 倍画，再缩小做抗锯齿。
"""
import hashlib
import math
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.abspath(__file__))
UI_DIR = os.path.join(ROOT, "..", "Assets", "EditorTweaksUI")
ICON_DIR = os.path.join(UI_DIR, "Icons")
STYLE_DIR = os.path.join(UI_DIR, "Styles")

OUT = 128                 # 输出尺寸
SS = 8                    # 超采样倍数
CANVAS = OUT * SS         # 1024
UNIT = CANVAS / 28.0      # 24 格的图标外面留 2 格边
PAD = 2 * UNIT
STROKE = 2.0 * UNIT


def pt(x, y):
    return (PAD + x * UNIT, PAD + y * UNIT)


class Pen:
    def __init__(self, mirror=False):
        self.img = Image.new("L", (CANVAS, CANVAS), 0)
        self.d = ImageDraw.Draw(self.img)
        self.mirror = mirror

    def _p(self, x, y):
        if self.mirror:
            x = 24 - x
        return pt(x, y)

    def _dot(self, p, r):
        self.d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=255)

    def line(self, points, closed=False, width=2.0):
        w = width * UNIT
        pts = [self._p(x, y) for x, y in points]
        if closed:
            pts = pts + [pts[0]]
        for a, b in zip(pts, pts[1:]):
            self.d.line([a, b], fill=255, width=int(w))
        for p in pts:
            self._dot(p, w / 2)

    def arc(self, cx, cy, r, a0, a1, width=2.0, steps=None):
        """从 a0 度画到 a1 度（屏幕坐标，y 向下，角度增大是顺时针）。"""
        n = steps or max(8, int(abs(a1 - a0) / 3))
        pts = []
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        self.line(pts, width=width)

    def circle(self, cx, cy, r, width=2.0):
        self.arc(cx, cy, r, 0, 360, width=width, steps=96)

    def disc(self, cx, cy, r):
        p = self._p(cx, cy)
        self._dot(p, r * UNIT)

    def rrect(self, x, y, w, h, r, width=2.0):
        r = min(r, w / 2, h / 2)
        pts = []
        corners = [
            (x + w - r, y + r, -90),
            (x + w - r, y + h - r, 0),
            (x + r, y + h - r, 90),
            (x + r, y + r, 180),
        ]
        for cx, cy, start in corners:
            for i in range(0, 13):
                a = math.radians(start + 90 * i / 12)
                pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        self.line(pts, closed=True, width=width)

    def finish(self):
        small = self.img.resize((OUT, OUT), Image.LANCZOS)
        out = Image.new("RGBA", (OUT, OUT), (255, 255, 255, 0))
        out.putalpha(small)
        return out


# ---------------------------------------------------------------- 图标

def icon_settings(p):
    teeth = 8
    pts = []
    for i in range(teeth):
        base = 360 / teeth * i
        for ang, rad in ((-11, 7.6), (-7, 9.6), (7, 9.6), (11, 7.6)):
            a = math.radians(base + ang)
            pts.append((12 + rad * math.cos(a), 12 + rad * math.sin(a)))
    p.line(pts, closed=True)
    p.circle(12, 12, 3.2)


def icon_close(p):
    p.line([(6, 6), (18, 18)])
    p.line([(18, 6), (6, 18)])


def icon_check(p):
    p.line([(4.5, 12.5), (10, 18), (19.5, 7)], width=2.4)


def icon_chevron_right(p):
    p.line([(9, 5), (16, 12), (9, 19)])


def icon_chevron_down(p):
    p.line([(5, 9), (12, 16), (19, 9)])


def icon_reset(p):
    # 逆时针的“重置”：顺时针版本水平翻转
    p.mirror = True
    p.arc(12, 12, 9, 0, 317)
    p.line([(18.74, 5.74), (21, 8)])
    p.line([(21, 3), (21, 8), (16, 8)])


def icon_drag(p):
    p.line([(3, 12), (21, 12)])
    p.line([(7, 8), (3, 12), (7, 16)])
    p.line([(17, 8), (21, 12), (17, 16)])


def icon_numbers(p):
    p.line([(9.5, 4), (7.5, 20)])
    p.line([(16.5, 4), (14.5, 20)])
    p.line([(4.5, 9), (20, 9)])
    p.line([(4, 15), (19.5, 15)])


def icon_ruler(p):
    p.rrect(2, 7.5, 20, 9, 2)
    for x, h in ((6, 4), (10, 3), (14, 4), (18, 3)):
        p.line([(x, 7.5), (x, 7.5 + h)])


def icon_decimal(p):
    p.rrect(2.5, 5, 6.5, 11, 3.25)
    p.rrect(15, 5, 6.5, 11, 3.25)
    p.disc(12, 14.6, 1.25)


def icon_stairs(p):
    p.line([(3.5, 20), (3.5, 16), (9, 16), (9, 11), (14.5, 11), (14.5, 6), (20.5, 6)])


def icon_snap(p):
    p.rrect(3, 3, 18, 18, 2.5)
    p.line([(9, 3), (9, 21)])
    p.line([(15, 3), (15, 21)])
    p.line([(3, 9), (21, 9)])
    p.line([(3, 15), (21, 15)])


def icon_pivot(p):
    p.circle(12, 12, 7.5)
    p.line([(12, 2), (12, 5)])
    p.line([(12, 19), (12, 22)])
    p.line([(2, 12), (5, 12)])
    p.line([(19, 12), (22, 12)])
    p.disc(12, 12, 1.7)


def icon_shapes(p):
    p.line([(8, 3), (12.2, 10.5), (3.8, 10.5)], closed=True)
    p.rrect(13.5, 12.5, 8, 8, 2)
    p.circle(7, 17, 3.8)


def icon_camera(p):
    p.rrect(2, 7, 20, 14, 3)
    p.line([(8, 7), (9.6, 4), (14.4, 4), (16, 7)])
    p.circle(12, 14, 3.6)


def icon_film(p):
    p.rrect(3, 3.5, 18, 17, 2.5)
    p.line([(7.5, 3.5), (7.5, 20.5)])
    p.line([(16.5, 3.5), (16.5, 20.5)])
    for y in (8.5, 15.5):
        p.line([(3, y), (7.5, y)])
        p.line([(16.5, y), (21, y)])


def icon_play(p):
    p.line([(7, 4), (20, 12), (7, 20)], closed=True)


def icon_sync(p):
    for flip in (False, True):
        def tr(x, y):
            return (24 - x, 24 - y) if flip else (x, y)
        n = 60
        pts = []
        for i in range(n + 1):
            a = math.radians(180 + 137 * i / n)
            pts.append(tr(12 + 9 * math.cos(a), 12 + 9 * math.sin(a)))
        pts.append(tr(21, 8))
        p.line(pts)
        p.line([tr(21, 3), tr(21, 8), tr(16, 8)])


def icon_image(p):
    p.rrect(3, 3, 18, 18, 2.5)
    p.circle(9, 9, 1.8)
    p.line([(21, 15.5), (16, 10), (5, 21)])


def icon_warning(p):
    p.line([(12, 3.2), (22, 20.2), (2, 20.2)], closed=True)
    p.line([(12, 9.5), (12, 14)])
    p.disc(12, 17.2, 1.15)


def icon_save(p):
    p.rrect(3, 3, 18, 18, 2.5)
    p.line([(7.5, 3), (7.5, 8), (15.5, 8), (15.5, 3)])
    p.line([(7.5, 21), (7.5, 14), (16.5, 14), (16.5, 21)])


def icon_tune(p):
    for y, kx in ((6, 15), (12, 8.5), (18, 16.5)):
        p.line([(3.5, y), (kx - 3, y)])
        p.line([(kx + 3, y), (20.5, y)])
        p.circle(kx, y, 2.1)


def icon_cursor(p):
    p.line([(5, 3), (19.5, 11.2), (12.3, 13), (9.2, 20.2)], closed=True)


def icon_info(p):
    p.circle(12, 12, 9.2)
    p.line([(12, 11), (12, 16.5)])
    p.disc(12, 7.8, 1.15)


def icon_keyboard(p):
    p.rrect(2, 5.5, 20, 13, 2.5)
    for x in (6.5, 10.5, 14.5, 18.5):
        p.disc(x, 9.6, 0.9)
    p.line([(7, 14.2), (17, 14.2)])


def icon_bolt(p):
    p.line([(13, 2.5), (4.5, 13.5), (11.5, 13.5), (10.5, 21.5), (19.5, 10.2), (12.5, 10.2)], closed=True)


def icon_sparkle(p):
    p.line([(12, 3), (14, 10), (21, 12), (14, 14), (12, 21), (10, 14), (3, 12), (10, 10)], closed=True)


def icon_gauge(p):
    p.arc(12, 13, 9, 150, 390)
    p.line([(12, 13), (16.2, 8.8)])
    p.disc(12, 13, 1.4)


def icon_diamond(p):
    p.line([(12, 2.5), (21.5, 12), (12, 21.5), (2.5, 12)], closed=True)


def icon_layers(p):
    p.line([(12, 3), (21.5, 8), (12, 13), (2.5, 8)], closed=True)
    p.line([(2.5, 12.2), (12, 17.2), (21.5, 12.2)])
    p.line([(2.5, 16.4), (12, 21.4), (21.5, 16.4)])


def icon_plus(p):
    p.line([(12, 5), (12, 19)])
    p.line([(5, 12), (19, 12)])


def icon_minus(p):
    p.line([(5, 12), (19, 12)])


def icon_arrow_left(p):
    p.line([(20, 12), (4, 12)])
    p.line([(10, 6), (4, 12), (10, 18)])


def icon_arrow_right(p):
    p.line([(4, 12), (20, 12)])
    p.line([(14, 6), (20, 12), (14, 18)])


def icon_hand(p):
    p.line([(8, 13), (8, 5.5)])
    p.line([(12, 12), (12, 4)])
    p.line([(16, 12.5), (16, 5.5)])
    p.line([(20, 14), (20, 8.5)])
    p.line([(8, 13), (5.8, 10.8)])
    p.line([(20, 14), (20, 17), (16, 21), (11, 21), (8, 18), (5.8, 14.2)])
    p.line([(5.8, 10.8), (4.8, 12), (5.8, 14.2)])


def icon_users(p):
    p.circle(9, 8, 3.6)
    p.arc(9, 21, 7, 180, 360)
    p.arc(16.5, 8.5, 3, -100, 120)
    p.line([(18.5, 14.6), (21.5, 17), (22, 20.5)])


def icon_link(p):
    p.rrect(2.5, 8, 11, 8, 4)
    p.rrect(10.5, 8, 11, 8, 4)


def icon_logout(p):
    p.line([(10, 3.5), (4, 3.5), (4, 20.5), (10, 20.5)])
    p.line([(9.5, 12), (20.5, 12)])
    p.line([(16, 7.5), (20.5, 12), (16, 16.5)])


def icon_folder(p):
    p.line([(3, 19.5), (3, 5), (9, 5), (11, 7.5), (21, 7.5), (21, 19.5)], closed=True)


def icon_download(p):
    p.line([(12, 3), (12, 15)])
    p.line([(7, 10.5), (12, 15.5), (17, 10.5)])
    p.line([(4, 17), (4, 20.5), (20, 20.5), (20, 17)])


def icon_wifi(p):
    p.arc(12, 19, 15, 225, 315)
    p.arc(12, 19, 10, 228, 312)
    p.arc(12, 19, 5, 232, 308)
    p.disc(12, 19.2, 1.4)


def icon_history(p):
    # 逆时针箭头绕成的钟面 + 指针
    p.arc(12, 12, 9, 200, 520)
    p.line([(1.5, 6.5), (3.6, 9.6), (6.9, 8.0)])
    p.line([(12, 7), (12, 12), (15.5, 14)])


def icon_music(p):
    p.line([(9, 18), (9, 5), (20, 3), (20, 16)])
    p.circle(6.5, 18, 2.6)
    p.circle(17.5, 16, 2.6)


ICONS = {
    "settings": icon_settings, "close": icon_close, "check": icon_check,
    "chevron-right": icon_chevron_right, "chevron-down": icon_chevron_down,
    "reset": icon_reset, "drag": icon_drag, "numbers": icon_numbers,
    "ruler": icon_ruler, "decimal": icon_decimal, "stairs": icon_stairs,
    "snap": icon_snap, "pivot": icon_pivot, "shapes": icon_shapes,
    "camera": icon_camera, "film": icon_film, "play": icon_play,
    "sync": icon_sync, "image": icon_image, "warning": icon_warning,
    "save": icon_save, "tune": icon_tune, "cursor": icon_cursor,
    "info": icon_info, "keyboard": icon_keyboard, "bolt": icon_bolt,
    "sparkle": icon_sparkle, "gauge": icon_gauge, "diamond": icon_diamond,
    "layers": icon_layers, "arrow-left": icon_arrow_left, "arrow-right": icon_arrow_right, "plus": icon_plus, "minus": icon_minus, "hand": icon_hand,
    "users": icon_users, "link": icon_link, "logout": icon_logout, "folder": icon_folder,
    "download": icon_download, "wifi": icon_wifi, "history": icon_history, "music": icon_music,
}

META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_meta(png_path):
    meta_path = png_path + ".meta"
    if os.path.exists(meta_path):
        return
    guid = hashlib.md5(os.path.relpath(png_path, UI_DIR).replace("\\", "/").encode("utf-8")).hexdigest()
    with open(meta_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(META_TEMPLATE.format(guid=guid))


def main():
    os.makedirs(ICON_DIR, exist_ok=True)
    os.makedirs(STYLE_DIR, exist_ok=True)

    names = sorted(ICONS)
    for name in names:
        pen = Pen()
        ICONS[name](pen)
        path = os.path.join(ICON_DIR, name + ".png")
        pen.finish().save(path)
        write_meta(path)

    lines = [
        "/* 图标：由 Tools/GenerateIcons.py 生成，不要手改。白色图，用 -unity-background-image-tint-color 着色。 */",
        "",
    ]
    for name in names:
        lines.append(
            '.ic-%s { background-image: url("project://database/Assets/EditorTweaksUI/Icons/%s.png"); }' % (name, name))
    with open(os.path.join(STYLE_DIR, "Icons.uss"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")

    # 预览总览图（不进 Unity，放在 Tools/ 里方便检查）
    cols = 8
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * 140, rows * 140), (11, 13, 20, 255))
    for i, name in enumerate(names):
        icon = Image.open(os.path.join(ICON_DIR, name + ".png"))
        sheet.alpha_composite(icon, ((i % cols) * 140 + 6, (i // cols) * 140 + 6))
    sheet.save(os.path.join(ROOT, "icons-preview.png"))
    print("generated", len(names), "icons")


if __name__ == "__main__":
    sys.exit(main())

# 公共 API 文档

此目录只存放 ADOFAI Editor Tweaks 对其他 Mod 开放的稳定接口文档。内部实现、维护说明和玩家操作手册不放在这里。

## API 列表

- [ChartRendering.md](ChartRendering.md)：谱面渲染任务 API v1，包括请求、播放模式、范围、进度、完成、取消、错误码和完整示例。

## 兼容约定

- 调用方直接引用发布包中的 `ADOFAI.EditorTweaks.dll`。
- 当前公共命名空间为 `ADOFAI.EditorTweaks.Api.Rendering`。
- 每个 API 都提供独立主版本号；同一主版本内只增加成员，不修改已有签名、枚举数值或语义。
- 未在本目录记录的 `internal` 类型、Unity 对象、Harmony 补丁和编码组件都不属于公共 API。
- 调用方应把 `ADOFAI.EditorTweaks` 声明为运行依赖，并在调用前检查对应 API 的可用状态。

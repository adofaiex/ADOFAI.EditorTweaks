# Web 设置页

Web 设置页是 Mod 的唯一完整设置界面。它运行在独立的 React 页面中，游戏内不再绘制设置浮窗，也不再绘制第二个渲染进度窗口。

## 页面启动

启用 Mod 后，`WebUiHost` 创建一个常驻 `MonoBehaviour`，在本机启动 `HttpListener`：

- 只绑定 `http://127.0.0.1:<port>/`，不接受局域网地址。
- 在固定的本地端口范围内选择可用端口。
- 每次启动生成新的随机令牌。
- 不自动打开浏览器，只有 UMM 中配置的快捷键会触发打开。
- 直接交给系统默认浏览器打开，Edge、Chrome、Firefox 等浏览器都可以访问。
- 日志会记录实际访问地址，方便排查浏览器没有自动打开的情况。

UMM 面板只显示一行快捷键录入。默认值是 `Ctrl+Shift+E`，录入后统一保存为 `Ctrl+Shift+E` 这类规范字符串。旧的浮窗位置、折叠状态和显示开关字段仍保留在 `Settings` 中，用于读取旧配置，但不会再影响运行逻辑。

## HTTP 接口

页面和 Mod 使用同源 HTTP 通信。除了静态文件以外，每个接口都必须提供令牌：页面通过 URL 查询参数携带令牌，也可以使用 `X-EditorTweaks-Token` 请求头。

| 方法 | 路径 | 作用 |
| --- | --- | --- |
| GET | `/api/events?token=...` | 建立 SSE 连接，接收状态和渲染变化 |
| POST | `/api/settings` | 修改白名单中的设置字段 |
| POST | `/api/settings/reset` | 恢复全部默认设置 |
| POST | `/api/render/start` | 使用当前设置创建渲染任务 |
| POST | `/api/render/cancel` | 取消当前渲染任务 |
| POST | `/api/cloud/upload` | 上传当前设置到 Steam 云 |
| POST | `/api/cloud/download` | 从 Steam 云下载设置 |
| POST | `/api/open-manual` | 打开用户手册 |
| POST | `/api/open-ffmpeg-help` | 打开 FFmpeg 参数参考 |

`HttpListener` 的后台线程只负责读取请求和写回响应。修改设置、启动渲染、取消渲染和打开本地文件都会进入 Unity 主线程队列，并设置超时，避免 HTTP 线程直接触碰 Unity 对象。

静态文件路径会先解码，再规范化为 `Resources/WebUI` 下的绝对路径。请求包含路径穿越、未知 API、无效令牌或不存在文件时会被拒绝。

## SSE 状态同步

SSE 是页面唯一的状态同步通道。客户端连接后立即收到一次完整状态快照。之后 Mod 在设置保存、渲染状态改变或渲染进度更新时广播 `state` 事件。连接断开后浏览器会重新连接，重新连接的第一条消息仍然是完整快照，所以不依赖客户端保留旧事件。

渲染任务现有的进度事件约每 0.1 秒更新一次，Web 页面沿用这个频率。快照会保留：

- 阶段、详情和百分比。
- 已写入帧数、总帧数、处理速度。
- 重复帧数量和比例。
- 预计剩余时间。
- 编码器名称、内存预算、队列预算。
- 捕获来源、输出文件路径和最终成功、失败或取消消息。

## 渲染期间的输入保护

`RenderInputGuardPatches` 只以 `ChartRenderService.IsActive` 作为条件：

- 阻止编辑器更新和编辑器缩放输入。
- 阻止玩家按下、松开、暂停和有效按键计数。
- 阻止 Unity `StandaloneInputModule.Process` 处理背景 UI 输入。
- 保留 `scrController.Update`，让离线视觉时钟和渲染任务继续推进。

页面取消按钮和 `Escape` 都调用 `ChartRenderTask.Cancel()`。禁用 Mod 时，Web 服务先停止并取消当前任务，然后销毁渲染服务和 Harmony Patch。

## 前端构建

`webui/` 是 React + Vite + TypeScript 工程，组件和图标使用 Arco Design。依赖通过 `package-lock.json` 锁定，运行时只加载构建后的本地静态文件，不使用 CDN。

`ADOFAIMod.targets` 在 .NET 构建前执行：

1. 检查 `node` 和 `npm` 是否可用。
2. 在没有 `node_modules` 时执行 `npm ci`。
3. 执行 `npm run build`。
4. 只把 `webui/dist` 复制到 `out/Resources/WebUI`。

最终包不包含 `webui` 源码、`node_modules` 或开发服务器文件。

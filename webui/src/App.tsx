import {
  Button,
  Message,
  Spin,
  Switch,
} from "@arco-design/web-react";
import {
  IconApps,
  IconCheckCircle,
  IconCheck,
  IconCloud,
  IconCode,
  IconDesktop,
  IconDownload,
  IconFile,
  IconImage,
  IconInfoCircle,
  IconPlayArrow,
  IconRefresh,
  IconSettings,
  IconTool,
  IconUndo,
  IconUpload,
  IconCloseCircle,
  IconDown,
} from "@arco-design/web-react/icon";
import React from "react";
import { createPortal } from "react-dom";
import { createEventSource, createMockState, isLocalPreview, postJson } from "./api";
import type { PageKey, PatchStatus, RenderState, SettingsState, WebUiState } from "./types";

const pageItems: Array<{ key: PageKey; label: string; description: string; icon: React.ReactNode }> = [
  { key: "overview", label: "总览", description: "兼容性与全部功能状态", icon: <IconApps /> },
  { key: "fixes", label: "编辑器修复", description: "编辑器行为修复与偏好保存", icon: <IconTool /> },
  { key: "numeric", label: "数值拖动", description: "数字输入和步进精度", icon: <IconCode /> },
  { key: "decoration", label: "装饰移动", description: "装饰拖动与吸附", icon: <IconApps /> },
  { key: "render", label: "谱面渲染", description: "视频输出与实时进度", icon: <IconImage /> },
  { key: "cloud", label: "云同步", description: "Steam 云端设置", icon: <IconCloud /> },
  { key: "tools", label: "工具", description: "帮助、日志与诊断", icon: <IconSettings /> },
];

const patchDescriptions: Record<string, string> = {
  编辑器修复: "修复编辑器常见问题与崩溃路径",
  数值拖动: "增强数字输入体验与精度",
  装饰移动: "优化装饰物选择与移动逻辑",
  谱面渲染: "将谱面画面和声音输出为视频",
  云同步: "云端备份与多端同步支持",
  工具: "额外实用工具集合",
};

const statusLabel: Record<string, string> = {
  active: "已启用",
  failed: "不可用",
  blocked: "已阻止",
  inactive: "未启用",
};

const settingLabels: Record<string, Record<string, string>> = {
  LegacyZipEncoding: {
    Auto: "自动检测",
    CP949: "CP949",
    GB18030: "GB18030",
    ShiftJIS: "Shift-JIS",
    CP437: "CP437",
  },
  ChartRenderEncoderMode: {
    "auto-balanced": "自动均衡",
    fastest: "最快",
    balanced: "均衡",
    quality: "质量",
    "cpu-compatibility": "CPU 兼容",
    custom: "自定义",
    AutoBalanced: "自动均衡",
    Fastest: "最快",
    Balanced: "均衡",
    Quality: "质量",
    CpuCompatibility: "CPU 兼容",
    Custom: "自定义",
  },
  ChartRenderCaptureFormat: { rgba: "RGBA", bgra: "BGRA", RGBA: "RGBA", BGRA: "BGRA" },
  ChartRenderCaptureSource: {
    camera: "摄像机渲染（推荐）",
    "game-view": "游戏画面渲染（兼容模式）",
    Camera: "摄像机渲染（推荐）",
    GameView: "游戏画面渲染（兼容模式）",
  },
  ChartRenderPreviewMode: { full: "完整", dim: "暗色", minimal: "极简", Full: "完整", Dim: "暗色", Minimal: "极简" },
  ChartRenderAudioFormat: { aac: "AAC", flac: "FLAC（无损）", alac: "ALAC（无损）", AAC: "AAC", FLAC: "FLAC（无损）", ALAC: "ALAC（无损）" },
  ChartRenderVideoFormat: {
    mp4: "MP4（H.264/AAC）",
    mkv: "MKV（Matroska）",
    mov: "MOV（QuickTime）",
    MP4: "MP4（H.264/AAC）",
    MKV: "MKV（Matroska）",
    MOV: "MOV（QuickTime）",
  },
};

const labelFor = (group: string, value: string) => settingLabels[group]?.[value] ?? value;

function App() {
  const [state, setState] = React.useState<WebUiState>(() => createMockState());
  const [page, setPage] = React.useState<PageKey>("overview");
  const [loading, setLoading] = React.useState(true);
  const [connected, setConnected] = React.useState(false);
  const [reconnecting, setReconnecting] = React.useState(false);
  const [connectionReload, setConnectionReload] = React.useState(0);
  const localPreview = isLocalPreview();
  const [demoMode, setDemoMode] = React.useState(localPreview);
  const renderActionInFlight = React.useRef(false);

  React.useEffect(() => {
    let disposed = false;
    let events: EventSource | undefined;
    let retryTimer: number | undefined;

    const scheduleReconnect = () => {
      if (disposed || localPreview || retryTimer !== undefined) return;
      setConnected(false);
      setReconnecting(true);
      retryTimer = window.setTimeout(() => {
        retryTimer = undefined;
        void connect();
      }, 2000);
    };

    const connect = () => {
      if (disposed || localPreview) return;
      events?.close();
      events = undefined;
      const source = createEventSource((nextState) => {
        if (disposed) return;
        setState(nextState);
        setLoading(false);
        setConnected(true);
        setReconnecting(false);
        setDemoMode(false);
      });
      events = source;
      source.onopen = () => {
        if (!disposed) {
          setLoading(false);
          setConnected(true);
          setReconnecting(false);
        }
      };
      source.onerror = () => {
        if (disposed) return;
        setLoading(false);
        setConnected(false);
        source.close();
        if (events === source) events = undefined;
        scheduleReconnect();
      };
    };

    if (localPreview) {
      setLoading(false);
      setConnected(false);
      setReconnecting(false);
      setDemoMode(true);
    } else {
      setDemoMode(false);
      connect();
    }

    return () => {
      disposed = true;
      events?.close();
      if (retryTimer !== undefined) window.clearTimeout(retryTimer);
    };
  }, [connectionReload, localPreview]);

  React.useEffect(() => {
    const preventBrowserSave = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        event.stopPropagation();
        Message.info(localPreview ? "本地预览不需要保存网页。" : "设置会自动保存，不需要保存网页。");
      }
    };

    window.addEventListener("keydown", preventBrowserSave);
    return () => window.removeEventListener("keydown", preventBrowserSave);
  }, [localPreview]);

  const updateSettings = async (changes: Partial<SettingsState>) => {
    setState((current) => ({ ...current, settings: { ...current.settings, ...changes } }));
    if (demoMode) {
      Message.success("演示状态：设置已更新");
      return;
    }

    try {
      const response = await postJson<{ state: WebUiState }>("/api/settings", { changes });
      if (response.state) setState(response.state);
      Message.success("设置已保存");
    } catch (error) {
      Message.error(error instanceof Error ? error.message : "设置保存失败");
    }
  };

  const renderAction = async (action: "start" | "cancel") => {
    if (renderActionInFlight.current || (action === "cancel" && state.render.cancelRequested)) return;
    if (!demoMode && !connected) {
      Message.error("Mod 服务未连接，正在尝试重连。请稍后再试。");
      return;
    }

    renderActionInFlight.current = true;
    try {
      if (demoMode) {
        setState((current) => ({
          ...current,
          render: action === "start"
            ? {
                ...current.render,
                active: true,
                state: "Rendering",
                cancelRequested: false,
                message: "演示渲染任务正在运行",
                progress: {
                  ...current.render.progress,
                  value: 0.62,
                  writtenFrames: 3721,
                  totalFrames: 6000,
                  duplicateFrames: 148,
                  duplicateRatio: 0.0398,
                  processingFramesPerSecond: 58.4,
                  estimatedRemaining: "00:00:21",
                  stage: "正在编码",
                  detail: "正在写入视频帧并合并音频。",
                  encoderName: "libx264 / veryfast",
                  memoryBudget: "1.2 GB / 2.0 GB",
                  queueBudget: "3 / 8"
                }
              }
            : {
                ...current.render,
                active: false,
                cancelRequested: false,
                state: "Canceled",
                message: "演示渲染已取消"
              }
        }));
      } else {
        const response = await postJson<{ state: WebUiState }>(`/api/render/${action}`, {
          taskId: state.render.taskId,
        });
        if (response.state) setState(response.state);
      }
      setPage("render");
    } catch (error) {
      Message.error(error instanceof Error ? error.message : "渲染操作失败");
    } finally {
      renderActionInFlight.current = false;
    }
  };

  const runAction = async (path: string) => {
    if (demoMode) {
      Message.success("演示状态：操作已完成");
      return;
    }

    try {
      const response = await postJson<{ state: WebUiState }>(path);
      if (response.state) setState(response.state);
      Message.success("操作已完成");
    } catch (error) {
      Message.error(error instanceof Error ? error.message : "操作失败");
    }
  };

  const activePage = pageItems.find((item) => item.key === page) ?? pageItems[0];
  const visibleRender = page === "render";
  const connectionLabel = localPreview
    ? "本地预览"
    : connected
      ? "本机服务已连接"
      : reconnecting
        ? "连接断开，正在重连…"
        : "正在连接 Mod 服务…";

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-lockup">
          <span className="brand-mark"><IconApps /></span>
          <span className="brand-name">EDITOR <strong>TWEAKS</strong></span>
          <span className="brand-divider">/</span>
          <span className="brand-page">设置</span>
        </div>
        <div className="topbar-actions">
          <span className={`connection-dot ${connected ? "is-online" : ""}`} />
          <span className="connection-label">{connectionLabel}</span>
          {!localPreview && !connected ? <button className="connection-retry" type="button" onClick={() => setConnectionReload((current) => current + 1)} title="重新连接 Mod 服务"><IconRefresh /> 重连</button> : null}
        </div>
      </header>

      <div className="workspace">
        <aside className="sidebar">
          <div className="sidebar-title">功能导航</div>
          <nav>
            {pageItems.map((item) => (
              <button
                className={`nav-item ${page === item.key ? "is-active" : ""}`}
                key={item.key}
                onClick={() => setPage(item.key)}
              >
                <span className="nav-icon">{item.icon}</span>
                <span className="nav-copy">
                  <span className="nav-label">{item.label}</span>
                  <span className="nav-description">{item.description}</span>
                </span>
              </button>
            ))}
          </nav>
          <div className="sidebar-footnote">Ctrl+Shift+E 打开本页面</div>
        </aside>

        <main className="main-content">
          {loading ? <div className="loading-state"><Spin size={32} /><span>正在连接 Mod 服务…</span></div> : null}
          {!loading && visibleRender ? (
            <RenderPage state={state} onSettings={updateSettings} onAction={runAction} onStart={() => renderAction("start")} onCancel={() => renderAction("cancel")} />
          ) : null}
          {!loading && !visibleRender && page === "overview" ? (
            <Overview state={state} onNavigate={setPage} onSettings={updateSettings} onAction={runAction} />
          ) : null}
          {!loading && !visibleRender && page === "fixes" ? <FixesPage state={state} onSettings={updateSettings} /> : null}
          {!loading && !visibleRender && page === "numeric" ? <NumericPage state={state} onSettings={updateSettings} /> : null}
          {!loading && !visibleRender && page === "decoration" ? <DecorationPage state={state} onSettings={updateSettings} /> : null}
          {!loading && !visibleRender && page === "cloud" ? <CloudPage state={state} onAction={runAction} /> : null}
          {!loading && !visibleRender && page === "tools" ? <ToolsPage onAction={runAction} /> : null}
        </main>
      </div>

      <footer className="statusbar">
        <span className="status-running"><span className="status-led" />运行中</span>
        <span className="status-divider" />
        <button onClick={() => setPage("render")} className="status-link"><IconFile /> 日志</button>
        <span className="status-divider" />
        <span className="status-version">EDITOR TWEAKS {state.compatibility.modVersion}</span>
      </footer>
    </div>
  );
}

function Overview({ state, onNavigate, onSettings, onAction }: { state: WebUiState; onNavigate: (page: PageKey) => void; onSettings: (changes: Partial<SettingsState>) => void; onAction: (path: string) => void }) {
  const allAvailable = state.patchSummary.active === state.patchSummary.total && !state.patchSummary.registrationError;
  return (
    <>
      <PageHeading title="兼容性概览" description="查看当前游戏版本和所有功能组状态。" />
      <section className="compatibility-panel angular-panel">
        <div className="compatibility-item"><IconDesktop /><span><small>游戏版本</small><strong>{state.compatibility.gameVersion}</strong></span></div>
        <div className="compatibility-item"><IconCode /><span><small>编辑器版本</small><strong>{state.compatibility.editorVersion}</strong></span></div>
        <div className="compatibility-item"><IconApps /><span><small>Editor Tweaks 版本</small><strong>{state.compatibility.modVersion}</strong></span></div>
        <div className="compatibility-item"><IconCheckCircle /><span><small>兼容状态</small><strong className={allAvailable ? "text-accent" : "text-warning"}>{allAvailable ? "完全兼容" : "部分可用"}</strong></span></div>
      </section>

      <section className="section-block">
        <SectionHeading title="补丁状态" hint={`${state.patchSummary.active}/${state.patchSummary.total} 个功能组可用`} />
        <div className="patch-list">
          {state.patches.map((patch) => <PatchRow key={patch.id} patch={patch} />)}
        </div>
      </section>

      <section className="section-block angular-panel global-settings">
        <SectionHeading title="全局设置" hint="只影响 Web 设置页和通用编辑行为" />
        <div className="settings-grid">
          <SettingSelect label="旧版 ZIP 文件名编码" group="LegacyZipEncoding" value={state.settings.LegacyZipEncoding} options={["Auto", "CP949", "GB18030", "ShiftJIS", "CP437"]} onChange={(value) => onSettings({ LegacyZipEncoding: value })} />
          <SettingNumber label="装饰移动吸附精度" value={state.settings.DecorationMoveSnapStep} min={0} step={0.1} onChange={(value) => onSettings({ DecorationMoveSnapStep: value })} suffix="px" />
          <SettingNumber label="小数每像素步进" value={state.settings.FloatStepPerPixel} min={0.0001} step={0.01} onChange={(value) => onSettings({ FloatStepPerPixel: value })} suffix="px" />
          <SettingSwitch label="编辑器偏好自动保存" value={state.settings.PersistEditorPreferences} onChange={(value) => onSettings({ PersistEditorPreferences: value })} />
        </div>
      </section>
      <div className="page-actions"><Button className="secondary-button" icon={<IconUndo />} onClick={() => onAction("/api/settings/reset")}>恢复默认</Button><Button className="primary-button" onClick={() => onNavigate("render")}>打开谱面渲染</Button></div>
    </>
  );
}

function FixesPage({ state, onSettings }: { state: WebUiState; onSettings: (changes: Partial<SettingsState>) => void }) {
  return <FeaturePage title="编辑器修复" description="控制编辑器输入、偏好保存和视频背景同步行为。">
    <SettingSwitch label="修复镜头相对装饰拖动" description="修正屏幕空间和世界空间的拖动换算。" value={state.settings.EnableCameraRelativeDecorationDragFix} onChange={(value) => onSettings({ EnableCameraRelativeDecorationDragFix: value })} />
    <SettingSwitch label="修复镜头/视差装饰轴心显示" description="让单选装饰的轴心十字跟随实际位置。" value={state.settings.EnableDecorationPivotFix} onChange={(value) => onSettings({ EnableDecorationPivotFix: value })} />
    <SettingSwitch label="修复中途播放时视频背景延迟" description="降低视频背景和谱面播放时钟的漂移。" value={state.settings.EnableVideoBackgroundSyncFix} onChange={(value) => onSettings({ EnableVideoBackgroundSyncFix: value })} />
    <SettingSwitch label="持久化官方编辑器偏好设置" description="官方偏好变化后立即写回持久化数据。" value={state.settings.PersistEditorPreferences} onChange={(value) => onSettings({ PersistEditorPreferences: value })} />
  </FeaturePage>;
}

function NumericPage({ state, onSettings }: { state: WebUiState; onSettings: (changes: Partial<SettingsState>) => void }) {
  return <FeaturePage title="数值拖动" description="增强数字输入框的右键拖动体验和精度控制。">
    <SettingSwitch label="启用数值输入框拖动调节" description="支持 Int、Float、Tile 和 Vector2 输入框。" value={state.settings.EnableNumericDrag} onChange={(value) => onSettings({ EnableNumericDrag: value })} />
    <SettingNumber label="小数每像素步进" value={state.settings.FloatStepPerPixel} min={0.0001} step={0.01} onChange={(value) => onSettings({ FloatStepPerPixel: value })} suffix="px" />
    <SettingNumber label="整数每像素步进" value={state.settings.IntStepPerPixel} min={0.0001} step={1} onChange={(value) => onSettings({ IntStepPerPixel: value })} suffix="px" />
    <SettingNumber label="小数最大位数" value={state.settings.MaxFloatingPoints} min={0} max={8} step={1} onChange={(value) => onSettings({ MaxFloatingPoints: value })} suffix="位" />
  </FeaturePage>;
}

function DecorationPage({ state, onSettings }: { state: WebUiState; onSettings: (changes: Partial<SettingsState>) => void }) {
  return <FeaturePage title="装饰移动" description="调整装饰物移动时的吸附和编辑辅助行为。">
    <SettingNumber label="装饰移动吸附精度" value={state.settings.DecorationMoveSnapStep} min={0} step={0.1} onChange={(value) => onSettings({ DecorationMoveSnapStep: value })} suffix="px" />
    <InfoBlock text="0 = 关闭。Camera 和 CameraAspect 装饰会保留修复后的拖动坐标。" />
  </FeaturePage>;
}

function RenderPage({ state, onSettings, onAction, onStart, onCancel }: { state: WebUiState; onSettings: (changes: Partial<SettingsState>) => void; onAction: (path: string) => void; onStart: () => void; onCancel: () => void }) {
  const progress = state.render.progress;
  const percent = Math.round(Math.max(0, Math.min(1, progress.value)) * 1000) / 10;
  const [advancedOpen, setAdvancedOpen] = React.useState(false);
  const [professionalOpen, setProfessionalOpen] = React.useState(false);
  const isGameView = state.settings.ChartRenderCaptureSource.toLowerCase() === "game-view";
  const isCustomEncoder = state.settings.ChartRenderEncoderMode.toLowerCase() === "custom";
  return <>
    <PageHeading title="谱面渲染" description="实时查看渲染阶段、编码状态和队列压力。" />
    <section className="render-panel angular-panel">
      <div className="render-head"><div><span className="eyebrow">{state.render.active ? "正在处理" : "渲染状态"}</span><h2>{progress.stage || "等待渲染"}</h2><p>{progress.detail || "从页面开始渲染后，全部进度会在这里实时更新。"}</p></div><div className="render-percent">{percent}<small>%</small></div></div>
      <div className="progress-track"><div className="progress-fill" style={{ width: `${percent}%` }} /></div>
      <div className="render-metrics">
        <Metric icon={<IconImage />} label="写入帧" value={`${progress.writtenFrames.toLocaleString()} / ${progress.totalFrames.toLocaleString()}`} />
        <Metric icon={<IconPlayArrow />} label="处理速度" value={`${progress.processingFramesPerSecond.toFixed(1)} 帧/秒`} />
        <Metric icon={<IconRefresh />} label="预计剩余" value={progress.estimatedRemaining || "00:00:00"} />
        <Metric icon={<IconInfoCircle />} label="重复帧" value={`${progress.duplicateFrames.toLocaleString()} (${(progress.duplicateRatio * 100).toFixed(2)}%)`} />
      </div>
      <div className="render-details">
        <DetailLine label="模式" value={`离线定帧 / ${state.render.captureSource.toLowerCase().includes("game") ? "游戏画面" : "摄像机"}`} />
        <DetailLine label="编码器" value={progress.encoderName || "等待编码器"} />
        <DetailLine label="内存预算" value={progress.memoryBudget || "—"} />
        <DetailLine label="队列预算" value={progress.queueBudget || "—"} />
        <DetailLine label="输出路径" value={state.render.outputPath || "—"} />
      </div>
      {state.render.active ? <Button className="danger-button" disabled={state.render.cancelRequested} icon={<IconCloseCircle />} onClick={onCancel}>{state.render.cancelRequested ? "正在取消…" : "取消渲染"}</Button> : <div className="render-action-row"><div className="render-idle"><IconCheckCircle /> {state.render.message || "当前没有正在运行的渲染任务"}</div><Button className="primary-button" icon={<IconPlayArrow />} onClick={onStart}>开始渲染</Button></div>}
    </section>
    <section className="render-settings angular-panel">
      <SectionHeading title="渲染设置" hint="修改后保存，下一次渲染使用" />
      <div className="settings-note">推荐默认：1080p、60 帧，并在可用时优先使用 GPU 硬编码。</div>
      <div className="settings-grid">
        <SettingText label="导出目录" value={state.settings.ChartRenderExportDirectory} onChange={(value) => onSettings({ ChartRenderExportDirectory: value })} />
        <SettingSelect label="画面捕获方式" group="ChartRenderCaptureSource" value={state.settings.ChartRenderCaptureSource} options={["camera", "game-view"]} onChange={(value) => onSettings({ ChartRenderCaptureSource: value })} />
        {isGameView ? <InfoBlock text="游戏画面模式会直接使用当前游戏窗口分辨率输出；渲染结束前请保持窗口尺寸不变。" /> : <>
          <PresetSetting label="分辨率预设" options={["1080p", "2K", "4K", "9:16", "21:9"]} active={getResolutionPreset(state.settings.ChartRenderWidth, state.settings.ChartRenderHeight)} onSelect={(preset) => onSettings(resolutionPresets[preset])} />
          <SettingNumber label="视频宽度" value={state.settings.ChartRenderWidth} min={16} max={7680} step={2} onChange={(value) => onSettings({ ChartRenderWidth: value })} suffix="px" />
          <SettingNumber label="视频高度" value={state.settings.ChartRenderHeight} min={16} max={4320} step={2} onChange={(value) => onSettings({ ChartRenderHeight: value })} suffix="px" />
        </>}
        <PresetSetting label="帧率预设" options={["30", "60", "120"]} active={String(state.settings.ChartRenderFps)} onSelect={(preset) => onSettings({ ChartRenderFps: Number(preset) })} />
        <SettingNumber label="帧率" value={state.settings.ChartRenderFps} min={1} max={240} step={1} onChange={(value) => onSettings({ ChartRenderFps: value })} suffix="帧/秒" />
        <SettingNumber label="结束后延迟停止秒数" value={state.settings.ChartRenderCompletionTailSeconds} min={0} step={0.1} onChange={(value) => onSettings({ ChartRenderCompletionTailSeconds: value })} suffix="" />
        <SettingSelect label="视频输出格式" group="ChartRenderVideoFormat" value={state.settings.ChartRenderVideoFormat} options={["mp4", "mkv", "mov"]} onChange={(value) => onSettings({ ChartRenderVideoFormat: value })} />
        <SettingSwitch label="渲染时显示判定文字" description="控制成品视频中是否显示 Perfect、Early、Late 等判定文字。" value={state.settings.ChartRenderShowHitJudgments} onChange={(value) => onSettings({ ChartRenderShowHitJudgments: value })} />
        <SettingSwitch label="仅渲染选中段落" description="开启后需要在编辑器中框选至少两个连续砖块。" value={state.settings.ChartRenderUseSelectedRange} onChange={(value) => onSettings({ ChartRenderUseSelectedRange: value })} />
      </div>
      <DisclosureButton label={advancedOpen ? "隐藏高级设置" : "显示高级设置"} open={advancedOpen} onClick={() => setAdvancedOpen((open) => !open)} />
      {advancedOpen ? <div className="settings-section">
        <div className="settings-note warning-note">高级设置只适合排查问题或有特殊导出需求时使用。错误的编码参数可能导致渲染失败或输出无法播放。</div>
        <div className="settings-grid">
          <SettingText label="工作区目录" value={state.settings.ChartRenderWorkspaceDirectory} onChange={(value) => onSettings({ ChartRenderWorkspaceDirectory: value })} />
          <SettingSelect label="编码档位" group="ChartRenderEncoderMode" value={state.settings.ChartRenderEncoderMode} options={["auto-balanced", "fastest", "balanced", "quality", "cpu-compatibility", "custom"]} onChange={(value) => onSettings({ ChartRenderEncoderMode: value })} />
          <SettingNumber label="画质参数" value={state.settings.ChartRenderCrf} min={0} max={51} step={1} onChange={(value) => onSettings({ ChartRenderCrf: value })} suffix="" />
          <SettingNumber label="视频码率（Mbps）" value={state.settings.ChartRenderBitrateMbps} min={0} max={300} step={1} onChange={(value) => onSettings({ ChartRenderBitrateMbps: value })} suffix="" />
          {isCustomEncoder ? <SettingText label="编码方式" value={state.settings.ChartRenderPreset} onChange={(value) => onSettings({ ChartRenderPreset: value })} /> : null}
          <SettingSelect label="回读格式" group="ChartRenderCaptureFormat" value={state.settings.ChartRenderCaptureFormat} options={["rgba", "bgra"]} onChange={(value) => onSettings({ ChartRenderCaptureFormat: value })} />
          <SettingSelect label="音频格式" group="ChartRenderAudioFormat" value={state.settings.ChartRenderAudioFormat} options={["aac", "flac", "alac"]} onChange={(value) => onSettings({ ChartRenderAudioFormat: value })} />
          <SettingSelect label="渲染预览" group="ChartRenderPreviewMode" value={state.settings.ChartRenderPreviewMode} options={["full", "dim", "minimal"]} onChange={(value) => onSettings({ ChartRenderPreviewMode: value })} />
          <SettingNumber label="音频同步偏移（毫秒）" value={state.settings.ChartRenderAudioSyncOffsetMs} min={-5000} max={5000} step={1} onChange={(value) => onSettings({ ChartRenderAudioSyncOffsetMs: value })} suffix="" />
        </div>
        <DisclosureButton label={professionalOpen ? "隐藏专业 FFmpeg 设置" : "显示专业 FFmpeg 设置"} open={professionalOpen} onClick={() => setProfessionalOpen((open) => !open)} />
        {professionalOpen ? <div className="professional-settings"><div className="settings-note">输入完整的自定义 FFmpeg 合成参数字符串。留空则使用自动生成参数。</div><SettingText label="自定义 FFmpeg 合成参数" value={state.settings.ChartRenderCustomMuxArgs} onChange={(value) => onSettings({ ChartRenderCustomMuxArgs: value })} /><Button className="secondary-button" onClick={() => onAction("/api/open-ffmpeg-help")}>打开 FFmpeg 参数参考帮助</Button></div> : null}
      </div> : null}
    </section>
  </>;
}

const resolutionPresets: Record<string, Partial<SettingsState>> = {
  "1080p": { ChartRenderWidth: 1920, ChartRenderHeight: 1080 },
  "2K": { ChartRenderWidth: 2560, ChartRenderHeight: 1440 },
  "4K": { ChartRenderWidth: 3840, ChartRenderHeight: 2160 },
  "9:16": { ChartRenderWidth: 1080, ChartRenderHeight: 1920 },
  "21:9": { ChartRenderWidth: 2560, ChartRenderHeight: 1080 },
};

function getResolutionPreset(width: number, height: number): string {
  const entry = Object.entries(resolutionPresets).find(([, value]) => value.ChartRenderWidth === width && value.ChartRenderHeight === height);
  return entry?.[0] ?? "";
}

function PresetSetting({ label, options, active, onSelect }: { label: string; options: string[]; active: string; onSelect: (value: string) => void }) {
  return <div className="setting-row preset-setting"><div><strong>{label}</strong></div><div className="preset-options">{options.map((option) => <button className={`preset-button ${active === option ? "is-active" : ""}`} key={option} type="button" onClick={() => onSelect(option)}>{option}</button>)}</div></div>;
}

function DisclosureButton({ label, open, onClick }: { label: string; open: boolean; onClick: () => void }) {
  return <button className={`disclosure-button ${open ? "is-open" : ""}`} type="button" onClick={onClick}><span>{label}</span><IconDown /></button>;
}

function CloudPage({ state, onAction }: { state: WebUiState; onAction: (path: string) => void }) {
  return <FeaturePage title="云同步" description="显式上传或下载设置，不会在启动和退出时自动覆盖本地配置.">
    <div className="cloud-status"><IconCloud /><div><strong>{state.cloud.available ? "Steam 云同步可用" : "Steam 云同步不可用"}</strong><span>{state.cloud.hasFile ? "检测到云端设置文件" : "尚未检测到云端设置文件"}</span></div></div>
    <div className="button-row"><Button className="secondary-button" icon={<IconDownload />} disabled={!state.cloud.available} onClick={() => onAction("/api/cloud/download")}>从云端下载</Button><Button className="primary-button" icon={<IconUpload />} disabled={!state.cloud.available} onClick={() => onAction("/api/cloud/upload")}>上传到云端</Button></div>
  </FeaturePage>;
}

function ToolsPage({ onAction }: { onAction: (path: string) => void }) {
  return <FeaturePage title="工具" description="帮助、诊断和设置维护入口.">
    <div className="tool-row"><IconFile /><div><strong>打开使用手册</strong><span>查看 Mod 功能、快捷键和常见问题。</span></div><Button className="secondary-button" onClick={() => onAction("/api/open-manual")}>打开</Button></div>
    <div className="tool-row"><IconImage /><div><strong>打开 FFmpeg 参数参考</strong><span>查看视频、音频和合成参数说明。</span></div><Button className="secondary-button" onClick={() => onAction("/api/open-ffmpeg-help")}>打开</Button></div>
    <div className="tool-row"><IconUndo /><div><strong>恢复全部默认设置</strong><span>清除自定义配置并恢复 Mod 默认值。</span></div><Button className="danger-outline" onClick={() => onAction("/api/settings/reset")}>恢复默认</Button></div>
  </FeaturePage>;
}

function PatchRow({ patch }: { patch: PatchStatus }) {
  const stateName = patch.state === "active" ? "active" : patch.state === "failed" ? "failed" : "inactive";
  const icon = patch.name.includes("数值") ? <IconCode /> : patch.name.includes("谱面") ? <IconImage /> : patch.name.includes("云") ? <IconCloud /> : patch.name.includes("工具") ? <IconSettings /> : <IconTool />;
  return <div className="patch-row"><span className={`patch-icon ${stateName}`}>{icon}</span><div className="patch-name"><strong>{patch.name}</strong><span>{patchDescriptions[patch.name] ?? patch.description}</span></div><span className={`patch-state ${patch.state}`}><span className="patch-state-dot" />{statusLabel[patch.state] ?? patch.state}</span></div>;
}

function FeaturePage({ title, description, children }: { title: string; description: string; children: React.ReactNode }) {
  return <><PageHeading title={title} description={description} /><section className="feature-panel angular-panel">{children}</section></>;
}

function PageHeading({ title, description }: { title: string; description: string }) {
  return <div className="page-heading"><div><h1>{title}</h1><p>{description}</p></div><span className="page-rule" /></div>;
}

function SectionHeading({ title, hint }: { title: string; hint?: string }) {
  return <div className="section-heading"><h2>{title}</h2>{hint ? <span>{hint}</span> : null}</div>;
}

function SettingSwitch({ label, description, value, onChange }: { label: string; description?: string; value: boolean; onChange: (value: boolean) => void }) {
  return <div className="setting-row"><div><strong>{label}</strong>{description ? <span>{description}</span> : null}</div><Switch checked={value} onChange={onChange} /></div>;
}

function SettingNumber({ label, value, min, max, step, suffix, onChange }: { label: string; value: number; min: number; max?: number; step: number; suffix: string; onChange: (value: number) => void }) {
  const [draft, setDraft] = React.useState(String(value));

  React.useEffect(() => {
    setDraft(String(value));
  }, [value]);

  const commit = () => {
    if (draft.trim() === "") {
      setDraft(String(value));
      return;
    }

    const next = Number(draft);
    if (Number.isFinite(next)) {
      if (next !== value) onChange(next);
    } else {
      setDraft(String(value));
    }
  };

  return <div className="setting-row"><div><strong>{label}</strong></div><div className="number-control"><input className="number-input" type="number" value={draft} min={min} max={max} step={step} onChange={(event) => setDraft(event.target.value)} onBlur={commit} onKeyDown={(event) => { if (event.key === "Enter") event.currentTarget.blur(); }} /><span>{suffix}</span></div></div>;
}

function SettingText({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  const [draft, setDraft] = React.useState(value);

  React.useEffect(() => {
    setDraft(value);
  }, [value]);

  const commit = () => {
    if (draft !== value) onChange(draft);
  };

  return <div className="setting-row"><div><strong>{label}</strong></div><input className="text-input" value={draft} onChange={(event) => setDraft(event.target.value)} onBlur={commit} onKeyDown={(event) => { if (event.key === "Enter") event.currentTarget.blur(); }} /></div>;
}

function SettingSelect({ label, group, value, options, onChange }: { label: string; group?: string; value: string; options: string[]; onChange: (value: string) => void }) {
  return <div className="setting-row"><div><strong>{label}</strong></div><CustomSelect group={group} value={value} options={options} onChange={onChange} /> </div>;
}

function CustomSelect({ group, value, options, onChange }: { group?: string; value: string; options: string[]; onChange: (value: string) => void }) {
  const [open, setOpen] = React.useState(false);
  const [menuStyle, setMenuStyle] = React.useState<React.CSSProperties>({});
  const controlRef = React.useRef<HTMLButtonElement>(null);
  const menuRef = React.useRef<HTMLDivElement>(null);

  React.useEffect(() => {
    if (!open) return;
    const updatePosition = () => {
      const rect = controlRef.current?.getBoundingClientRect();
      if (!rect) return;
      setMenuStyle({ left: rect.left, top: rect.bottom + 6, width: rect.width });
    };
    const closeOnOutsideClick = (event: MouseEvent) => {
      const target = event.target as Node;
      if (controlRef.current && !controlRef.current.contains(target) && !menuRef.current?.contains(target)) setOpen(false);
    };
    updatePosition();
    document.addEventListener("mousedown", closeOnOutsideClick);
    window.addEventListener("resize", updatePosition);
    window.addEventListener("scroll", updatePosition, true);
    return () => {
      document.removeEventListener("mousedown", closeOnOutsideClick);
      window.removeEventListener("resize", updatePosition);
      window.removeEventListener("scroll", updatePosition, true);
    };
  }, [open]);

  const menu = open ? createPortal(
    <div ref={menuRef} className="select-menu" role="listbox" style={menuStyle}>
      {options.map((option) => <button className={`select-option ${option === value ? "is-selected" : ""}`} key={option} type="button" role="option" aria-selected={option === value} onClick={() => { onChange(option); setOpen(false); }}><span className="select-option-check">{option === value ? <IconCheck /> : null}</span><span>{labelFor(group ?? "", option)}</span></button>)}
    </div>,
    document.body,
  ) : null;

  return <div className="select-field"><button ref={controlRef} className={`select-control ${open ? "is-open" : ""}`} type="button" aria-haspopup="listbox" aria-expanded={open} onClick={() => setOpen((current) => !current)}><span className="select-value">{labelFor(group ?? "", value)}</span><IconDown className="select-chevron" /></button>{menu}</div>;
}

function InfoBlock({ text }: { text: string }) { return <div className="info-block"><IconInfoCircle /><span>{text}</span></div>; }
function Metric({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) { return <div className="metric"><span className="metric-icon">{icon}</span><span><small>{label}</small><strong>{value}</strong></span></div>; }
function DetailLine({ label, value }: { label: string; value: string }) { return <div className="detail-line"><span>{label}</span><strong title={value}>{value}</strong></div>; }

export default App;

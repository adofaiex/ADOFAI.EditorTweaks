import type { SettingsState, WebUiState } from "./types";

const queryToken = (): string => {
  return new URLSearchParams(window.location.search).get("token") ?? "";
};

export const isLocalPreview = (): boolean => {
  return window.location.protocol === "file:" || !queryToken();
};

const apiUrl = (path: string): string => {
  const token = queryToken();
  return `${path}${path.includes("?") ? "&" : "?"}token=${encodeURIComponent(token)}`;
};

export async function postJson<T>(path: string, body: unknown = {}): Promise<T> {
  const response = await fetch(apiUrl(path), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const payload = (await response.json()) as T & { error?: string };
  if (!response.ok) {
    throw new Error(payload.error ?? `Request failed: ${response.status}`);
  }
  return payload;
}

export function createEventSource(onState: (state: WebUiState) => void): EventSource {
  const events = new EventSource(apiUrl("/api/events"));
  events.addEventListener("state", (event) => {
    try {
      onState(JSON.parse((event as MessageEvent).data) as WebUiState);
    } catch {
      // A malformed event must not break the EventSource reconnect loop.
    }
  });
  return events;
}

export function createMockState(): WebUiState {
  const settings: SettingsState = {
    WebUiOpenHotkey: "Ctrl+Shift+E",
    EnableNumericDrag: true,
    EnableCameraRelativeDecorationDragFix: true,
    EnableDecorationPivotFix: true,
    EnableVideoBackgroundSyncFix: true,
    PersistEditorPreferences: true,
    LegacyZipEncoding: "Auto",
    DecorationMoveSnapStep: 0.5,
    FloatStepPerPixel: 0.1,
    IntStepPerPixel: 1,
    MaxFloatingPoints: 3,
    ChartRenderWorkspaceDirectory: "D:/ADOFAI/Workspace",
    ChartRenderExportDirectory: "D:/ADOFAI/Exports",
    ChartRenderWidth: 1920,
    ChartRenderHeight: 1080,
    ChartRenderFps: 60,
    ChartRenderCrf: 18,
    ChartRenderBitrateMbps: 0,
    ChartRenderPreset: "veryfast",
    ChartRenderEncoderMode: "auto-balanced",
    ChartRenderCaptureFormat: "rgba",
    ChartRenderCaptureSource: "camera",
    ChartRenderPreviewMode: "full",
    ChartRenderAudioFormat: "aac",
    ChartRenderVideoFormat: "mp4",
    ChartRenderCompletionTailSeconds: 5,
    ChartRenderAudioSyncOffsetMs: 0,
    ChartRenderShowHitJudgments: true,
    ChartRenderUseSelectedRange: false,
    ChartRenderCustomMuxArgs: "",
  };

  const names = ["编辑器修复", "数值拖动", "装饰移动", "谱面渲染", "云同步", "工具"];
  return {
    server: { connected: false, version: "演示状态" },
    compatibility: { gameVersion: "1.3.2", editorVersion: "1.3.2", modVersion: "1.4.5" },
    patches: names.map((name, index) => ({
      id: `mock-${index}`,
      name,
      description: "与当前游戏版本兼容",
      state: "active",
      patchCount: 1,
      reason: "",
    })),
    patchSummary: { active: 6, total: 6, registrationError: false },
    cloud: { available: true, hasFile: true },
    settings,
    render: {
      active: false,
      taskId: "",
      state: "Idle",
      cancelRequested: false,
      captureSource: "Camera",
      outputPath: "",
      message: "",
      progress: {
        value: 0,
        writtenFrames: 0,
        totalFrames: 1,
        duplicateFrames: 0,
        duplicateRatio: 0,
        processingFramesPerSecond: 0,
        estimatedRemaining: "00:00:00",
        stage: "等待渲染",
        detail: "从 Mod 页面启动渲染后，进度会实时显示在这里。",
        encoderName: "",
        memoryBudget: "",
        queueBudget: "",
      },
    },
  };
}

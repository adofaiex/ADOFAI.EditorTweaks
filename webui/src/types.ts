export type PatchState = "active" | "failed" | "blocked" | "inactive";

export type PageKey =
  | "overview"
  | "fixes"
  | "numeric"
  | "decoration"
  | "render"
  | "cloud"
  | "tools";

export interface PatchStatus {
  id: string;
  name: string;
  description: string;
  state: PatchState;
  patchCount: number;
  reason: string;
  dependency?: string;
}

export interface SettingsState {
  WebUiOpenHotkey: string;
  EnableNumericDrag: boolean;
  EnableCameraRelativeDecorationDragFix: boolean;
  EnableDecorationPivotFix: boolean;
  EnableVideoBackgroundSyncFix: boolean;
  PersistEditorPreferences: boolean;
  LegacyZipEncoding: string;
  DecorationMoveSnapStep: number;
  FloatStepPerPixel: number;
  IntStepPerPixel: number;
  MaxFloatingPoints: number;
  ChartRenderWorkspaceDirectory: string;
  ChartRenderExportDirectory: string;
  ChartRenderWidth: number;
  ChartRenderHeight: number;
  ChartRenderFps: number;
  ChartRenderCrf: number;
  ChartRenderBitrateMbps: number;
  ChartRenderPreset: string;
  ChartRenderEncoderMode: string;
  ChartRenderCaptureFormat: string;
  ChartRenderCaptureSource: string;
  ChartRenderPreviewMode: string;
  ChartRenderAudioFormat: string;
  ChartRenderVideoFormat: string;
  ChartRenderCompletionTailSeconds: number;
  ChartRenderAudioSyncOffsetMs: number;
  ChartRenderShowHitJudgments: boolean;
  ChartRenderUseSelectedRange: boolean;
  ChartRenderCustomMuxArgs: string;
}

export interface RenderProgress {
  value: number;
  writtenFrames: number;
  totalFrames: number;
  duplicateFrames: number;
  duplicateRatio: number;
  processingFramesPerSecond: number;
  estimatedRemaining: string;
  stage: string;
  detail: string;
  encoderName: string;
  memoryBudget: string;
  queueBudget: string;
}

export interface RenderState {
  active: boolean;
  taskId: string;
  state: string;
  cancelRequested: boolean;
  captureSource: string;
  outputPath: string;
  message: string;
  progress: RenderProgress;
}

export interface WebUiState {
  server: {
    connected: boolean;
    version: string;
  };
  compatibility: {
    gameVersion: string;
    editorVersion: string;
    modVersion: string;
  };
  patches: PatchStatus[];
  patchSummary: {
    active: number;
    total: number;
    registrationError: boolean;
  };
  cloud: {
    available: boolean;
    hasFile: boolean;
  };
  settings: SettingsState;
  render: RenderState;
}

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartGameViewFrameCapture : IChartFrameCapture
    {
        private readonly int rowBytes;
        private readonly TextureFormat readbackFormat;
        private readonly RenderTexture captureTarget;
        private bool disposed;

        public ChartGameViewFrameCapture(string captureFormat)
        {
            Width = Screen.width;
            Height = Screen.height;
            if (Width <= 0 || Height <= 0)
            {
                throw new InvalidOperationException(Settings.Text("chartRendererInvalidGameViewSize"));
            }

            rowBytes = Width * 4;
            readbackFormat = ChartFrameReadback.ResolveFormat(captureFormat);
            PixelFormatName = ChartFrameReadback.GetPixelFormatName(readbackFormat);
            captureTarget = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
            {
                name = "EditorTweaks.ChartRender.GameView",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!captureTarget.Create())
            {
                UnityEngine.Object.Destroy(captureTarget);
                throw new InvalidOperationException("Failed to create game view render target " + Width + "x" + Height + ".");
            }

            ChartRenderDiagnostics.Log("Frame capture mode=game-view. source="
                + Width + "x" + Height
                + ", output=follows-game-view"
                + ", readback=" + PixelFormatName
                + ", verticalFlip=false.");
        }

        public string PixelFormatName { get; }

        public int Width { get; }

        public int Height { get; }

        public bool RequiresVerticalFlip => false;

        public ChartPendingFrame RequestFrame(int index, int repeatCount = 1)
        {
            ThrowIfDisposed();
            if (Screen.width != Width || Screen.height != Height)
            {
                string message = Settings.Text("chartRendererGameViewSizeChanged");
                ChartRenderDiagnostics.Log(message + " Initial=" + Width + "x" + Height
                    + ", current=" + Screen.width + "x" + Screen.height + ".");
                throw new InvalidOperationException(message);
            }

            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(captureTarget);
                return new ChartPendingFrame(
                    index,
                    repeatCount,
                    AsyncGPUReadback.Request(captureTarget, 0, readbackFormat),
                    rowBytes,
                    Height);
            }
            catch (Exception ex)
            {
                ChartRenderDiagnostics.Log("Game view capture failed at frame " + index + ": " + ex);
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            captureTarget.Release();
            UnityEngine.Object.Destroy(captureTarget);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ChartGameViewFrameCapture));
            }
        }
    }
}

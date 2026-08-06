using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EditorTweaks.Features.ChartRendering
{
    internal interface IChartFrameCapture : IDisposable
    {
        string PixelFormatName { get; }

        int Width { get; }

        int Height { get; }

        bool RequiresVerticalFlip { get; }

        ChartPendingFrame RequestFrame(int index, int repeatCount = 1);
    }

    internal static class ChartFrameCaptureFactory
    {
        public static IChartFrameCapture Create(Settings settings, string captureSource, bool showPreview)
        {
            string source = ChartRenderOptionValues.NormalizeCaptureSource(captureSource);
            if (source == ChartRenderOptionValues.CaptureSourceGameView)
            {
                return new ChartGameViewFrameCapture(settings.ChartRenderCaptureFormat);
            }

            return new ChartCameraFrameCapture(
                settings.ChartRenderWidth,
                settings.ChartRenderHeight,
                settings.ChartRenderCaptureFormat,
                showPreview);
        }
    }

    internal sealed class ChartCameraFrameCapture : IChartFrameCapture
    {
        private readonly int height;
        private readonly RenderTexture captureTarget;
        private readonly int rowBytes;
        private readonly TextureFormat readbackFormat;
        private readonly Camera bgStaticCamera;
        private readonly Camera bgCamera;
        private readonly Camera mainCamera;
        private readonly MeshRenderer? quadRenderer;
        private readonly RenderTexture? oldBgStaticTarget;
        private readonly RenderTexture? oldBgTarget;
        private readonly RenderTexture? oldMainTarget;
        private readonly Texture? oldQuadTexture;
        private readonly bool oldOverlayActive;
        private readonly bool oldQuadActive;

        public ChartCameraFrameCapture(int width, int height, string captureFormat, bool showPreview)
        {
            this.height = height;
            Width = width;
            Height = height;
            rowBytes = width * 4;
            readbackFormat = ChartFrameReadback.ResolveFormat(captureFormat);
            PixelFormatName = ChartFrameReadback.GetPixelFormatName(readbackFormat);

            scrCamera camera = scrCamera.instance;
            if (camera == null)
            {
                throw new InvalidOperationException("scrCamera.instance is not available.");
            }

            bgStaticCamera = camera.Bgcamstatic;
            bgCamera = camera.BGcam;
            mainCamera = camera.camobj;
            if (bgStaticCamera == null || bgCamera == null || mainCamera == null)
            {
                throw new InvalidOperationException("The chart camera chain is not available.");
            }

            oldBgStaticTarget = bgStaticCamera.targetTexture;
            oldBgTarget = bgCamera.targetTexture;
            oldMainTarget = mainCamera.targetTexture;
            oldOverlayActive = camera.Overlaycam != null && camera.Overlaycam.gameObject.activeSelf;
            oldQuadActive = camera.quad != null && camera.quad.activeSelf;
            quadRenderer = camera.quad == null ? null : camera.quad.GetComponent<MeshRenderer>();
            oldQuadTexture = quadRenderer == null ? null : quadRenderer.material.mainTexture;

            captureTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            captureTarget.Create();

            bgStaticCamera.targetTexture = captureTarget;
            bgCamera.targetTexture = captureTarget;
            mainCamera.targetTexture = captureTarget;
            if (camera.Overlaycam != null)
            {
                camera.Overlaycam.gameObject.SetActive(showPreview);
            }

            if (camera.quad != null)
            {
                camera.quad.SetActive(showPreview);
            }

            if (showPreview && quadRenderer != null)
            {
                quadRenderer.material.mainTexture = captureTarget;
            }

            ChartRenderDiagnostics.Log("Frame capture mode=camera. Camera chain: Bgcamstatic="
                + CameraName(bgStaticCamera) + ", BGcam=" + CameraName(bgCamera)
                + ", camobj=" + CameraName(mainCamera)
                + ", scene=" + ADOBase.sceneName
                + ", output=" + Width + "x" + Height
                + ", readback=" + PixelFormatName
                + ", preview=" + showPreview + ".");
        }

        public string PixelFormatName { get; }

        public int Width { get; }

        public int Height { get; }

        public bool RequiresVerticalFlip => true;

        public ChartPendingFrame RequestFrame(int index, int repeatCount = 1)
        {
            return new ChartPendingFrame(index, repeatCount, AsyncGPUReadback.Request(captureTarget, 0, readbackFormat), rowBytes, height);
        }

        public void Dispose()
        {
            bgStaticCamera.targetTexture = oldBgStaticTarget;
            bgCamera.targetTexture = oldBgTarget;
            mainCamera.targetTexture = oldMainTarget;

            scrCamera camera = scrCamera.instance;
            if (camera != null)
            {
                if (camera.Overlaycam != null)
                {
                    camera.Overlaycam.gameObject.SetActive(oldOverlayActive);
                }

                if (camera.quad != null)
                {
                    camera.quad.SetActive(oldQuadActive);
                }
            }

            if (quadRenderer != null)
            {
                quadRenderer.material.mainTexture = oldQuadTexture;
            }

            if (captureTarget != null)
            {
                captureTarget.Release();
                UnityEngine.Object.Destroy(captureTarget);
            }
        }

        private static string CameraName(Camera camera)
        {
            return camera == null ? "<null>" : camera.name + "(depth=" + camera.depth + ")";
        }

    }

    internal static class ChartFrameReadback
    {
        public static TextureFormat ResolveFormat(string captureFormat)
        {
            if (ChartRenderOptionValues.NormalizeCaptureFormat(captureFormat) != ChartRenderOptionValues.CaptureBgra)
            {
                return TextureFormat.RGBA32;
            }

            try
            {
                if (Enum.IsDefined(typeof(TextureFormat), "BGRA32"))
                {
                    return (TextureFormat)Enum.Parse(typeof(TextureFormat), "BGRA32");
                }
            }
            catch
            {
            }

            ChartRenderDiagnostics.Log("BGRA32 readback is not available in this Unity runtime; falling back to RGBA32.");
            return TextureFormat.RGBA32;
        }

        public static string GetPixelFormatName(TextureFormat format)
        {
            return format.ToString() == "BGRA32" ? "bgra" : "rgba";
        }
    }

    internal sealed class ChartPendingFrame
    {
        private readonly int byteLength;
        private readonly AsyncGPUReadbackRequest request;

        public ChartPendingFrame(int index, int repeatCount, AsyncGPUReadbackRequest request, int rowBytes, int height)
        {
            Index = index;
            RepeatCount = Math.Max(1, repeatCount);
            this.request = request;
            byteLength = rowBytes * height;
        }

        public int Index { get; }

        public int RepeatCount { get; }

        public int ByteLength => byteLength;

        public bool Done => request.done;

        public void Complete(byte[] destination)
        {
            if (request.hasError)
            {
                throw new InvalidOperationException("AsyncGPUReadback failed for frame " + Index + ".");
            }

            Unity.Collections.NativeArray<byte> source = request.GetData<byte>();
            if (destination.Length < source.Length)
            {
                throw new InvalidOperationException("Frame buffer is too small.");
            }

            source.CopyTo(destination);
        }
    }
}

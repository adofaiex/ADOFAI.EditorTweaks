using System;
using System.Diagnostics;
using UnityEngine;

namespace EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderProgressModel
    {
        private readonly Stopwatch stopwatch = new Stopwatch();

        public int WrittenFrames { get; private set; }

        public int TotalFrames { get; private set; } = 1;

        public int DuplicateFrames { get; private set; }

        public float Progress => TotalFrames <= 0 ? 0f : Mathf.Clamp01(WrittenFrames / (float)TotalFrames);

        public float DuplicateRatio => WrittenFrames <= 0 ? 0f : DuplicateFrames / (float)WrittenFrames;

        public double ProcessingFps
        {
            get
            {
                double elapsed = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                return WrittenFrames / elapsed;
            }
        }

        public TimeSpan EstimatedRemaining
        {
            get
            {
                double fps = ProcessingFps;
                double remaining = fps <= 0.001 ? 0.0 : (TotalFrames - WrittenFrames) / fps;
                return TimeSpan.FromSeconds(Math.Max(0, remaining));
            }
        }

        public string SmoothnessText
        {
            get
            {
                float ratio = DuplicateRatio;
                if (ratio <= 0.01f)
                {
                    return "excellent";
                }

                if (ratio <= 0.03f)
                {
                    return "good";
                }

                if (ratio <= 0.05f)
                {
                    return "minor stutter";
                }

                if (ratio <= 0.10f)
                {
                    return "visible stutter";
                }

                return "heavy stutter";
            }
        }

        public void Reset()
        {
            WrittenFrames = 0;
            DuplicateFrames = 0;
            TotalFrames = 1;
            stopwatch.Restart();
        }

        public void SetTotalFrames(int totalFrames)
        {
            TotalFrames = Math.Max(1, totalFrames);
        }

        public void UpdateFrames(int writtenFrames, int duplicateFrames)
        {
            WrittenFrames = Math.Max(0, writtenFrames);
            DuplicateFrames = Math.Max(0, duplicateFrames);
        }
    }
}

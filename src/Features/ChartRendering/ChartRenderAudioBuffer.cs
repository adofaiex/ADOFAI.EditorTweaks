using System;
using UnityEngine;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderAudioBuffer
    {
        private const int RenderDspBufferSize = 256;

        private AudioConfiguration savedConfiguration;
        private bool active;

        public bool IsActive => active;

        public void Begin()
        {
            if (active)
            {
                return;
            }

            savedConfiguration = AudioSettings.GetConfiguration();
            int originalSize = savedConfiguration.dspBufferSize;
            int targetSize = Math.Min(originalSize, RenderDspBufferSize);
            if (targetSize <= 0 || targetSize == originalSize)
            {
                ChartRenderDiagnostics.Log("Render audio DSP buffer unchanged. dspBufferSize=" + originalSize + ".");
                active = true;
                return;
            }

            AudioConfiguration renderConfiguration = savedConfiguration;
            renderConfiguration.dspBufferSize = targetSize;
            AudioSettings.Reset(renderConfiguration);
            active = true;
            ChartRenderDiagnostics.Log("Render audio DSP buffer set temporarily. original=" + originalSize
                + " render=" + targetSize + ".");
        }

        public void Restore()
        {
            if (!active)
            {
                return;
            }

            try
            {
                AudioSettings.Reset(savedConfiguration);
                ChartRenderDiagnostics.Log("Render audio DSP buffer restored. dspBufferSize="
                    + savedConfiguration.dspBufferSize + ".");
            }
            catch (Exception exception)
            {
                ChartRenderDiagnostics.Log("Failed to restore render audio DSP buffer: " + exception.Message);
            }
            finally
            {
                active = false;
            }
        }
    }
}

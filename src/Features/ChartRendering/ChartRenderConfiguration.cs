using ADOFAI.EditorTweaks.Api.Rendering;

namespace ADOFAI.EditorTweaks.Features.ChartRendering
{
    internal sealed class ChartRenderConfiguration
    {
        public ChartRenderConfiguration(ChartRenderRequest request, Settings settings, ChartRenderRange range)
        {
            Request = request;
            Settings = settings;
            Range = range;
        }

        public ChartRenderRequest Request { get; }

        public Settings Settings { get; }

        public ChartRenderPlaybackMode PlaybackMode => Request.PlaybackMode;

        public ChartRenderRange Range { get; }

        public string OutputFileName => Request.OutputFileName;

        public bool ShowBuiltInProgressUi => Request.ShowBuiltInProgressUi;

        public bool ShowHitJudgments => Request.ShowHitJudgments;
    }
}

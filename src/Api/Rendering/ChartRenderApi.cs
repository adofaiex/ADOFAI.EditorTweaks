using ADOFAI.EditorTweaks.Features.ChartRendering;

namespace ADOFAI.EditorTweaks.Api.Rendering
{
    public static class ChartRenderApi
    {
        public const int ApiVersion = 1;

        public static ChartRenderTask? CurrentTask => ChartRenderService.CurrentTask;

        public static ChartRenderAvailability GetAvailability()
        {
            return ChartRenderService.GetAvailability();
        }

        public static ChartRenderRequest CreateRequestFromCurrentSettings()
        {
            return ChartRenderService.CreateRequestFromCurrentSettings();
        }

        public static ChartRenderStartResult Start(ChartRenderRequest request)
        {
            return ChartRenderService.Start(request);
        }
    }
}

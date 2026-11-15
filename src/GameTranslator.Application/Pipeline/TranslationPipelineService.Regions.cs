using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public sealed partial class TranslationPipelineService
{
    public sealed partial class LiveTranslationSession
    {
        private async Task<TextCandidateRegionDetectionResult> DetectLiveRegionsAsync(LiveZoneState state,
            LiveCapturedZone captured, CancellationToken token)
        {
            var request = CreateOcrRequest(profile, state.Zone, captured.Frame);
            if (!runOptions.EnableIncrementalMenuDetection || state.Zone.ContentLayoutMode != ContentLayoutMode.StaticMenu
                || profile.OcrSettings.WatermarkFilter != WatermarkFilterMode.Off
                || profile.OcrSettings.OrientationMode == OcrOrientationMode.Vertical)
                return await service.candidateRegionOcrService.DetectAsync(request, token);

            if (!state.CandidateStates.Values.Any(c => c.Menu?.Authority.Confirmed == true && c.Result is not null))
                state.MenuChanges.Reset(); // Initial confirmation still uses independent detector observations.
            var previous = state.CandidateStates.Values.Select(c => c.Region.Candidate).ToArray();
            var plan = state.MenuChanges.Plan(captured.Frame, previous.Select(c => c.Bounds).ToArray(), MenuNow, token);
            var retained = previous.Where(c => !plan.Areas.Any(a => MenuRegionChangeMap.Intersects(a, c.Bounds))).ToArray();
            var scope = new RegionDetectionDiagnostics(plan.Reason, plan.FullScan, plan.ChangedTiles, plan.TotalTiles,
                plan.Areas, retained.Length, plan.FullScan ? 1 : 0,
                plan.FullScan ? (long)captured.Frame.Width * captured.Frame.Height : 0);
            TextCandidateRegionDetectionResult detection;
            if (plan.FullScan)
            {
                var full = await service.candidateRegionOcrService.DetectAsync(request, token);
                detection = new(full.Availability, full.DetectorId, full.UnavailableReason, full.Regions, full.Diagnostics)
                { RegionRefresh = scope };
            }
            else detection = await service.candidateRegionOcrService.DetectAreasAsync(request, plan.Areas, retained, scope, token);
            token.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (detection.Availability == TextCandidateDetectorAvailability.Available)
                state.MenuChanges.Commit(captured.Frame, plan, detection.RegionRefresh!.FullScan);
            else state.MenuChanges.Reset();
            return detection;
        }
    }
}

using GameTranslator.Application.Cache;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

internal static class LanguageTranslationBatcher
{
    public static async Task<TranslationCacheResult> RunAsync(TranslatorSettings settings,
        IReadOnlyList<OcrTextBlock> blocks,
        Func<TranslatorSettings, string[], Task<TranslationCacheResult>> translate)
    {
        var batches = blocks.Select((block, index) => (block, index)).GroupBy(item =>
            settings.AutoDetectSourceLanguage == true ? item.block.TranslationSourceLanguage : settings.ResolveSourceLanguage()).ToArray();
        var output = new string[blocks.Count];
        var results = new List<TranslationCacheResult>();
        foreach (var batch in batches)
        {
            var effective = settings.AutoDetectSourceLanguage == true
                ? settings with { SourceLanguage = batch.Key, AutoDetectSourceLanguage = batch.Key == "auto" } : settings;
            var items = batch.ToArray();
            var result = await translate(effective, items.Select(item => item.block.Text).ToArray());
            if (result.TranslatedTexts.Count != items.Length)
                throw new InvalidOperationException("Translator response item count must match language group count.");
            for (var i = 0; i < items.Length; i++) output[items[i].index] = result.TranslatedTexts[i];
            results.Add(result);
        }
        if (results.Count == 1) return results[0];
        var requests = results.Where(r => r.ProviderRequestIssued).ToArray();
        return new TranslationCacheResult(output, results.Max(r => r.TranslatedAt),
            results.Sum(r => r.MemoryHitCount), results.Sum(r => r.PersistentHitCount),
            results.Sum(r => r.MissCount), results.Sum(r => r.StoredCount), settings.Provider,
            string.Join("; ", results.Select(r => r.DiagnosticMessage).Where(s => s.Length > 0).Distinct()),
            requests.Length == 0 ? null : requests.Min(r => r.ProviderRequestStartedAt),
            requests.Length == 0 ? null : requests.Max(r => r.ProviderRequestCompletedAt),
            results.Sum(r => r.SanitizedTranslationCount));
    }
}

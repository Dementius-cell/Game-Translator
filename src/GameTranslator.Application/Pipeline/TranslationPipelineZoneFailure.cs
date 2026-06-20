namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineZoneFailure
{
    public TranslationPipelineZoneFailure(
        string zoneId,
        string zoneName,
        TranslationPipelineStage stage,
        string message,
        Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);

        ZoneId = zoneId;
        ZoneName = zoneName?.Trim() ?? string.Empty;
        Stage = stage;
        Message = message?.Trim() ?? string.Empty;
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    public string ZoneId { get; }

    public string ZoneName { get; }

    public TranslationPipelineStage Stage { get; }

    public string Message { get; }

    public Exception Exception { get; }
}

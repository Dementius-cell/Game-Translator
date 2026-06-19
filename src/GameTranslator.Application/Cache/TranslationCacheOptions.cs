namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheOptions
{
    public static TimeSpan DefaultTimeToLive { get; } = TimeSpan.FromDays(30);

    public TranslationCacheOptions(TimeSpan? timeToLive = null)
    {
        var effectiveTimeToLive = timeToLive ?? DefaultTimeToLive;
        if (effectiveTimeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "Translation cache TTL must be positive.");
        }

        TimeToLive = effectiveTimeToLive;
    }

    public TimeSpan TimeToLive { get; }
}

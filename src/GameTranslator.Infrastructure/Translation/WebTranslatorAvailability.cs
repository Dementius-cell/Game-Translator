using System.Net;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

/// <summary>Classifies actual web-provider traffic; never probes unrelated hosts or retries requests.</summary>
internal static class WebTranslatorAvailability
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request,
        string provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslatorProviderException(provider, TranslatorProviderFailureKind.Timeout,
                $"{provider} did not respond in time. Try again later.");
        }
        catch (HttpRequestException exception)
        {
            throw ClassifyNetworkFailure(provider, exception);
        }
    }

    public static TranslatorProviderException ClassifyNetworkFailure(string provider, HttpRequestException exception)
    {
        var (kind, detail) = exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => (TranslatorProviderFailureKind.Dns, "The service name could not be resolved. Check DNS and your connection."),
            HttpRequestError.ConnectionError => (TranslatorProviderFailureKind.Connection, "Could not connect to the service. Check your connection or try later."),
            HttpRequestError.SecureConnectionError => (TranslatorProviderFailureKind.Tls, "A secure connection to the service could not be established."),
            _ => (TranslatorProviderFailureKind.Network, "The service request failed. Check your connection or try later."),
        };
        // Do not retain transport exception text: it may contain request URLs and source text.
        return new TranslatorProviderException(provider, kind, $"{provider}: {detail}");
    }

    public static void ValidateResponse(string provider, HttpResponseMessage response, string body,
        bool expectsJson = true, bool handleHttpErrors = true)
    {
        // Preserve provider-specific throttle handling (Bing cooldown) when requested.
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (!handleHttpErrors) return;
            var delay = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
            if (delay <= TimeSpan.Zero) delay = null;
            throw new TranslatorProviderException(provider, response.StatusCode, TranslatorProviderFailureKind.Throttled,
                $"{provider} limited requests (HTTP 429). Try again later.", retryAfter: delay);
        }

        var prefix = body.AsSpan().TrimStart();
        var html = string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        var path = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
        var redirectPath = response.Headers.Location?.IsAbsoluteUri == true
            ? response.Headers.Location.AbsolutePath : response.Headers.Location?.OriginalString.Split('?')[0] ?? string.Empty;
        var challengePath = path.StartsWith("/showcaptcha", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/sorry/", StringComparison.OrdinalIgnoreCase)
            || redirectPath.StartsWith("/showcaptcha", StringComparison.OrdinalIgnoreCase)
            || redirectPath.StartsWith("/sorry/", StringComparison.OrdinalIgnoreCase);
        // Inspect a bounded prefix; never persist it or treat translated JSON mentioning CAPTCHA as a challenge.
        var sample = body[..Math.Min(body.Length, 65536)];
        if (challengePath || (html && (sample.Contains("smart-captcha", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("g-recaptcha", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("/showcaptcha", StringComparison.OrdinalIgnoreCase))))
        {
            throw new TranslatorProviderException(provider, response.StatusCode, TranslatorProviderFailureKind.UserVerificationRequired,
                $"{provider} requires user verification. Stop translation and check the service in your browser; browser verification may not authorize this application.");
        }
        if (!response.IsSuccessStatusCode)
        {
            if (handleHttpErrors)
                throw new TranslatorProviderException(provider, response.StatusCode,
                    $"{provider} returned HTTP {(int)response.StatusCode}. Try again later.");
            return;
        }
        if (expectsJson && html)
            throw new TranslatorProviderException(provider, TranslatorProviderFailureKind.UnsupportedResponse,
                $"{provider} returned a web page instead of translation data. Check the service in your browser.");
    }
}

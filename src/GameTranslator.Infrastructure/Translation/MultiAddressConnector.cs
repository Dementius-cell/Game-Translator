using System.Net;
using System.Net.Sockets;

namespace GameTranslator.Infrastructure.Translation;

/// <summary>Allows another DNS address to connect while an earlier address is unresponsive.</summary>
internal static class MultiAddressConnector
{
    public static ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token) =>
        new(ConnectAsync(context.DnsEndPoint, Dns.GetHostAddressesAsync, OpenSocketAsync,
            TimeSpan.FromMilliseconds(200), token));

    internal static async Task<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPEndPoint, CancellationToken, Task<Stream>> connect,
        TimeSpan stagger,
        CancellationToken token)
    {
        var addresses = (await resolve(endpoint.Host, token)).Distinct().ToArray();
        token.ThrowIfCancellationRequested();
        using var attemptsCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var attempts = addresses.Select((address, index) => AttemptAsync(address, index)).ToArray();
        var pending = attempts.ToList();
        Stream? winner = null;
        Exception? lastFailure = null;
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var result = await completed;
                token.ThrowIfCancellationRequested();
                if (result.Stream is not null)
                {
                    winner = result.Stream;
                    return winner;
                }
                lastFailure = result.Failure ?? lastFailure;
            }
            token.ThrowIfCancellationRequested();
            throw lastFailure ?? new SocketException((int)SocketError.HostNotFound);
        }
        finally
        {
            attemptsCancellation.Cancel();
            // Drain every attempt, including simultaneous successes, before transferring ownership.
            foreach (var result in await Task.WhenAll(attempts))
                if (result.Stream is not null && !ReferenceEquals(result.Stream, winner))
                    result.Stream.Dispose();
        }

        async Task<(Stream? Stream, Exception? Failure)> AttemptAsync(IPAddress address, int index)
        {
            try
            {
                await Task.Delay(stagger * index, attemptsCancellation.Token);
                return (await connect(new IPEndPoint(address, endpoint.Port), attemptsCancellation.Token), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }
    }

    private static async Task<Stream> OpenSocketAsync(IPEndPoint endpoint, CancellationToken token)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

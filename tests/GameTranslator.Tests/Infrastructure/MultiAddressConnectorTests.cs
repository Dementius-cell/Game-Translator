using System.IO;
using System.Net;
using System.Net.Sockets;
using GameTranslator.Infrastructure.Translation;

namespace GameTranslator.Tests.Infrastructure;

public sealed class MultiAddressConnectorTests
{
    private static readonly IPAddress[] Addresses = [IPAddress.Loopback, IPAddress.IPv6Loopback];

    [Fact]
    public async Task StalledFirstAddress_DoesNotBlockSecond_AndCancelsLoser()
    {
        var cancelled = false;
        var stream = new MemoryStream();
        var result = await MultiAddressConnector.ConnectAsync(new DnsEndPoint("example.test", 443),
            (_, _) => Task.FromResult(Addresses), async (endpoint, token) =>
            {
                if (endpoint.Address.Equals(IPAddress.IPv6Loopback)) return stream;
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { cancelled = token.IsCancellationRequested; }
                throw new InvalidOperationException();
            }, TimeSpan.FromMilliseconds(1), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(stream, result);
        Assert.True(cancelled);
        Assert.True(stream.CanRead);
        result.Dispose();
    }

    [Fact]
    public async Task SimultaneousSuccess_DisposesOnlyLosingStream()
    {
        var streams = new List<MemoryStream>();
        var result = await MultiAddressConnector.ConnectAsync(new DnsEndPoint("example.test", 443),
            (_, _) => Task.FromResult(Addresses), (_, _) =>
            {
                var stream = new MemoryStream(); streams.Add(stream); return Task.FromResult<Stream>(stream);
            }, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(2, streams.Count);
        Assert.Single(streams, stream => stream.CanRead);
        Assert.True(result.CanRead);
        result.Dispose();
    }

    [Fact]
    public async Task Cancellation_DrainsAllPendingConnections()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        var pending = MultiAddressConnector.ConnectAsync(new DnsEndPoint("example.test", 443),
            (_, _) => Task.FromResult(Addresses), async (_, token) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { finished = true; }
                return new MemoryStream();
            }, TimeSpan.FromSeconds(10), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(finished);
    }

    [Fact]
    public async Task AllAddressesFail_PreservesSocketFailure()
    {
        var failure = await Assert.ThrowsAsync<SocketException>(() => MultiAddressConnector.ConnectAsync(
            new DnsEndPoint("example.test", 443), (_, _) => Task.FromResult(Addresses),
            (_, _) => Task.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused)),
            TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SocketError.ConnectionRefused, failure.SocketErrorCode);
    }

    [Fact]
    public async Task EmptyDnsResult_IsReportedAsHostNotFound()
    {
        var failure = await Assert.ThrowsAsync<SocketException>(() => MultiAddressConnector.ConnectAsync(
            new DnsEndPoint("example.test", 443), (_, _) => Task.FromResult(Array.Empty<IPAddress>()),
            (_, _) => throw new InvalidOperationException("No socket should be opened"),
            TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SocketError.HostNotFound, failure.SocketErrorCode);
    }
}

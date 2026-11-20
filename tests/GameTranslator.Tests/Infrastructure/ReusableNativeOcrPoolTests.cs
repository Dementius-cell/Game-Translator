using System.IO;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class ReusableNativeOcrPoolTests
{
    private sealed class Native : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void SuccessfulLeaseIsExclusiveAndReused()
    {
        using var pool = new ReusableNativeOcrPool<Native>(3);
        var a = pool.Rent("model", () => new Native());
        var b = pool.Rent("model", () => new Native());
        var c = pool.Rent("model", () => new Native());
        Assert.NotSame(a.Value, b.Value);
        Assert.NotSame(a.Value, c.Value);
        Assert.Throws<InvalidOperationException>(() => pool.Rent("model", () => new Native()));
        a.Reusable = true;
        a.Dispose();
        using var next = pool.Rent("model", () => throw new Exception("Unexpected model reload"));
        Assert.Same(a.Value, next.Value);
        b.Dispose();
        c.Dispose();
    }

    [Fact]
    public void FailedOrCancelledLeaseIsDiscarded()
    {
        using var pool = new ReusableNativeOcrPool<Native>(1);
        var a = pool.Rent("model", () => new Native());
        a.Dispose();
        a.Dispose();
        Assert.Equal(1, a.Value.Disposals);
        using var next = pool.Rent("model", () => new Native());
        Assert.NotSame(a.Value, next.Value);
    }

    [Fact]
    public void ChangedModelDisposesPreviousInstance()
    {
        using var pool = new ReusableNativeOcrPool<Native>(1);
        var a = pool.Rent("eng:old", () => new Native());
        a.Reusable = true;
        a.Dispose();
        using var next = pool.Rent("eng:new", () => new Native());
        Assert.Equal(1, a.Value.Disposals);
        Assert.NotSame(a.Value, next.Value);
    }

    [Fact]
    public void ShutdownDisposesIdleButWaitsForBorrower()
    {
        var pool = new ReusableNativeOcrPool<Native>(2);
        var idle = pool.Rent("model", () => new Native());
        var active = pool.Rent("model", () => new Native());
        idle.Reusable = true;
        idle.Dispose();
        pool.Dispose();
        pool.Dispose();
        Assert.Equal(1, idle.Value.Disposals);
        Assert.Equal(0, active.Value.Disposals);
        Assert.Throws<ObjectDisposedException>(() => pool.Rent("model", () => new Native()));
        active.Reusable = true;
        active.Dispose();
        Assert.Equal(1, active.Value.Disposals);
    }

    [Fact]
    public void FailedCreationDoesNotConsumeSlot()
    {
        using var pool = new ReusableNativeOcrPool<Native>(1);
        Assert.Throws<IOException>(() => pool.Rent("model", () => throw new IOException()));
        using var next = pool.Rent("model", () => new Native());
        Assert.NotNull(next.Value);
    }
}

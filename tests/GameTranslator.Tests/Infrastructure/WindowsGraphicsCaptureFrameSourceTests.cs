using System.IO;
using GameTranslator.Application.Capture;
using GameTranslator.Infrastructure.Capture;
using GameTranslator.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class WindowsGraphicsCaptureFrameSourceTests
{
    [Fact]
    public void CachedSnapshot_TwoDeliveriesPreserveActualFrameOriginAndAge()
    {
        var capturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var origin = new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(42), capturedAt);
        var snapshot = new WindowsGraphicsCaptureFrameSource.FullFrameSnapshot(4, 4, 16, new byte[64], origin);
        var first = snapshot.Crop(new CaptureRegion(0, 0, 4, 4), capturedAt);
        var reused = snapshot.Crop(new CaptureRegion(1, 1, 2, 2), capturedAt.AddSeconds(5), isCachedReuse: true);
        Assert.Same(first.Origin, reused.Origin);
        Assert.Equal(capturedAt, first.CapturedAt);
        Assert.Equal(capturedAt.AddSeconds(5), reused.CapturedAt);
        Assert.Equal(capturedAt, reused.Origin!.CapturedAt);
        Assert.False(first.IsCachedReuse);
        Assert.True(reused.IsCachedReuse);
    }

    [Fact]
    public void IdenticalPixelsFromDifferentCompositorFrames_HaveDifferentOrigins()
    {
        var source = Guid.NewGuid();
        var time = DateTimeOffset.UtcNow;
        var first = new WindowsGraphicsCaptureFrameSource.FullFrameSnapshot(4, 4, 16, new byte[64],
            new CaptureFrameOrigin(source, TimeSpan.FromSeconds(42), time)).Crop(new CaptureRegion(0, 0, 4, 4), time);
        var next = new WindowsGraphicsCaptureFrameSource.FullFrameSnapshot(4, 4, 16, new byte[64],
            new CaptureFrameOrigin(source, TimeSpan.FromSeconds(43), time.AddSeconds(1))).Crop(new CaptureRegion(0, 0, 4, 4), time.AddSeconds(1));
        Assert.Equal(first.PixelData.ToArray(), next.PixelData.ToArray());
        Assert.NotEqual(first.Origin, next.Origin);
        Assert.False(next.IsCachedReuse);
    }

    [Fact]
    public void InfrastructureServiceModule_RegistersWindowsGraphicsCaptureFrameSource()
    {
        var services = new ServiceCollection();

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICaptureFrameSource)
                && descriptor.ImplementationType == typeof(WindowsGraphicsCaptureFrameSource));
    }

    [Fact]
    public void WindowsGraphicsCaptureFrameSource_UsesWgcAndAvoidsForbiddenCaptureApis()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.Infrastructure",
                "Capture",
                "WindowsGraphicsCaptureFrameSource.cs"));

        Assert.Contains("Direct3D11CaptureFramePool", source, StringComparison.Ordinal);
        Assert.Contains("SoftwareBitmap.CreateCopyFromSurfaceAsync", source, StringComparison.Ordinal);

        var forbiddenApiNames = new[]
        {
            "BitBlt",
            "CopyFromScreen",
            "ReadProcessMemory",
            "WriteProcessMemory",
            "CreateRemoteThread",
            "SetWindowsHookEx",
        };

        foreach (var forbiddenApiName in forbiddenApiNames)
        {
            Assert.DoesNotContain(forbiddenApiName, source, StringComparison.Ordinal);
        }
    }
}

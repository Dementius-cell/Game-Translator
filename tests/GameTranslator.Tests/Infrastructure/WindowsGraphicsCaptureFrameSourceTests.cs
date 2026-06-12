using System.IO;
using GameTranslator.Application.Capture;
using GameTranslator.Infrastructure.Capture;
using GameTranslator.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class WindowsGraphicsCaptureFrameSourceTests
{
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

using System.Net;
using System.Net.Http;
using System.Text;
using System.IO;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class OcrLanguagePackServiceTests
{
    [Fact]
    public async Task CheckAsync_WhenTesseractChineseVerticalDataMissing_ReportsRequiredVertModel()
    {
        using var workspace = new TemporaryDirectory();
        var service = new OcrLanguagePackService(
            new HttpClient(new StaticHttpMessageHandler(Array.Empty<byte>())),
            workspace.Path);

        var status = await service.CheckAsync(
            OcrSettings.TesseractEngineId,
            "zh-CN",
            OcrOrientationMode.Vertical);

        Assert.False(status.IsReady);
        Assert.True(status.CanInstall);
        Assert.Equal("Missing: Tesseract OCR zh-CN Vertical needs chi_sim_vert.traineddata.", status.Message);
    }

    [Fact]
    public async Task CheckAsync_WhenTesseractTraditionalChineseVerticalDataExists_ReportsReady()
    {
        using var workspace = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(workspace.Path, "chi_tra_vert.traineddata"), Array.Empty<byte>());
        var service = new OcrLanguagePackService(
            new HttpClient(new StaticHttpMessageHandler(Array.Empty<byte>())),
            workspace.Path);

        var status = await service.CheckAsync(
            OcrSettings.TesseractEngineId,
            "zh-TW",
            OcrOrientationMode.Vertical);

        Assert.True(status.IsReady);
        Assert.False(status.CanInstall);
        Assert.Equal("Ready: Tesseract OCR zh-TW Vertical uses chi_tra_vert.traineddata.", status.Message);
    }

    [Fact]
    public async Task InstallAsync_WhenTesseractDataMissing_DownloadsTrainedData()
    {
        using var workspace = new TemporaryDirectory();
        var modelBytes = Encoding.UTF8.GetBytes("fake traineddata");
        var handler = new StaticHttpMessageHandler(modelBytes);
        var service = new OcrLanguagePackService(new HttpClient(handler), workspace.Path);

        var result = await service.InstallAsync(
            OcrSettings.TesseractEngineId,
            "ja",
            OcrOrientationMode.Vertical);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(workspace.Path, "jpn_vert.traineddata")));
        Assert.Equal(modelBytes, File.ReadAllBytes(Path.Combine(workspace.Path, "jpn_vert.traineddata")));
        Assert.Equal("Ready: Tesseract OCR ja Vertical uses jpn_vert.traineddata.", result.Message);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/jpn_vert.traineddata", request.RequestUri?.AbsoluteUri, StringComparison.Ordinal);
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] responseBody;

        public StaticHttpMessageHandler(byte[] responseBody)
        {
            this.responseBody = responseBody;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBody),
                RequestMessage = request,
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gt-ocr-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

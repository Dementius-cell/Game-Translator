using System.Windows;
using GameTranslator.Application.DependencyInjection;
using GameTranslator.Application.Ocr;
using GameTranslator.UI.DependencyInjection;
using GameTranslator.UI.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace GameTranslator.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly IHost host;

    public App()
    {
        host = Host.CreateDefaultBuilder()
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.Debug()
                    .WriteTo.File(
                        path: "logs/game_translator_.txt",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddApplicationServices();
                services.AddDefaultProfileStorageOptions();
                services.AddDefaultSettingsStorageOptions();
                services.AddDefaultTranslationCacheStorageOptions();
                services.AddPresentationServices();
                services.AddExternalServiceModules("GameTranslator.Infrastructure");
            })
            .Build();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (PortableOcrSmokeRunner.TryGetReportPath(e.Args, out var portableOcrSmokeReportPath))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = 1;
            try
            {
                host.StartAsync().GetAwaiter().GetResult();
                var ocrService = host.Services.GetRequiredService<OcrService>();
                exitCode = PortableOcrSmokeRunner.Run(ocrService, portableOcrSmokeReportPath);
            }
            catch (Exception exception)
            {
                exitCode = PortableOcrSmokeRunner.WriteStartupFailure(portableOcrSmokeReportPath, exception);
            }

            Shutdown(exitCode);
            return;
        }

        host.StartAsync().GetAwaiter().GetResult();
        Log.Information("GameTranslator application started.");

        MainWindow = host.Services.GetRequiredService<MainWindow>();
        Log.Information("Main window resolved from dependency injection.");
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("GameTranslator application stopped.");
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        finally
        {
            host.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}


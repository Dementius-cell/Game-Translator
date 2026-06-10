using System.Windows;
using GameTranslator.UI.ViewModels;
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
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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


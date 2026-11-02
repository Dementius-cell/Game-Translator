using System.Windows.Threading;
using System.Globalization;
using GameTranslator.Application.Ocr;
using GameTranslator.UI.Commands;
using GameTranslator.UI.Localization;

namespace GameTranslator.UI.ViewModels;

public sealed class DetectorStatusViewModel : ObservableObject, IDisposable
{
    private readonly IDetectorRuntime runtime;
    private readonly DispatcherTimer timer;
    private DetectorRuntimeStatus status = new(DetectorRuntimeState.NotStarted);
    private bool disposed;

    public DetectorStatusViewModel(IDetectorRuntime runtime)
    {
        this.runtime = runtime;
        RetryCommand = new RelayCommand(() => { _ = runtime.InitializeAsync(retry: true); Refresh(); }, () => CanRetry);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += OnTick;
    }

    public RelayCommand RetryCommand { get; }
    public bool CanRetry => status.State == DetectorRuntimeState.Unavailable;
    public bool IsPreparing => status.State == DetectorRuntimeState.Initializing;
    public bool IsReady => status.State == DetectorRuntimeState.Ready;
    public string Message => string.Format(CultureInfo.CurrentCulture, UiText.Get(status.State switch
    {
        DetectorRuntimeState.Ready => "OcrRuntimeReadyDuration",
        DetectorRuntimeState.Initializing when status.Stage == DetectorRuntimeStage.LoadingLibraries => "OcrRuntimeLoadingLibraries",
        DetectorRuntimeState.Initializing when status.Stage == DetectorRuntimeStage.LoadingModel => "OcrRuntimeLoadingModel",
        DetectorRuntimeState.Initializing => "OcrRuntimeStarting",
        DetectorRuntimeState.Unavailable when status.Failure == DetectorRuntimeFailure.CudaUnavailable => "OcrRuntimeCudaUnavailable",
        DetectorRuntimeState.Unavailable when status.Failure == DetectorRuntimeFailure.RuntimeMissing => "OcrRuntimeMissing",
        DetectorRuntimeState.Unavailable => "OcrRuntimeFailed",
        _ => "OcrRuntimeNotStarted",
    }), status.Elapsed.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture));

    public void Start()
    {
        if (disposed) return;
        _ = runtime.InitializeAsync();
        Refresh();
        timer.Start();
    }

    public void Refresh()
    {
        var current = runtime.Status;
        if (disposed || status == current) return;
        status = current;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsPreparing));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(CanRetry));
        RetryCommand.RaiseCanExecuteChanged();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();
    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
    }
}

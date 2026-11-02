using System.IO;
using System.Runtime.Loader;
using System.Windows.Input;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Smoke;

[Collection(WpfResourceCollection.Name)]
public sealed class DetectorStatusViewModelTests
{
    [Fact]
    public void StatusAndRetry_FollowReadinessWithoutWaitingOnInitialization()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(RepositoryRoot.Find(), "src/GameTranslator.UI/bin",
                    AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug", "net9.0-windows10.0.19041.0/GameTranslator.UI.dll"));
                var type = assembly.GetType("GameTranslator.UI.ViewModels.DetectorStatusViewModel", true)!;
                var runtime = new FakeRuntime();
                using var vm = (IDisposable)Activator.CreateInstance(type, runtime)!;
                void Refresh() => type.GetMethod("Refresh")!.Invoke(vm, null);
                object? Get(string name) => type.GetProperty(name)!.GetValue(vm);
                type.GetMethod("Start")!.Invoke(vm, null);
                Assert.Equal(1, runtime.Starts);
                Assert.Equal(true, Get("IsPreparing"));
                Assert.Equal(false, Get("CanRetry"));
                Assert.False(runtime.Pending.Task.IsCompleted);
                var preparing = Get("Message");
                runtime.Status = new(DetectorRuntimeState.Initializing, Stage: DetectorRuntimeStage.LoadingLibraries, Elapsed: TimeSpan.FromSeconds(12));
                Refresh();
                Assert.NotEqual(preparing, Get("Message"));
                Assert.Contains("12", (string)Get("Message")!);
                runtime.Status = new(DetectorRuntimeState.Initializing, Stage: DetectorRuntimeStage.LoadingModel, Elapsed: TimeSpan.FromSeconds(20));
                Refresh();
                Assert.Contains("20", (string)Get("Message")!);
                runtime.Status = new(DetectorRuntimeState.Unavailable, DetectorRuntimeFailure.CudaUnavailable);
                Refresh();
                Assert.Equal(true, Get("CanRetry"));
                Assert.Contains("NVIDIA", (string)Get("Message")!);
                ((ICommand)Get("RetryCommand")!).Execute(null);
                Assert.True(runtime.Retried);
                Assert.Equal(false, Get("CanRetry"));
                runtime.Status = new(DetectorRuntimeState.Ready, Elapsed: TimeSpan.FromSeconds(33));
                Refresh();
                Assert.Contains("33", (string)Get("Message")!);
                Assert.Equal(true, Get("IsReady"));
                Assert.NotEqual(preparing, Get("Message"));
                Assert.Equal(false, Get("IsPreparing"));
                Assert.Equal(false, Get("CanRetry"));
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    private sealed class FakeRuntime : IDetectorRuntime
    {
        public DetectorRuntimeStatus Status { get; set; } = new(DetectorRuntimeState.NotStarted);
        public TaskCompletionSource Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts { get; private set; }
        public bool Retried { get; private set; }
        public Task InitializeAsync(bool retry = false)
        {
            Starts++;
            Retried = retry;
            Status = new(DetectorRuntimeState.Initializing);
            return Pending.Task;
        }
        public Task StopAsync() => Task.CompletedTask;
    }
}

using GameTranslator.Application.Abstractions;

namespace GameTranslator.UI.ViewModels;

public sealed class ApplicationInfoViewModel(IApplicationInfoService service, IApplicationLogger logger) : ObservableObject
{
    public const string ProjectUrl = "https://github.com/Dementius-cell/Game-Translator";
    private string adapters = "Определение видеоадаптеров…";
    public string Version => service.Version;
    public string Adapters { get => adapters; private set => SetProperty(ref adapters, value); }

    public async Task LoadAsync()
    {
        try
        {
            var devices = await service.GetDisplayAdaptersAsync();
            Adapters = devices.Count == 0 ? "Windows не сообщил о доступных видеоадаптерах." : string.Join(Environment.NewLine, devices);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Could not enumerate display adapters for application info.");
            Adapters = "Не удалось получить список видеоадаптеров. Закройте Info и попробуйте снова.";
        }
    }
}

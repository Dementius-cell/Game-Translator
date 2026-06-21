namespace GameTranslator.Application.Updates;

public sealed class ApplicationUpdateOptions
{
    public const string DefaultUpdateSource = "https://github.com/Dementius-cell/Game-Translator/releases/latest/download";

    public ApplicationUpdateOptions(
        string? updateSource = DefaultUpdateSource,
        bool checkOnStartup = true)
    {
        UpdateSource = string.IsNullOrWhiteSpace(updateSource)
            ? string.Empty
            : updateSource.Trim();
        CheckOnStartup = checkOnStartup;
    }

    public string UpdateSource { get; }

    public bool CheckOnStartup { get; }
}

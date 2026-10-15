namespace GameTranslator.Application.Abstractions;

public interface IApplicationInfoService
{
    string Version { get; }
    Task<IReadOnlyList<string>> GetDisplayAdaptersAsync();
}

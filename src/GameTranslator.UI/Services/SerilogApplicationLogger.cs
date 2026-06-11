using GameTranslator.Application.Abstractions;
using Serilog;

namespace GameTranslator.UI.Services;

public sealed class SerilogApplicationLogger : IApplicationLogger
{
    public void Information(string message)
    {
        Log.Information("{Message}", message);
    }

    public void Warning(string message)
    {
        Log.Warning("{Message}", message);
    }

    public void Error(Exception exception, string message)
    {
        Log.Error(exception, "{Message}", message);
    }
}

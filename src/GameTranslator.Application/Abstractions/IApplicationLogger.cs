namespace GameTranslator.Application.Abstractions;

public interface IApplicationLogger
{
    void Information(string message);

    void Warning(string message);

    void Error(Exception exception, string message);
}

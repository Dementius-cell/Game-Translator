using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Application.Composition;

public interface IApplicationServiceModule
{
    void RegisterServices(IServiceCollection services);
}

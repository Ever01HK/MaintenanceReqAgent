using MaintenanceAgent.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MaintenanceAgent.Api.Tests.Integration;

public class GeminiFailureTestFactory : IntegrationTestFactory
{
    private readonly bool _failFirstCall;

    public GeminiFailureTestFactory(bool failFirstCall)
    {
        _failFirstCall = failFirstCall;
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<GeminiAgentService>()
                .ConfigurePrimaryHttpMessageHandler(
                    () => new FailingGeminiHandler(_failFirstCall));
        });
    }
}
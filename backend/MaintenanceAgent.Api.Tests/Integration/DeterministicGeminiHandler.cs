using System.Net;
using System.Text;
using System.Text.Json;

namespace MaintenanceAgent.Api.Tests.Integration;

public class DeterministicGeminiHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!
            .ReadAsStringAsync(cancellationToken);

        var isSecondCall = body.Contains("\"functionResponse\"");

        object responseBody;

        if (isSecondCall)
        {
            responseBody = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new object[]
                            {
                                new
                                {
                                    text = "Your maintenance request is awaiting approval."
                                }
                            }
                        }
                    }
                }
            };
        }
        else
        {
            responseBody = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new object[]
                            {
                                new
                                {
                                    functionCall = new
                                    {
                                        name = "CreateMaintenanceRequest",
                                        args = new
                                        {
                                            description = "Water leaking",
                                            category = "Plumbing",
                                            priority = "Normal"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(responseBody),
                Encoding.UTF8,
                "application/json")
        };
    }
}

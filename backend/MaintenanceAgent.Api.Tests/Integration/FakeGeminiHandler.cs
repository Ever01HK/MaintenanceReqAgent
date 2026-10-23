using System.Net;
using System.Text;
using System.Text.Json;

namespace MaintenanceAgent.Api.Tests.Integration;

public class FakeGeminiHandler : HttpMessageHandler
{
    private int _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);

        object responseBody;

        if (callNumber % 2 == 1)
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
                                    text = "Your maintenance request is awaiting approval."
                                }
                            }
                        }
                    }
                }
            };
        }

        var json = JsonSerializer.Serialize(responseBody);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        });
    }
}
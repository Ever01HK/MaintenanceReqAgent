using System.Net;
using System.Text;
using System.Text.Json;

namespace MaintenanceAgent.Api.Tests.Integration;

public class FailingGeminiHandler : HttpMessageHandler
{
    private readonly bool _failFirstCall;

    public FailingGeminiHandler(bool failFirstCall)
    {
        _failFirstCall = failFirstCall;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!
            .ReadAsStringAsync(cancellationToken);

        bool isSecondCall = body.Contains("\"functionResponse\"");

        if ((_failFirstCall && !isSecondCall) ||
            (!_failFirstCall && isSecondCall))
        {
            return new HttpResponseMessage(
                HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    "Gemini service unavailable")
            };
        }

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
                                    text = "Request awaiting approval."
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
                                            priority = "High"
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
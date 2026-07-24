using System.Net;

namespace RefereeStats.Core.Tests;

/// <summary>Handler HTTP falso que devuelve contenido fijo, sin red real.</summary>
public sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content),
        };
        return Task.FromResult(response);
    }
}

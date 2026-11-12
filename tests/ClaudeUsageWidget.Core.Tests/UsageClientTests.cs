using System.Net;
using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.Core.Tests;

public class UsageClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static (UsageClient Client, StubHandler Handler) Make(HttpStatusCode code, string body = "{}", TimeSpan? retryAfter = null)
    {
        var handler = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(code) { Content = new StringContent(body) };
            if (retryAfter is { } ra)
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(ra);
            return r;
        });
        return (new UsageClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Sends_bearer_token_and_oauth_beta_header()
    {
        var (client, handler) = Make(HttpStatusCode.OK, """{"five_hour":null}""");

        await client.FetchAsync("tok", Now, CancellationToken.None);

        Assert.Equal("https://api.anthropic.com/api/oauth/usage", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer tok", handler.LastRequest.Headers.Authorization!.ToString());
        Assert.Contains("oauth-2025-04-20", handler.LastRequest.Headers.GetValues("anthropic-beta"));
    }

    [Fact]
    public async Task Ok_response_returns_snapshot()
    {
        var (client, _) = Make(HttpStatusCode.OK, """{"five_hour":{"utilization":42,"resets_at":"2026-11-12T19:00:00Z"}}""");

        var result = await client.FetchAsync("tok", Now, CancellationToken.None);

        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(42, result.Snapshot!.FiveHour!.Utilization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Auth_errors_report_unauthorized(HttpStatusCode code)
    {
        var (client, _) = Make(code);

        Assert.Equal(FetchStatus.Unauthorized, (await client.FetchAsync("tok", Now, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Too_many_requests_reports_retry_after()
    {
        var (client, _) = Make(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMinutes(7));

        var result = await client.FetchAsync("tok", Now, CancellationToken.None);

        Assert.Equal(FetchStatus.RateLimited, result.Status);
        Assert.Equal(TimeSpan.FromMinutes(7), result.RetryAfter);
    }

    [Fact]
    public async Task Server_error_reports_failed()
    {
        var (client, _) = Make(HttpStatusCode.InternalServerError);

        Assert.Equal(FetchStatus.Failed, (await client.FetchAsync("tok", Now, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Unparseable_body_reports_failed()
    {
        var (client, _) = Make(HttpStatusCode.OK, "<html>");

        Assert.Equal(FetchStatus.Failed, (await client.FetchAsync("tok", Now, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Network_error_reports_failed()
    {
        var client = new UsageClient(new HttpClient(new StubHandler(_ => throw new HttpRequestException("down"))));

        Assert.Equal(FetchStatus.Failed, (await client.FetchAsync("tok", Now, CancellationToken.None)).Status);
    }
}

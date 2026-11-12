using System.Net;
using System.Net.Http.Headers;

namespace ClaudeUsageWidget.Core;

public enum FetchStatus
{
    Ok,
    Unauthorized,
    RateLimited,
    Failed,
}

public sealed record FetchResult(FetchStatus Status, UsageSnapshot? Snapshot = null, TimeSpan? RetryAfter = null, string? Error = null);

/// <summary>
/// Calls the undocumented endpoint Claude Code uses for /usage. It may change without notice,
/// so every failure is reported as a status instead of an exception.
/// </summary>
public sealed class UsageClient
{
    public const string Endpoint = "https://api.anthropic.com/api/oauth/usage";
    public const string BetaHeader = "oauth-2025-04-20";

    private readonly HttpClient _http;

    public UsageClient(HttpClient http) => _http = http;

    public async Task<FetchResult> FetchAsync(string accessToken, DateTimeOffset now, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.UserAgent.ParseAdd("ClaudeUsageWidget/0.1");

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return new FetchResult(FetchStatus.Unauthorized, Error: $"HTTP {(int)response.StatusCode}");
                case HttpStatusCode.TooManyRequests:
                    return new FetchResult(FetchStatus.RateLimited, RetryAfter: response.Headers.RetryAfter?.Delta,
                        Error: "HTTP 429");
            }

            if (!response.IsSuccessStatusCode)
                return new FetchResult(FetchStatus.Failed, Error: $"HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new FetchResult(FetchStatus.Ok, UsageParser.Parse(body, now));
        }
        catch (FormatException ex)
        {
            return new FetchResult(FetchStatus.Failed, Error: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new FetchResult(FetchStatus.Failed, Error: ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new FetchResult(FetchStatus.Failed, Error: "Request timed out");
        }
    }
}

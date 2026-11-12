using ClaudeUsageWidget.Core;

namespace ClaudeUsageWidget.Core.Tests;

public class CredentialsReaderTests
{
    [Fact]
    public void Reads_access_token_and_expiry()
    {
        const string json = """
            {"claudeAiOauth":{"accessToken":"tok-123","refreshToken":"r","expiresAt":1794526270671,"subscriptionType":"max"}}
            """;

        var creds = CredentialsReader.Parse(json);

        Assert.Equal("tok-123", creds.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794526270671), creds.ExpiresAt);
    }

    [Fact]
    public void Missing_oauth_section_throws()
    {
        Assert.Throws<FormatException>(() => CredentialsReader.Parse("""{"other":{}}"""));
    }

    [Fact]
    public void Missing_token_throws()
    {
        Assert.Throws<FormatException>(() => CredentialsReader.Parse("""{"claudeAiOauth":{"expiresAt":1}}"""));
    }

    [Fact]
    public void Expiry_is_optional()
    {
        var creds = CredentialsReader.Parse("""{"claudeAiOauth":{"accessToken":"t"}}""");

        Assert.Null(creds.ExpiresAt);
    }

    [Fact]
    public void Default_path_uses_claude_config_dir_when_set()
    {
        var path = CredentialsReader.ResolvePath(configDir: @"D:\cfg", userProfile: @"C:\Users\me");

        Assert.Equal(Path.Combine(@"D:\cfg", ".credentials.json"), path);
    }

    [Fact]
    public void Default_path_falls_back_to_user_profile()
    {
        var path = CredentialsReader.ResolvePath(configDir: null, userProfile: @"C:\Users\me");

        Assert.Equal(Path.Combine(@"C:\Users\me", ".claude", ".credentials.json"), path);
    }
}

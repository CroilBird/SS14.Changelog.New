using Microsoft.Extensions.Configuration;

namespace Changelog;

public sealed class Config
{
    public static Config Instance = new();

    [ConfigurationKeyName("REPO")]
    public string? Repo { get; set; }
    [ConfigurationKeyName("BRANCH")]
    public string? Branch { get; set; }
    [ConfigurationKeyName("CHANGELOG_REPO_PATH")]
    public string? ChangelogRepoPath { get; set; }
    [ConfigurationKeyName("EXTRA_CATEGORIES")]
    public string? ExtraCategories { get; set; }
    [ConfigurationKeyName("GITHUB_TOKEN")]
    public string? GithubToken { get; set; }
    [ConfigurationKeyName("DISCORD_WEBHOOK")]
    public string? DiscordWebHook { get; set; }

    public Config()
    {
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddEnvFile(".env")
            .Build()
            .Bind(this);
    }
}
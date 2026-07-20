using Microsoft.Extensions.Configuration;

namespace Changelog;

/// <summary>
/// Class containing configuration. This is taken from the env variables or a .env file in the working directory
/// </summary>
public sealed class Config
{
    public static Config Instance = new();

    /// <summary>
    /// The repository to use
    /// </summary>
    [ConfigurationKeyName("REPO")]
    public string? Repo { get; set; }

    /// <summary>
    /// The branch to use as a base when gathering PRs. should probably be master or stable
    /// </summary>
    [ConfigurationKeyName("BRANCH")]
    public string? Branch { get; set; }

    /// <summary>
    /// the relative path to the changelog directory. should probably be Resources/Changelog
    /// </summary>
    [ConfigurationKeyName("CHANGELOG_REPO_PATH")]
    public string? ChangelogRepoPath { get; set; }

    /// <summary>
    /// The extra categories to scan. E.g. for wizden there is Admin, Maps and Rules
    /// </summary>
    [ConfigurationKeyName("EXTRA_CATEGORIES")]
    public string? ExtraCategories { get; set; }

    /// <summary>
    /// The github PAT to use. Should have content.read
    /// </summary>
    [ConfigurationKeyName("GITHUB_TOKEN")]
    public string? GithubToken { get; set; }

    /// <summary>
    /// The discord webhook to use in sending changelog diffs
    /// </summary>
    [ConfigurationKeyName("DISCORD_WEBHOOK")]
    public string? DiscordWebHook { get; set; }

    /// <summary>
    /// Maximum number of pages to go through in the graphQL. if you exceed this it means you have not updated the
    /// changelog in months.
    /// </summary>
    [ConfigurationKeyName("MAX_GRAPQHL_PAGES")]
    public int MaxPages { get; set; } = 50;

    public Config()
    {
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddEnvFile(".env", optional: true)
            .Build()
            .Bind(this);
    }
}

using System.Collections.Immutable;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Changelog;

public static class PR
{
    // Regexes are all copied from the old code
    // https://github.com/space-wizards/SS14.Changelog/blob/83831f3cf8d1b6e49432b4a45f5aa3c6e3f5fc2c/SS14.Changelog/Controllers/WebhookController.cs#L23
    private static readonly Regex IsChangelogFileRegex = new Regex(@"^Resources/Changelog/Parts/.*\.yml$");

    private static readonly Regex ChangelogHeaderRegex =
        new Regex(@"^\s*(?::cl:|🆑) *([a-z0-9_\- ,&]+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex ChangelogEntryRegex =
        new Regex(@"^ *[*-]? *(add|remove|tweak|fix|bug|bugfix): *([^\n\r]+)\r?$", RegexOptions.IgnoreCase);

    private static readonly Regex ChangelogCategoryRegex =
        new Regex(@"^\s*([a-z]+):\s*$", RegexOptions.IgnoreCase);

    private static readonly Regex CommentRegex = new(@"(?<!\\)<!--([^>]+)(?<!\\)-->");

    private static readonly HttpClient Client = new();

    public const string MainCategory = "Main";

    private const string GithubApiBase = "https://api.github.com/repos";
    private const string GithubRawDownloadBase = "https://raw.githubusercontent.com";

    private const int MaxPages = 10;

    /// <summary>
    /// Parse a PR body as returned by github and return a more terse changelog data
    /// This function was copied pretty much entirely from here:
    /// https://github.com/space-wizards/SS14.Changelog/blob/83831f3cf8d1b6e49432b4a45f5aa3c6e3f5fc2c/SS14.Changelog/Controllers/WebhookController.cs#L161
    /// </summary>
    /// <param name="pr"></param>
    /// <param name="extraCategories"></param>
    /// <returns></returns>
    public static ChangelogData? ParsePRBody(GHPullRequest pr, List<string> extraCategories)
    {
        var allCategories = new HashSet<string>
        {
            MainCategory,
        };
        allCategories.UnionWith(extraCategories);

        var body = CommentRegex.Replace(pr.Body!, "");
        var match = ChangelogHeaderRegex.Match(body);
        if (!match.Success)
            return null;

        var author = match.Groups[1].Success ? match.Groups[1].Value.Trim() : pr.User.Login;
        var changelogBody = body.Substring(match.Index + match.Length);

        var currentCategory = MainCategory;
        var entries = new List<(string, ChangelogData.Change)>();

        var reader = new StringReader(changelogBody);
        while (reader.ReadLine() is { } line)
        {
            var categoryMatch = ChangelogCategoryRegex.Match(line);
            if (categoryMatch.Success)
            {
                // Changelog category directive.
                // Check if it's actually a defined category, skip it otherwise.
                var categoryName = categoryMatch.Groups[1].Value;

                var correctedName = categoryName.ToUpperInvariant() switch
                {
                    "ADMIN" => "Admin",
                    "MAPS" => "Maps",
                    "RULES" => "Rules",
                    _ => MainCategory,
                };

                if (allCategories.TryGetValue(correctedName, out var matchedCategory))
                    currentCategory = matchedCategory;

                continue;
            }

            var entryMatch = ChangelogEntryRegex.Match(line);
            if (!entryMatch.Success)
                continue;

            var type = entryMatch.Groups[1].Value.ToLowerInvariant() switch
            {
                "add" => ChangelogData.ChangeType.Add,
                "remove" => ChangelogData.ChangeType.Remove,
                "fix" or "bugfix" or "bug" => ChangelogData.ChangeType.Fix,
                "tweak" => ChangelogData.ChangeType.Tweak,
                _ => (ChangelogData.ChangeType?) null,
            };

            var message = entryMatch.Groups[2].Value.Trim();

            if (type is { } t)
                entries.Add((currentCategory, new ChangelogData.Change(t, message)));
        }

        var finalCategories = entries
            .GroupBy(e => e.Item1)
            .Select(g => new ChangelogData.CategoryData(g.Key, g.Select(e => e.Item2).ToImmutableArray()))
            .ToImmutableArray();

        return new ChangelogData(author, finalCategories, pr.MergedAt ?? DateTimeOffset.Now)
        {
            Number = pr.Number,
            HtmlUrl = pr.Html_url
        };
    }

    public static List<ChangelogData> ParseAllPRBodies(IEnumerable<GHPullRequest> pullRequests, List<string>? extraCategories = null)
    {
        List<ChangelogData> changelog = [];
        foreach (var pullRequest in pullRequests)
        {
            var changelogData = ParsePRBody(pullRequest, extraCategories ?? []);

            if (changelogData is null)
                continue;

            changelog.Add(changelogData);
        }

        return changelog;
    }

    public static List<GHPullRequest> GetDiff(DateTimeOffset since, string repo, string branch, string? authToken)
    {
        List<GHPullRequest> pullRequests = [];
        var page = 0;

        while (page < MaxPages)
        {
            page++;

            Console.WriteLine($"Crawling page {page} of {repo}/{branch} pull requests");

            var commitApiURL = $"{GithubApiBase}/{repo}/pulls?state=closed&base={branch}&page={page}&per_page=50";

            var request = new HttpRequestMessage(HttpMethod.Get, commitApiURL);

            request.Headers.Add("User-Agent", repo);
            request.Headers.Add("X-Github-Api-Version", "2026-03-10");
            request.Headers.Add("Accept", "application/vnd.github+json");

            if (authToken is not null)
                request.Headers.Add("Authorization", $"Bearer {authToken}");

            var response = Client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Github got mad: {response.Content.ReadAsStringAsync().Result}");
            }

            var options = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            };
            var pulls = JsonSerializer.Deserialize<List<GHPullRequest>>(response.Content.ReadAsStream(), options);

            if (pulls is null)
            {
                throw new Exception("Could not deserialize response json");
            }

            foreach (var pull in pulls)
            {
                if (pull.MergedAt is null)
                    continue;

                if (pull.Body is null)
                {
                    Console.WriteLine($"PR {pull.Number} has a null body");
                    continue;
                }

                if (pull.MergedAt <= since)
                    return pullRequests;

                pullRequests.Add(pull);
            }

            // we do a little rate limiting
            if (authToken is null)
                Thread.Sleep(1000);
        }

        Console.WriteLine("We went so far back we stopped crawling. The generated changelog will have to do.");

        // order PRs by time ascending
        pullRequests = pullRequests.OrderBy(item => item.MergedAt!.Value).Reverse().ToList();

        return pullRequests;
    }

    private static DateTimeOffset GetLastMergedChangelogEntry(YamlMappingNode changelog)
    {
        var lastMergeTime = DateTimeOffset.MinValue;
        
        var entries = (YamlSequenceNode)changelog.Children[new YamlScalarNode("Entries")];
        foreach (var entry in entries)
        {
            if (entry is not YamlMappingNode mappingNode)
                continue;

            var id = int.Parse((string)mappingNode.Children[new YamlScalarNode("id")]);
            var timeNodeKey = new YamlScalarNode("time");

            if (!mappingNode.Children.TryGetValue(timeNodeKey, out var timeValue))
                continue;

            var timeString = (string)timeValue;

            var prMergeTime = DateTimeOffset.Parse(timeString);

            if (prMergeTime <= lastMergeTime)
                continue;

            lastMergeTime = prMergeTime;
        }

        return lastMergeTime;
    }

    /// <summary>
    /// Get the number of the last PR that was included in the changelog
    /// </summary>
    /// <param name="changelogDir">Directory that contains the Changelog.yml and specific changelog files</param>
    /// <param name="extraCategories">Names of extra categories to parse, e.g. Admin, Maps, Rules</param>
    /// <returns></returns>
    public static DateTimeOffset GetLastMergedTimeFromChangelogs(string changelogDir, List<string>? extraCategories = null)
    {
        // parse the current yamls
        var allCategories = new HashSet<string>
        {
            "Changelog",
        };
        
        if (extraCategories is not null)
            allCategories.UnionWith(extraCategories);
        
        var lastMergedTime = DateTimeOffset.MinValue;

        foreach (var category in allCategories)
        {
            var fileName = Path.Combine(
                changelogDir,
                $"{category}.yml"
            );

            // Yamldotnet's deserialization is, for lack of a better term, pure ass.
            // I suspect this is the reason why part of the changelog generation stuff was in python
            // because python's libraries actually do work.
            // If I try to deserialize the yaml stream in any way into a proper object,
            // it simply refuses to work. I'll make a class like
            // public class Root {
            //     public string Fuck;
            // }
            // and deserialize the following yml:
            // Fuck: shit
            // and Yamldotnet, in its infinite wisdom, will insist that
            // System.Runtime.Serialization.SerializationException: Property 'Fuck' not found on type 'Root'.
            // complete garbage.

            // so instead we do a bunch of bullshit

            using var reader = new StreamReader(fileName);
            var yamlStream = new YamlStream();
            yamlStream.Load(reader);

            var changelog = (YamlMappingNode)yamlStream.Documents[0].RootNode;

            var categoryLastMergedTime = GetLastMergedChangelogEntry(changelog);

            if (lastMergedTime < categoryLastMergedTime)
            {
                lastMergedTime = categoryLastMergedTime;
            }
        }

        Console.WriteLine($"Last PR time: {lastMergedTime}");

        return lastMergedTime;
    }


    public static DateTimeOffset GetLastMergedFromRef(string sinceRefSha, List<string> extraCategories)
    {
        var lastMergedTime = DateTimeOffset.MinValue;

        List<string> allCategories = ["Changelog"];
        allCategories.AddRange(extraCategories);

        foreach (var category in allCategories)
        {
            var refChangelogUrl =
                $"{GithubRawDownloadBase}/{Config.Instance.Repo}/{sinceRefSha}/{Config.Instance.ChangelogRepoPath}/{category}.yml";

            HttpRequestMessage request = new(HttpMethod.Get, refChangelogUrl);
            
            if (Config.Instance.GithubToken is not null)
                request.Headers.Add("Authorization", $"Bearer {Config.Instance.GithubToken}");

            var response = Client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Could not get changelog content: " + response.Content.ReadAsStringAsync().Result);
            }
            
            using var reader = new StreamReader(response.Content.ReadAsStream());
            var yamlStream = new YamlStream();
            yamlStream.Load(reader);

            var changelog = (YamlMappingNode)yamlStream.Documents[0].RootNode;
            
            var categoryLastMergedTime = GetLastMergedChangelogEntry(changelog);

            if (lastMergedTime < categoryLastMergedTime)
            {
                lastMergedTime = categoryLastMergedTime;
            }
        }
    
        return lastMergedTime;
    }
}

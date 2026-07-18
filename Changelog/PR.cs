using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using YamlDotNet.RepresentationModel;

namespace Changelog;

/// <summary>
/// PR helper functions. seeks out last merges, collects PRs, generates changelog objects
/// </summary>
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

    private const string GithubGraphQLApiBase = "https://api.github.com/graphql";
    private const string GithubRawDownloadBase = "https://raw.githubusercontent.com";

    /// <summary>
    /// Get the last merge time in a changelog yaml mapping node
    /// </summary>
    /// <param name="changelog">YAML mapping node containing changelog entries</param>
    /// <returns>Time of last PR with a changelog that was merged</returns>
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
    /// Get the time at which the last PR was merged in the given changelogs under the changelogDir
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


    /// <summary>
    /// Get the time at which the last PR with a changelog was merged from a specific git reference
    /// </summary>
    /// <param name="sinceRefSha"></param>
    /// <param name="extraCategories"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static DateTimeOffset GetLastMergedFromRef(string sinceRefSha, List<string> extraCategories)
    {
        var lastMergedTime = DateTimeOffset.MinValue;

        List<string> allCategories = ["Changelog"];
        allCategories.AddRange(extraCategories);

        foreach (var category in allCategories)
        {
            // get the category's YAML at a specific ref
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
            
            // read the file YML
            using var reader = new StreamReader(response.Content.ReadAsStream());
            var yamlStream = new YamlStream();
            yamlStream.Load(reader);

            var changelog = (YamlMappingNode)yamlStream.Documents[0].RootNode;
            
            // Get the last merged time found in this file
            var categoryLastMergedTime = GetLastMergedChangelogEntry(changelog);

            if (lastMergedTime < categoryLastMergedTime)
            {
                lastMergedTime = categoryLastMergedTime;
            }
        }
    
        return lastMergedTime;
    }

    /// <summary>
    /// Returns a list of github pull request objects that have a body and were merged into `<paramref name="branch"/>` after `<paramref name="lastMergeTime"/>`
    /// </summary>
    /// <param name="lastMergeTime">The last merged PR. this will NOT be included in the diff</param>
    /// <param name="repo">The repository to look at</param>
    /// <param name="branch">The branch serving as a base on which PRs were merged. Probably should be mastger</param>
    /// <param name="authToken">Optional auth token if you don't want to get rate limited. This should probably not be optional</param>
    /// <returns></returns>
    public static List<GHPullRequest> GetDiff(DateTimeOffset lastMergeTime, string repo, string branch, string? authToken)
    {
        List<GHPullRequest> pullRequests = [];
        
        
        // Github allows you to filter by merged after a certain date, but it only accepts the date part
        var date = lastMergeTime.ToString("yyyy-MM-dd");

        var page = 0;
        string? afterCursor = null;

        while (page < Config.Instance.MaxPages) {
            // yes I know graphql-dotnet has variables and placeholders. no they aren't documented correctly or very well
            // no I am not going to spend more time trying to guess how they should be used. it can go kick rocks.
            // string interpolation it is
            var query = $$"""
                          {
                            search(first: 50, query: "is:pr repo:{{repo}} base:{{branch}} is:merged merged:>={{date}}", type: ISSUE, after: {{ '"' + afterCursor + '"' ?? "null"}}) {
                              edges {
                                node {
                                  ... on PullRequest {
                                    merged
                                    body
                                    user: author {
                                      login
                                    }
                                    mergedAt
                                    base: baseRef {
                                      ref: name
                                    }
                                    number
                                    html_url: url
                                  }
                                }
                              }
                              pageInfo {
                                hasNextPage
                                endCursor
                              }
                            }
                          }
                          """;


            var graphQL = new GraphQLHttpClient(
                GithubGraphQLApiBase,
                new SystemTextJsonSerializer()
            );


            if (authToken is not null)
                graphQL.HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");

            var graphQLRequest = new GraphQLRequest(query);

            var response = graphQL.SendQueryAsync<GraphQLResponse>(graphQLRequest).Result;

            foreach (var edge in response.Data.Search.Edges)
            {
                if (edge.Node.MergedAt <= lastMergeTime)
                    continue;

                pullRequests.Add(edge.Node);
            }

            if (!response.Data.Search.PageInfo.HasNextPage)
                break;

            afterCursor = response.Data.Search.PageInfo.EndCursor;
        }
        
        // at the end of this we have a collection of PRs with bodies
        
        // order PRs by time ascending
        pullRequests = pullRequests.OrderBy(item => item.MergedAt!.Value).ToList();
        
        return pullRequests;
    }

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
}

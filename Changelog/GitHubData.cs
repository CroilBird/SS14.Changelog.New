using System.Collections.Immutable;
using System.Text.Json.Serialization;

// this is largely taken from the old code here 
// https://github.com/space-wizards/SS14.Changelog/blob/83831f3cf8d1b6e49432b4a45f5aa3c6e3f5fc2c/SS14.Changelog/GitHubData.cs
namespace Changelog
{
    public sealed record GraphQLResponse(GrapQLSearchResponse Search);

    public sealed record GrapQLSearchResponse(List<GraphQLEdge> Edges, GraphQLPageInfo PageInfo);

    public sealed record GraphQLPageInfo(bool HasNextPage, string EndCursor);

    public sealed record GraphQLEdge(GHPullRequest Node);
    
    public sealed record GHPullRequest(
        bool Merged,
        string Body,
        GHUser User,
        DateTimeOffset? MergedAt,
        GHPullRequestBase Base,
        int Number,
        string Html_url);

    public sealed record GHPullRequestBase(string Ref);

    public sealed record GHUser(string Login);

    public sealed record GHPushEvent(ImmutableArray<GHPushedCommit> Commits, string Ref);

    public sealed record GHPushedCommit(ImmutableArray<string> Added, ImmutableArray<string> Modified);
}
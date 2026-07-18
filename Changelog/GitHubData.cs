using System.Collections.Immutable;
using System.Text.Json.Serialization;

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
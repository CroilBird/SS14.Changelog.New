using System.Collections.Immutable;

namespace Changelog
{
    /// <summary>
    /// Contains the changelog data relevant for our changelog .ymls
    /// This is taken from https://github.com/space-wizards/SS14.Changelog/blob/master/SS14.Changelog/ChangelogData.cs
    /// </summary>
    public sealed record ChangelogData
    {
        public const string MainCategory = "Main";

        public ChangelogData(string author, ImmutableArray<CategoryData> categories, DateTimeOffset time)
        {
            Author = author;
            Categories = categories;
            Time = time;
        }

        public string Author { get; }
        public ImmutableArray<CategoryData> Categories { get; }
        public DateTimeOffset Time { get; }
        public int Number { get; init; }
        public required string HtmlUrl { get; init; }

        public sealed record CategoryData(string Category, ImmutableArray<Change> Changes);

        public record struct Change(ChangeType Type, string Message);

        public enum ChangeType
        {
            Add,
            Remove,
            Fix,
            Tweak
        }
    }
}

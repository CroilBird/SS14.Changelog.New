using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Changelog;

/// <summary>
/// File IO for yml and markdown stuff
/// </summary>
public static class IO
{
    /// <summary>
    /// How many changelog entries should be in a yaml. older ones are pruned
    /// </summary>
    public static int MaxChangelogEntries = 500;

    /// <summary>
    /// Emojis associated with a change type
    /// </summary>
    public static Dictionary<ChangelogData.ChangeType, string> Emojis = new()
    {
        { ChangelogData.ChangeType.Add, "🆕" },
        { ChangelogData.ChangeType.Fix, "🐛" },
        { ChangelogData.ChangeType.Remove, "❌" },
        { ChangelogData.ChangeType.Tweak, "⚒️" },
    };

    /// <summary>
    /// Convert a yaml scalar (text) node to a single-quoted string
    /// </summary>
    private static YamlScalarNode SingleQuoted(string content)
    {
        return new YamlScalarNode(content) { Style = ScalarStyle.SingleQuoted };
    }

    /// <summary>
    /// Convert a yaml scalar (text) node to a double-quoted string
    /// </summary>
    private static YamlScalarNode DoubleQuoted(string content)
    {
        return new YamlScalarNode(content) { Style = ScalarStyle.DoubleQuoted };
    }

    /// <summary>
    /// Dumps a changelog to markdown. This only sends "main" category changelogs, so no admin or map or whatever.
    /// </summary>
    /// <param name="changelogMarkdownPath">Path of the markdown file</param>
    /// <param name="changelogParts">Changelog parts to include</param>
    public static void DumpChangelogToMarkdown(string changelogMarkdownPath, IEnumerable<ChangelogData> changelogParts)
    {
        using var writer = new StreamWriter(changelogMarkdownPath);

        // each changelog part corresponds to a PR with a valid changelog body
        foreach (var changelogPart in changelogParts)
        {
            // each category corresponds to a category in a PR which may have multiple changes to that category
            foreach (var category in changelogPart.Categories)
            {
                // we will only send main category ones though. the reason the other ones are included here is because
                // the code that gets the last change needs all of them. don't worry about it
                if (category.Category != PR.MainCategory)
                    continue;

                writer.WriteLine($"**{changelogPart.Author}** updated:");
                foreach (var change in category.Changes)
                {
                    var emoji = Emojis[change.Type];
                    writer.WriteLine($"{emoji} - {change.Message} ([#{changelogPart.Number}]({changelogPart.HtmlUrl}))");
                }

                writer.WriteLine();
            }
        }
    }


    /// <summary>
    /// Update the YML changelogs from a changelog part.
    /// A changelog part may include multiple categories
    /// </summary>
    private static void UpdateChangelogFromPart(ChangelogData changelogPart, string changelogDir)
    {
        // go through all the categories. these contain the actual changes
        foreach (var category in changelogPart.Categories)
        {
            // grab the correct category file
            var categoryFile = category.Category == PR.MainCategory ? "Changelog" : category.Category;

            var changelogYmlPath = Path.Combine(
                changelogDir,
                $"{categoryFile}.yml"
            );

            Console.WriteLine($"Writing changelog part {changelogYmlPath}");

            // load the entire yaml
            var yamlStream = new YamlStream();

            using var reader = new StreamReader(changelogYmlPath);
            yamlStream.Load(reader);

            // this is just yamldotnet shenanigans. I can't figure out how to deserialize this into an object properly
            // using yamldotnet so we are doing it this way.
            // here's the root node
            var changelog = (YamlMappingNode)yamlStream.Documents[0].RootNode;

            // the entries node contains all the actual changelog entries
            var entries = (YamlSequenceNode)changelog.Children[new YamlScalarNode("Entries")];

            // now we have our full set of entries. get the last ID so we can increment it on the next one

            var lastEntry = (YamlMappingNode)entries.Last();
            var lastEntryId = int.Parse((string)lastEntry.Children[new YamlScalarNode("id")]);

            // now make a new entry with the incremented ID

            var yamlMapping = new YamlMappingNode
            {
                {"author", changelogPart.Author},
                {"time", SingleQuoted(changelogPart.Time.ToString("O"))},
                {"url", changelogPart.HtmlUrl},
                {
                    "changes",
                    new YamlSequenceNode(
                        category.Changes.Select(c => new YamlMappingNode
                        {
                            {"type", c.Type.ToString()},
                            {"message", c.Message},
                        }))
                },
                {"id", (lastEntryId + 1).ToString() },
            };

            if (category.Category != ChangelogData.MainCategory)
                yamlMapping.Add("category", category.Category);


            entries.Add(yamlMapping);

            // if you think the above stinks, so do I. this is the way it is because I cannot nicely serialize and
            // deserialize between yaml and C# objects. if you know how to do it replace this and god help me

            // trim old entries. yes this also stinks
            if (entries.Count() > MaxChangelogEntries)
            {
                while (entries.Count() - MaxChangelogEntries > 0)
                {
                    entries.Children.RemoveAt(0);
                }
            }

            // done pruning old entries, write everything.
            using var writer = new StreamWriter(changelogYmlPath);
            yamlStream.Save(writer);
        }
    }

    /// <summary>
    /// Helper function to update YML changelogs from a list of changelog parts.
    /// </summary>
    public static void UpdateChangelogs(IEnumerable<ChangelogData> changelogParts, string changelogDir)
    {
        foreach (var changelogPart in changelogParts)
        {
            UpdateChangelogFromPart(changelogPart, changelogDir);
        }
    }
}

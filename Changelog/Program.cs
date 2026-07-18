using System.CommandLine;

namespace Changelog
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            // options
            Option<string> changelogDirOption = new("--changelog-dir", "-d")
            {
                Description = "Path to the changelog directory",
                Required = true,
            };
            
            Option<string> sinceRefShaOption = new("--sha", "-s")
            {
                Description = "Specific ref sha to compare changes to. Good chance this should be the github.event.pull_request.base.sha workflow env",
                Required = true,
            };
            
            Option<string> discordWebhookUrlOption = new("--discord-webhook-url", "-u")
            {
                Description = "URL for the discord webhook",
                Required = true,
            };

            Option<string> changelogMarkdownPathOption = new("--changelog-md-path", "-c")
            {
                Description = "Path where the changelog markdown file is located. This will be sent to the discord webhook. Won't generate if not included.",
                Required = true,
            };
            
            
            RootCommand rootCommand = new("Changelog generator for SS14");
            
            // Update changelog subcommand
            Command updateCommand = new("update", "Updates the changelog.yml files in resources");

            updateCommand.Options.Add(changelogDirOption);

            updateCommand.SetAction(parseResult => Generate(
                parseResult.GetValue(changelogDirOption)!
            ));
            rootCommand.Subcommands.Add(updateCommand);

            // generate diff subcommand
            Command dumpCommand = new("dump-diff", "Dumps a diff to a markdown file, for later sending to discord or hosting on CDN");

            dumpCommand.Options.Add(sinceRefShaOption);
            dumpCommand.Options.Add(changelogMarkdownPathOption);

            dumpCommand.SetAction(parseResult => DumpDiffToMarkdown(
                parseResult.GetValue(sinceRefShaOption)!,
                parseResult.GetValue(changelogMarkdownPathOption)!
            ));
            rootCommand.Subcommands.Add(dumpCommand);

            // Send webhook subcommand
            Command sendWebhookCommand = new("send-webhook", "Send changelog markdown file to a discord webhook");


            sendWebhookCommand.Options.Add(discordWebhookUrlOption);
            sendWebhookCommand.Options.Add(changelogMarkdownPathOption);

            sendWebhookCommand.SetAction(parseResult => SendDiscordWebhook(
                parseResult.GetValue(discordWebhookUrlOption)!,
                parseResult.GetValue(changelogMarkdownPathOption)!
            ));

            rootCommand.Subcommands.Add(sendWebhookCommand);

            return rootCommand.Parse(args).Invoke();
        }

        /// <summary>
        /// Generates new changelogs
        /// </summary>
        /// <param name="changelogDir"></param>
        private static int Generate(
            string changelogDir
        )
        {
            if (Config.Instance.Repo is null)
                throw new Exception("Repository not set");

            if (Config.Instance.Branch is null)
                throw new Exception("Branch is not set");

            if (Config.Instance.GithubToken is not null)
                Console.WriteLine("Using github token");

            List<string> extraCategories = [];
            if (Config.Instance.ExtraCategories is not null)
                extraCategories.AddRange(Config.Instance.ExtraCategories.Split(','));

            // Get the last merged PR time
            var lastMergedTime = PR.GetLastMergedTimeFromChangelogs(changelogDir, extraCategories);
            
            Console.WriteLine($"Generating diff from {lastMergedTime}");

            // Get the list of PRs that were merged since last time.
            var diff = PR.GetDiff(lastMergedTime, Config.Instance.Repo, Config.Instance.Branch, Config.Instance.GithubToken);

            Console.WriteLine($"Collected {diff.Count} pull requests");

            // Generate a new YMLfest out of this
            var changelogs = PR.ParseAllPRBodies(diff, extraCategories);

            if (changelogs.Count == 0)
            {
                Console.WriteLine("Nothing to do");
                return 0;
            }
            
            Console.WriteLine($"Generated {changelogs.Count} changelogs");

            // Add these parts to the actual changelog and trim older entries
            IO.UpdateChangelogs(changelogs, changelogDir);

            return 0;
        }

        private static int DumpDiffToMarkdown(
            string sinceRefSha,
            string changelogMarkdownPath
        )
        {
            if (Config.Instance.Repo is null)
                throw new Exception("Repository not set");

            if (Config.Instance.Branch is null)
                throw new Exception("Branch is not set");

            if (Config.Instance.GithubToken is not null)
                Console.WriteLine("Using github token");

            List<string> extraCategories = [];
            if (Config.Instance.ExtraCategories is not null)
                extraCategories.AddRange(Config.Instance.ExtraCategories.Split(','));

            // Get the last merged PR time
            var lastMergedTime = PR.GetLastMergedFromRef(sinceRefSha, extraCategories);
            
            Console.WriteLine($"Generating diff from {lastMergedTime}");

            // Get the list of PRs that were merged since last time.
            var diff = PR.GetDiff(lastMergedTime, Config.Instance.Repo, Config.Instance.Branch, Config.Instance.GithubToken);

            Console.WriteLine($"Collected {diff.Count} pull requests");
            
            // Generate a new YMLfest out of this
            var changelogs = PR.ParseAllPRBodies(diff, extraCategories);

            if (changelogs.Count == 0)
            {
                Console.WriteLine("Nothing to do");
                return 0;
            }
            
            Console.WriteLine($"Generated {changelogs.Count} changelogs");
            
            IO.DumpChangelogToMarkdown(changelogMarkdownPath, changelogs);

            return 0;
        }

        private static int SendDiscordWebhook(string discordWebhookUrl, string? changelogMarkdownPath)
        {
            if (changelogMarkdownPath is null)
            {
                Console.WriteLine();
                return 1;
            }

            using var reader = new StreamReader(changelogMarkdownPath);

            if (!DiscordWebhook.SendDiffInParts(discordWebhookUrl, reader))
                return 1;

            return 0;
        }
    }
}

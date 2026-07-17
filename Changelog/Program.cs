// See https://aka.ms/new-console-template for more information

using System.CommandLine;

namespace Changelog
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            RootCommand rootCommand = new("Changelog generator for SS14");


            // Update changelog subcommand
            Command updateCommand = new("update-changelogs", "Updates the changelog.yml files in resources");

            Option<string> changelogDirOption = new("--changelog-dir", "-d")
            {
                Description = "Path to the changelog directory",
                Required = true,
            };
            Option<string> repoOption = new("--repo", "-r")
            {
                Description = "Repository to use. in the form owner/repo. E.g. space-wizards/space-station-14",
                Required = true,
            };
            Option<string> branchOption = new("--branch", "-b")
            {
                Description = "Branch to look at for updates. Should probably be master.",
                Required = true,
            };
            Option<List<string>> extraCategories = new("--extra-categories", "-e")
            {
                Description = "Comma-separated list of extra categories",
                CustomParser = parseResult =>
                {
                    var parts = parseResult.Tokens.Single().Value.Split(",");
                    return parts.ToList();
                },
            };

            Option<string?> githubToken = new("--github-token", "-t")
            {
                Description = "Optional github token. Requests are limited to 60 per hour if you don't add one, so like.. you should add one.",
            };

            updateCommand.Options.Add(changelogDirOption);
            updateCommand.Options.Add(repoOption);
            updateCommand.Options.Add(branchOption);
            updateCommand.Options.Add(extraCategories);
            updateCommand.Options.Add(githubToken);

            updateCommand.SetAction(parseResult => Generate(
                parseResult.GetValue(changelogDirOption)!,
                parseResult.GetValue(repoOption)!,
                parseResult.GetValue(branchOption)!,
                parseResult.GetValue(extraCategories)!,
                parseResult.GetValue(githubToken)
            ));
            rootCommand.Subcommands.Add(updateCommand);

            // generate diff subcommand
            // TODO



            // Send webhook subcommand

            Command sendWebhookCommand = new("send-webhook", "Send changelog markdown file to a discord webhook");

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
        /// <param name="repo"></param>
        /// <param name="branch"></param>
        /// <param name="extraCategories"></param>
        /// <param name="githubToken"></param>
        private static int Generate(
            string changelogDir,
            string repo,
            string branch,
            List<string> extraCategories,
            string? githubToken = null
        )
        {
            if (githubToken is not null)
                Console.WriteLine("Using github token");

            // Get the last merged PR time
            var since = PR.GetLastMergedTimeOffset(changelogDir, extraCategories);

            // Get the list of PRs that were merged since last time.
            var diff = PR.GetDiff(since, repo, branch, githubToken);

            Console.WriteLine($"Collected {diff.Count} pull requests");

            // Generate a new YMLfest out of this
            var changelogs = PR.ParseAllPRBodies(diff, extraCategories);

            // // Write the actual changelog to .YML parts
            // IO.WriteChangelogParts(changelogs, changelogDir);

            // Add these parts to the actual changelog and trim older entries
            IO.UpdateChangelogs(changelogs, changelogDir);

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

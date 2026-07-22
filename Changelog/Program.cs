using System.CommandLine;

namespace Changelog
{
    internal static class Program
    {
        private enum MergeTimeMethod
        {
            LastRefYaml,
            RefInfo
        }

        private const string DefaultMergeTimeMethod = "lastrefyaml";

        static int Main(string[] args)
        {
            // create options for the command line
            Option<string> changelogDirOption = new("--changelog-dir", "-d")
            {
                Description = "Path to the changelog directory",
                Required = true,
            };

            Option<string> sinceRefShaOption = new("--ref", "-r")
            {
                Description = "Specific ref sha or branch to compare changes to. Good chance this should be the github.event.pull_request.base.sha workflow env",
                Required = true,
            };

            Option<string> mergeTimeMethodOption = new("--merge-time-method", "-m")
            {
                Description = """
                              Method to use to determine the last merge time to use.
                              Options:
                              - lastrefyaml | refyaml - Use the last time recorded in changelog .yml files on the specific ref passed by --sha/-s, on the repo specified in the configuration
                              - ref | refinfo - Use the time of the last commit to the given ref which is passed using --ref or -r
                              """,
                DefaultValueFactory = _ => DefaultMergeTimeMethod
            };

            // changelogMarkdownPathOption will function both for dump-diff and discord-webhook, as output and input respectively
            Option<string> changelogMarkdownPathOption = new("--changelog-md-path", "-c")
            {
                Description = "Path where the changelog markdown file is located. This will be sent to the discord webhook.",
                Required = true,
            };

            // Create the root command and subcommands for the command line
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
            dumpCommand.Options.Add(mergeTimeMethodOption);
            dumpCommand.Options.Add(changelogMarkdownPathOption);

            dumpCommand.SetAction(parseResult =>
            {
                var mergeTimeMethodString = parseResult.GetValue(mergeTimeMethodOption)!;
                var mergeTimeMethod = mergeTimeMethodString switch
                {
                    "lastrefyaml" or "refyaml" => MergeTimeMethod.LastRefYaml,
                    "ref" or "refinfo" => MergeTimeMethod.RefInfo,
                    _ => MergeTimeMethod.LastRefYaml
                };

                DumpDiffToMarkdown(
                    parseResult.GetValue(sinceRefShaOption)!,
                    mergeTimeMethod,
                    parseResult.GetValue(changelogMarkdownPathOption)!
                );
            });
            rootCommand.Subcommands.Add(dumpCommand);

            // Send webhook subcommand
            Command sendWebhookCommand = new("send-webhook", "Send changelog markdown file to a discord webhook");


            sendWebhookCommand.Options.Add(changelogMarkdownPathOption);

            sendWebhookCommand.SetAction(parseResult => SendDiscordWebhook(
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
            // mandatory configuration checks
            if (Config.Instance.Repo is null)
                throw new Exception("Repository is not set in environment or could not be read from .env in working dir");

            if (Config.Instance.Branch is null)
                throw new Exception("Branch is not set in environment or could not be read from .env in working dir");

            if (Config.Instance.GithubToken is null)
                throw new Exception("Github token is not set in environment or could not be read from .env in working dir");

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
            string sinceRef,
            MergeTimeMethod mergeTimeMethod,
            string changelogMarkdownPath
        )
        {
            if (Config.Instance.Repo is null)
                throw new Exception("Repository is not set in environment or could not be read from .env in working dir");

            if (Config.Instance.Branch is null)
                throw new Exception("Branch is is not set in environment or could not be read from .env in working dir");

            if (Config.Instance.GithubToken is null)
                throw new Exception("Github token is not set in environment or could not be read from .env in working dir");

            List<string> extraCategories = [];
            if (Config.Instance.ExtraCategories is not null)
                extraCategories.AddRange(Config.Instance.ExtraCategories.Split(','));

            // Get the last merged PR time
            DateTimeOffset lastMergedTime;
            switch (mergeTimeMethod)
            {
                case MergeTimeMethod.LastRefYaml:
                    lastMergedTime = PR.GetLastMergedFromRefYaml(sinceRef, extraCategories);
                    break;
                case MergeTimeMethod.RefInfo:
                    lastMergedTime = PR.GetDateFromGitRefInfo(sinceRef);
                    break;
                default:
                    throw new Exception($"Invalid merge time method: {mergeTimeMethod}");
            }

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

        private static int SendDiscordWebhook(string changelogMarkdownPath)
        {
            if (Config.Instance.DiscordWebHook is null)
                throw new Exception("Discord webhook is not set in environment or could not be read from .env in working dir");

            using var reader = new StreamReader(changelogMarkdownPath);

            if (!DiscordWebhook.SendDiffInParts(Config.Instance.DiscordWebHook, reader))
                return 1;

            return 0;
        }
    }
}

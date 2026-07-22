# SS14 changelog

This is a program that generates changelogs for SS14. Parts of it were copied from the old one:

https://github.com/space-wizards/SS14.Changelog

## Setup

Configure this by setting env variables or having a .env file in whatever place you run this in.

```
REPO=space-wizards/space-station-14
BRANCH=master
CHANGELOG_REPO_PATH=Resources/Changelog
EXTRA_CATEGORIES=Admin,Maps,Rules
#GITHUB_TOKEN=optional sort of but you should really be using this. you can probably use the workflow token at GITHUB_TOKEN or whatever
DISCORD_WEBHOOK=url to discord webhook for posting changelogs to a channel
```

The workflow file included in this repository runs on a merge queue, but it can be adapted to run at some other point in
your CI/CD pipeline.

The workflow expects the following variables:

```
CHANGELOG_REPO # set to space-wizards/space-station-14 for upstream
CHANGELOG_BRANCH # set to master for test releases
CHANGELOG_EXTRA_CATEGORIES # set to Admin,Maps,Rules for upstream
```


It does the following:

### Update the changelog YMLs

```
Description:
  Updates the changelog.yml files in resources

Usage:
  Changelog update [options]

Options:
  -d, --changelog-dir <changelog-dir> (REQUIRED)  Path to the changelog directory
  -?, -h, --help                                  Show help and usage information
```

Takes the last date it can find in the changelog .yml files, then crawls the repository and branch specified in the
config for merged PRs. It then works these into the changelog .yml files.

This is intended for the master branch whenever a test publish is triggered but before the build is done and published

### Generate a diff markdown file

```
Description:                                                                                                                                                                                                                                                                                                                                                    
  Dumps a diff to a markdown file, for later sending to discord or hosting on CDN

Usage:
  Changelog dump-diff [options]

Options:
  -r, --ref <ref> (REQUIRED)                              Specific ref sha or branch to compare changes to. Good chance this should be the github.event.pull_request.base.sha workflow env
  -m, --merge-time-method <merge-time-method>             Method to use to determine the last merge time to use.
                                                          Options:
                                                          - lastrefyaml | refyaml - Use the last time recorded in changelog .yml files on the specific ref passed by --sha/-s, on the repo specified in the configuration
                                                          - ref | refinfo - Use the time of the last commit to the given ref which is passed using --ref or -r [default: lastrefyaml]
  -c, --changelog-md-path <changelog-md-path> (REQUIRED)  Path where the changelog markdown file is located. This will be sent to the discord webhook.
  -?, -h, --help                                          Show help and usage information
```

Takes the last date it can find in the changelog .yml files **at the specified `--ref` ref**, then crawls the repository
and branch specified in the config for merged PRs. It then works these into a human readable markdown file that is meant
for people eyes specified at `--changelog-md-path`

the ref should probably be the commit of the last stable publish

This can be used to send it to a discord webhook or upload to a cdn or whatever

### Send the contents of a diff markdown to a discord webhook

```
Description:
  Send changelog markdown file to a discord webhook

Usage:
  Changelog send-webhook [options]

Options:
  -c, --changelog-md-path <changelog-md-path> (REQUIRED)      Path where the changelog markdown file is located. This will be sent to the discord webhook. Won't generate if not included.
  -?, -h, --help                                              Show help and usage information
```

Reads the content of the input `--changelog-md-path` and sends the parts of it (split by discords max character limit of 2000)
to the webhook specified by `--discord-webhook-url`


# how this should be used probably

### During publish testing:

Workflow: checkout code -> `dotnet run --project Changelog -- update -d /path/to/Resources/Changelog` -> commit -> build -> publish testing build

### During publish stable

Workflow: get last publish ref -> run `dotnet run --project Changelog -- dump-diff -s [ref] -c diff.md` -> build -> publish -> if all goes well, `dotnet run --project Changelog -- send-webhook -c diff.md`


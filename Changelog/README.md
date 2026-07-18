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
#GITHUB_TOKEN=optional
DISCORD_WEBHOOK=url to discord webhook for posting changelogs to a channel
```


It does the following:

### Update the changelog yms

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

This is intended for the master branch whenever a PR is merged.

### Generate a diff markdown file

```
Description:                                                                                                                                                                                                                                                                                                                                                    
  Dumps a diff to a markdown file, for later sending to discord or hosting on CDN

Usage:
  Changelog dump-diff [options]

Options:
  -s, --sha <sha> (REQUIRED)                              Specific ref sha to compare changes to. Good chance this should be the github.event.pull_request.base.sha workflow env
  -c, --changelog-md-path <changelog-md-path> (REQUIRED)  Path where the changelog markdown file is located. This will be sent to the discord webhook. Won't generate if not included.
  -?, -h, --help                                          Show help and usage information
```

Takes the last date it can find in the changelog .yml files **at the specified `--sha` ref**, then crawls the repository
and branch specified in the config for merged PRs. It then works these into a human readable markdown file that is meant
for people eyes specified at `--changelog-md-path`

This can be used to send it to a discord webhook or upload to a cdn or whatever

### Send the contents of a diff markdown to a discord webhook

```
Description:                                                                                                                                                                                                                                                                                                                                                    
  Send changelog markdown file to a discord webhook

Usage:
  Changelog send-webhook [options]

Options:
  -u, --discord-webhook-url <discord-webhook-url> (REQUIRED)  URL for the discord webhook
  -c, --changelog-md-path <changelog-md-path> (REQUIRED)      Path where the changelog markdown file is located. This will be sent to the discord webhook. Won't generate if not included.
  -?, -h, --help                                              Show help and usage information
```

Reads the content of the input `--changelog-md-path` and sends the parts of it (split by discords max character limit of 2000)
to the webhook specified by `--discord-webhook-url` 


# ratcache

ratcache is a simple, open source pastebin. Paste text or code, get a short link to share, and read what other people have shared. You can use it from the web or from a `ratcache` command in your terminal.

- Paste plain text or code, from a web form or a file or pipe
- Share any paste with a unique URL
- Syntax highlighting for 15 languages, with line numbers
- Optional expiry (10 minutes to never) and public, unlisted or private pastes
- Every paste is encrypted at rest
- Optional account, so you can list, edit and delete your own pastes
- Dark theme

> **Status:** early development. The web app and CLI work locally. Hosted deployment isn't set up yet, so for now you run your own copy.

## Contents

1. [What you need](#what-you-need)
2. [Run the server](#run-the-server)
3. [Install the `ratcache` command](#install-the-ratcache-command)
4. [Connect the CLI to your account](#connect-the-cli-to-your-account)
5. [Using the CLI](#using-the-cli)
6. [Admins](#admins)
7. [Development](#development)
8. [Contributing](#contributing)

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (to run the server)
- Python 3.10 or newer (for the CLI; it has no other dependencies)
- Git
- [pipx](https://pipx.pypa.io/) (recommended for installing the CLI)

Check with `dotnet --list-sdks` and `python3 --version`.

## Run the server

```bash
git clone https://github.com/mkmcgrath/ratcache.git
cd ratcache

# One time: create the key that encrypts pastes. It's stored in your user
# profile, not in the repo. Don't lose it: pastes can't be read without it.
dotnet user-secrets set RatCache:EncryptionKey "$(openssl rand -base64 32)" --project src/RatCache.Web

dotnet run --project src/RatCache.Web --launch-profile http
```

Open <http://localhost:5097>. The database (`src/RatCache.Web/ratcache.db`) is created on first run. Delete it to start over.

Leave this terminal running and open a second one for the CLI.

## Install the `ratcache` command

From the repo root:

```bash
pipx install ./cli
```

That gives you a plain `ratcache` command that works from any directory. Check it:

```bash
ratcache --version
ratcache ping            # should print: http://localhost:5097: ok
```

**If `ratcache: command not found`:** run `pipx ensurepath`, then open a new terminal.

**If you don't have pipx:** use a virtual environment instead.

```bash
python3 -m venv ~/.venvs/ratcache
~/.venvs/ratcache/bin/pip install ./cli
# then either run ~/.venvs/ratcache/bin/ratcache, or link it onto your PATH:
mkdir -p ~/.local/bin && ln -s ~/.venvs/ratcache/bin/ratcache ~/.local/bin/ratcache
```

(On Windows the script is `.venvs\ratcache\Scripts\ratcache.exe`.)

Avoid a bare `pip install ./cli`. Recent Debian, Ubuntu and Fedora refuse it ("externally-managed-environment") to protect the system Python.

**Trying it without installing:** `cd cli && python3 -m ratcache ping` runs the same code.

To update after pulling new code, run `pipx install --force ./cli`. To remove it, run `pipx uninstall ratcache`.

## Connect the CLI to your account

You can paste without an account, but those pastes are anonymous and can't be listed or edited. To tie pastes to you:

**1. Create an account** on the website: open <http://localhost:5097/account> and use the register form. You need a username (3 to 32 characters: letters, numbers, `_` and `-`) and a password of at least 12 characters. There is no email.

**2. Sign in from the CLI:**

```bash
ratcache login
# username: yourname
# password:            (typed hidden)
# signed in to http://localhost:5097 as yourname
```

For scripts, you can pass `-u yourname --password-stdin` and pipe the password in.

**3. Check it worked:**

```bash
ratcache list            # your pastes (empty at first)
```

**What login does:** it signs in once, asks the server for an API token named after your computer, saves the token and discards the session. Your password is never saved. The token is in `~/.config/ratcache/config.json` (`%APPDATA%\ratcache\config.json` on Windows), readable only by you. Logging in again replaces the old token.

**Signing out:** `ratcache logout` revokes the token on the server and deletes the saved copy. If the server can't be reached, it keeps the saved token so you can try again. You can also revoke tokens from the account page on the website.

**Using a different server:** the CLI talks to the server you last logged in to. To use another one, set `RATCACHE_URL` and log in again:

```bash
export RATCACHE_URL=https://paste.example.com
ratcache login
```

Without `RATCACHE_URL` or a saved login, it uses `http://localhost:5097`. The CLI only sends your token to the server that issued it.

## Using the CLI

```bash
ratcache notes.txt                          # paste a file, print its link
cat error.log | ratcache --expire 1h        # paste from a pipe
ratcache main.py --visibility unlisted      # language is guessed from .py
ratcache get Ab3dE9fGh1                     # print a paste's text (an ID or a full link works)
ratcache get Ab3dE9fGh1 > saved.txt
ratcache list                               # your pastes
ratcache edit Ab3dE9fGh1 new.txt            # replace the content
ratcache edit Ab3dE9fGh1 --expire never     # or just change options
ratcache delete Ab3dE9fGh1
```

| Command | Does |
|---|---|
| `ratcache [file]` | Create a paste from a file, or from stdin, and print its link |
| `ratcache get <id or link>` | Print a paste's raw text |
| `ratcache login` / `logout` | Sign in and save a token / revoke it and sign out |
| `ratcache list` | List your pastes (needs login) |
| `ratcache edit <id> [file]` | Replace a paste's content (`-` reads stdin) or change its options (needs login) |
| `ratcache delete <id>` | Delete a paste (needs login) |
| `ratcache ping` | Check that the server is reachable |

Options for create and edit:

| Option | Values |
|---|---|
| `--title` | any text (`--title ""` removes it when editing) |
| `--lang` | `text bash c cpp css go html java javascript json markdown python rust sql yaml` |
| `--visibility` | `public`, `unlisted` (link only) or `private` (login needed, only you can see it) |
| `--expire` | `10m`, `1h`, `1d`, `1w`, `1mo`, `never` |

Size limits: 1 MB for anonymous pastes, 5 MB when signed in.

Results (links, text) go to stdout and messages and errors go to stderr, so `ratcache get` and `ratcache file.txt` work in pipes. Exit status: 0 success, 1 failure (including "not signed in"), 2 bad usage.

## Admins

An admin can delete any paste through the API. Admins are set by username, in configuration, at startup:

```bash
dotnet user-secrets set "RatCache:AdminUsernames:0" "yourusername" --project src/RatCache.Web
```

Restart the app to apply it. On a server, set the environment variable `RatCache__AdminUsernames__0` instead. Anyone not listed is a normal user, and changing someone's role signs them out.

## Development

Run the tests from the repo root:

```bash
dotnet test                       # server tests
cd cli && python3 -m unittest     # CLI tests
```

Project layout:

```
src/RatCache.Web/     Blazor web app + API (.NET 10, SQLite via EF Core)
  Components/         pages, layout, shared components
  Controllers/        HTTP API used by the CLI
  Services/           business logic shared by pages and API
  Models/, Data/      database classes and DbContext
tests/RatCache.Tests/ xUnit tests
cli/                  Python command-line client
documentation/        SRS, spec, project skeleton
```

Troubleshooting:

- **Server won't start, "encryption key" error:** you skipped the `user-secrets set` step, or you're running without the launch profile. Use `--launch-profile http`, or set `ASPNETCORE_ENVIRONMENT=Development`.
- **Port 5097 in use:** stop the other copy (`pkill -x RatCache.Web`) or use `--urls http://localhost:PORT` with `--no-launch-profile`, and set `RATCACHE_URL` to match.
- **CLI says "could not reach http://…":** check the server is running with `ratcache ping`, and that `RATCACHE_URL` isn't pointing somewhere else.
- **CLI says your saved login was rejected:** the token was revoked or the database was reset. Run `ratcache login` again.

## Contributing

1. Pick a requirement from the SRS (`documentation/RatCache_SRS.docx`) or the Jira board.
2. Branch off `main` (e.g. `feature/fr-2-6-delete-paste`).
3. Build it, then make sure `dotnet build` has 0 warnings and both test suites pass.
4. Open a pull request.

Before you start, read:

- `RatCache_Starting_Point.docx`: how the project is set up and why
- `documentation/AI_Help/corbin_blzr_starting_point.md`: detailed conventions. Give this to your AI assistant if you want it to help.

Database changes (models, migrations): check with whoever is handling the DB first. Never commit `.db` files or secrets.

## Links

- Jira: <https://ratnest.atlassian.net/jira/software/projects/KAN/boards/2>
- Requirements: `documentation/RatCache_SRS.docx`

## Contributors

Matthew McGrath, Corbin Probasco, Andrew Lawrence, Adam Sheridan

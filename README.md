# ratcache

ratcache is a simple, open source pastebin built as a class project. You can paste text or code, get a shareable link, and view what other people have shared. That's all it does.

## Features

- Create pastes from plain text or code
- Share any paste with a unique URL
- Syntax highlighting for common languages
- Optional expiration times for pastes
- Clean, lightweight interface
- Web interface and a command-line client

## Status

Early development. The app runs, but SRS features are still being built.
Placeholders in the code are tagged with the requirement they're waiting for (e.g. `TODO FR-2.4`).

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Python 3.10 or newer (for the CLI)
- Git

Check with `dotnet --list-sdks` and `python3 --version`.

## Quick start

```bash
git clone git@github.com:mkmcgrath/ratcache.git
cd ratcache
# one time: generate the paste encryption key (stored in your user profile, not the repo)
dotnet user-secrets set RatCache:EncryptionKey "$(openssl rand -base64 32)" --project src/RatCache.Web
dotnet run --project src/RatCache.Web --launch-profile http
```

The database (`src/RatCache.Web/ratcache.db`) is created on first run.

Open <http://localhost:5097>.

## Running the tests

```bash
dotnet test                       # server tests (from the repo root)
cd cli && python3 -m unittest     # CLI tests
```

## Using the CLI

With the server running, in a second terminal:

```bash
cd cli
python3 -m ratcache ping          # check the CLI can reach the server
python3 -m ratcache --help        # list commands
```

Optional: install it as a `ratcache` command with `pip install -e cli`.

To point the CLI at a different server:

```bash
export RATCACHE_URL=https://example.com
```

Planned commands:

| Command | Does |
|---|---|
| `ratcache [file]` | Create a paste from a file, or from stdin (`cat log.txt \| ratcache`) |
| `ratcache get <id>` | Print a paste's raw text |
| `ratcache login` / `logout` | Sign in / out |
| `ratcache list` | List your pastes |
| `ratcache edit <id> [file]` | Change a paste |
| `ratcache delete <id>` | Delete a paste |

Options: `--title`, `--lang`, `--visibility public|unlisted|private`, `--expire 10m|1h|1d|1w|1mo|never`

## Project layout

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

## Contributing

1. Pick a requirement from the SRS (`documentation/RatCache_SRS.docx`) or the Jira board.
2. Branch off `main` (e.g. `feature/fr-2-6-delete-paste`).
3. Build it, then make sure `dotnet build` has 0 warnings and both test suites pass.
4. Open a pull request.

Before you start, read:

- `RatCache_Starting_Point.docx`: how the project is set up and why
- `documentation/AI_Help/corbin_blzr_starting_point.md`: detailed conventions. Give this to your AI assistant if you need guidance or assistance.
Database changes (models, migrations): check with whoever is handling the DB first. Never commit `.db` files or secrets.

## Links

- Jira: <https://ratnest.atlassian.net/jira/software/projects/KAN/boards/2>
- Requirements: `documentation/RatCache_SRS.docx`

## Contributors

Matthew McGrath
Corbin Probasco
Andrew Lawrence
Adam Sheridan

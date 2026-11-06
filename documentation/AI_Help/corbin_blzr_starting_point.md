# corbin_blzr_starting_point

Architecture and build rules for RatCache. This file is written for coding agents and teammates. It covers how the starting point is put together and how to add features so that four people's branches still fit together.

If your change breaks a rule here, either follow the rule or change this file in the same branch and say why. Don't drift silently.

---

## 1. What RatCache is

RatCache is a minimal, open-source pastebin "by developers for developers". It's a class project for Group 4: Matthew McGrath, Corbin Probasco, Andrew Lawrence, and Adam Sheridan.

- **Requirements:** `documentation/RatCache_SRS.docx` is the source of truth. Requirements have IDs like **FR-2.4** and **NFR-3.3**, with MoSCoW priorities. The team works through the SRS a batch at a time, one feature branch per requirement or group of requirements.
- **Status:** the starting point has **no SRS features implemented**. Every placeholder is tagged with the FR it's waiting for.

To read the SRS from a terminal, run `pandoc documentation/RatCache_SRS.docx -t plain`.

## 2. Stack

| Piece | Choice | Version |
|---|---|---|
| Web UI + API server | ASP.NET Core **Blazor Web App** (Razor components) + **API controllers**, one project | .NET **10** (`net10.0`) |
| Database | **SQLite** via **EF Core** | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.x |
| API docs | Built-in OpenAPI (`AddOpenApi`), not Swashbuckle | `Microsoft.AspNetCore.OpenApi` 10.0.x |
| Server tests | **xUnit** + `WebApplicationFactory<Program>` | xunit 2.9, `Microsoft.AspNetCore.Mvc.Testing` 10.0.x |
| CLI | **Python 3.10+**, **standard library only** (argparse, urllib, json) | — |
| Hosting | UpCloud (later). Local-only for now | — |

Don't add a package without a reason that comes from a requirement. Keep the CLI dependency-free unless the team agrees otherwise.

## 3. Layout

```
ratcache/
├ RatCache.sln                    builds src/ + tests/
├ .gitignore                      bin/ obj/ *.db __pycache__ ...
├ RatCache_Starting_Point.docx    2-page overview of this starting point for the team
├ documentation/                  SRS, spec, skeleton tree doc
│ └ AI_Help/                      this file + what_is_this.txt
├ logo/                           rat.png in several sizes
├ src/RatCache.Web/               THE server: pages + API + DB access
│ ├ Program.cs                    all service registration + middleware pipeline
│ ├ appsettings.json              ConnectionStrings:Default = "Data Source=ratcache.db"
│ ├ appsettings.Development.json
│ ├ Properties/launchSettings.json  http://localhost:5097 , https://localhost:7106
│ ├ Components/
│ │ ├ App.razor                   HTML shell (<head>, CSS links, blazor.web.js)
│ │ ├ Routes.razor                router; NotFoundPage = Pages.NotFound
│ │ ├ _Imports.razor              global @using for components
│ │ ├ Layout/MainLayout.razor     <header><NavMenu/></header><main>@Body</main>
│ │ ├ Layout/NavMenu.razor        ratcache · recent · my pastes · account
│ │ ├ Layout/ReconnectModal.*     template file for interactive-server reconnects; leave it
│ │ ├ Pages/                      one .razor file per routable page
│ │ └ Shared/                     reusable components used by 2+ pages
│ ├ Controllers/                  HTTP API for the CLI (and anything else external)
│ ├ Models/                       entity / data classes; Languages.cs (shared language IDs)
│ ├ Data/RatCacheDbContext.cs     EF Core DbContext (no DbSets yet)
│ ├ Services/                     business logic shared by pages and controllers
│ └ wwwroot/app.css               the only global stylesheet
├ tests/RatCache.Tests/           xUnit, references RatCache.Web
│ └ HealthTests.cs                in-memory app tests (the pattern)
└ cli/                            Python CLI
  ├ pyproject.toml                console script `ratcache` -> ratcache.main:main
  ├ ratcache/main.py              argparse; one cmd_* function per command
  ├ ratcache/client.py            HTTP helper; base URL from RATCACHE_URL
  ├ ratcache/__main__.py          enables `python3 -m ratcache`
  └ tests/test_cli.py             unittest
```

Mapping from the original Node-style skeleton. `documentation/ratcacheProjectSkeleton.docx` now shows the .NET tree; if the layout changes, update that file too:

| Original skeleton | .NET equivalent |
|---|---|
| `src/controllers` | `Controllers/` |
| `src/models` | `Models/` + `Data/` |
| `src/services` | `Services/` |
| `src/routes` | `[Route]` attributes on controllers, `@page` on components |
| `src/middleware` | `Program.cs` (create a `Middleware/` folder only when there is custom middleware) |
| `src/views/partials`, `forms`, `errors` | `Components/Layout`, `Components/Shared`, `Components/Pages` (NotFound, Error) |
| `public/css`, `js`, `images` | `wwwroot/` |
| `.env` | `appsettings*.json` + environment variables / user-secrets |
| `package.json` | `.csproj` / `.sln` (+ `cli/pyproject.toml`) |

## 4. Commands

Run these from the repo root unless a line says otherwise.

```bash
dotnet build                                                   # whole solution, expect 0 warnings
dotnet run --project src/RatCache.Web --launch-profile http    # http://localhost:5097
dotnet test                                                    # server tests
cd cli && python3 -m unittest                                  # CLI tests
cd cli && python3 -m ratcache ping                             # CLI -> running server
pip install -e cli                                             # optional: puts `ratcache` on PATH
```

- OpenAPI JSON is served at `http://localhost:5097/openapi/v1.json` in Development only.
- To stop a background server, use `pkill -x RatCache.Web`. **Don't use `pkill -f RatCache.Web`**: it matches the agent's own shell command line, because the paths contain the string, and kills the shell.

**Definition of done for any change:** `dotnet build` with 0 warnings, `dotnet test` green, `python3 -m unittest` green in `cli/`, and the app starts and the touched pages/endpoints respond.

## 5. Architecture

### 5.1 One server, two front doors

```
 browser ──HTTP──> Blazor pages (Components/Pages) ──┐
                                                     ├──> Services/ ──> RatCacheDbContext ──> SQLite
 CLI ─────HTTP──> API controllers (Controllers/) ────┘
```

- **Business logic lives in `Services/`.** That includes creating pastes, encryption, visibility and expiry checks, ID generation, and auth checks. Pages and controllers stay thin and call the same service methods. That's how SRS §2.1 ("every feature works the same way in both") is met.
- **Blazor pages call services directly** by injecting them. They do **not** call the app's own `/api/...` over HttpClient: the pages already run on the server.
- **Controllers exist for the CLI** and other external clients. Each controller action should be a few lines: validate input, call a service, map the result to an HTTP response.
- **Only `Services/` (and `Data/`) touch `RatCacheDbContext`.** Pages and controllers don't query the DB directly.

### 5.2 Rendering mode

The app was generated with `dotnet new blazor --interactivity Server --all-interactive false --empty`.

- **Pages render statically on the server by default.** Each request produces plain HTML. Forms are normal HTML form posts. This is the default because cookie sign-in (NFR-2.5) and antiforgery work simply this way.
- A page that genuinely needs live interactivity (for example, a copy-to-clipboard button, FR-2.9) can add `@rendermode InteractiveServer`. Prefer a tiny interactive **component** inside a static page over making the whole page interactive.
- **Never** switch the app to global interactivity or WebAssembly without a team decision. Auth and forms depend on static rendering.

**Forms on static pages** use `EditForm` with `FormName`, plus `[SupplyParameterFromForm]`:

```razor
<EditForm Model="Input" FormName="new-paste" OnValidSubmit="Create" method="post">
    <DataAnnotationsValidator />
    ...
</EditForm>

@code {
    [SupplyParameterFromForm] private NewPasteInput Input { get; set; } = new();
    private async Task Create() { /* call a service, then NavigationManager.NavigateTo(...) */ }
}
```

Antiforgery tokens are added automatically (`app.UseAntiforgery()` is already in the pipeline). Don't disable antiforgery.

### 5.3 Middleware pipeline (`Program.cs`): order matters

```
UseOpenApi (dev) | UseExceptionHandler("/Error") + UseHsts (prod)
UseWhen(not /api and not /raw) -> UseStatusCodePagesWithReExecute("/not-found")
UseHttpsRedirection
UseRouting          <- must come AFTER the status-code-pages line
UseAntiforgery
MapStaticAssets, MapControllers, MapRazorComponents<App>
```

- The status-code-pages line turns empty 404s for **web** URLs into the `NotFound` page. API routes (`/api/*`) and `/raw/*` are excluded, so they keep real status codes (NFR-4.1).
- **`app.UseRouting()` is explicit on purpose.** Without it after the `UseWhen`, the re-execute can't re-route and unknown URLs return an empty 404. Don't remove it or move it above the `UseWhen`.
- New middleware, such as authentication, authorization, or rate limiting (NFR-3.6), goes **after `UseRouting` and before `UseAntiforgery`**, in this order: `UseAuthentication`, `UseAuthorization`, `UseRateLimiter`.
- If you add another non-HTML route prefix, add it to the `UseWhen` exclusion.
- `public partial class Program { }` at the bottom is required by the test project. Keep it.

### 5.4 Configuration and secrets

- Non-secret settings go in `appsettings.json`.
- **Secrets never go in any committed file.** That includes the AES-256-GCM key (NFR-1.3) and any SMTP password (FR-1.7).
  - Locally, use `dotnet user-secrets` in `src/RatCache.Web`.
  - On the server, use environment variables. ASP.NET maps `RatCache__EncryptionKey` to the config key `RatCache:EncryptionKey`.
  - **Decided:** the key is `RatCache:EncryptionKey`, 32 random bytes as base64. Set it locally with
    `dotnet user-secrets set RatCache:EncryptionKey "$(openssl rand -base64 32)" --project src/RatCache.Web`.
    The app refuses to start without it (`Services/PasteEncryption.cs`). Changing or losing the key makes every existing paste unreadable.
- The SQLite file `ratcache.db` is created in the web project's working directory and is git-ignored. Never commit a `.db` file.

## 6. How to add things

### 6.1 A page

1. Create `Components/Pages/<Name>.razor` with `@page "/route"` and a `<PageTitle>`.
2. Use one `<h1>` per page. `FocusOnNavigate` targets it.
3. Inject services with `@inject PasteService Pastes`. Don't inject `RatCacheDbContext` or `HttpClient`.
4. If it belongs in the nav, add a `<NavLink>` to `Layout/NavMenu.razor`. **The nav is shared: change it in small, separate commits** so merges are easy.
5. Replace the placeholder's `TODO FR-x` comment and "Not implemented yet" text when you implement it.

Existing placeholder routes are the public URLs the SRS (§5.1) and the CLI depend on. **Keep them stable:**

| Route | File | Waiting for |
|---|---|---|
| `/` | `Home.razor` | FR-5.1, FR-2.1 (form exists; submit disabled) |
| `/p/{Id}` | `PasteView.razor` | FR-2.3, FR-4.2, FR-4.4, FR-2.9 |
| `/recent` | `Recent.razor` | FR-3.6 |
| `/my` | `MyPastes.razor` | FR-2.7, FR-2.5, FR-2.6 |
| `/account` | `Account.razor` | FR-1.1, 1.3, 1.5, 1.6, 1.8 (may be split into `/account/*` pages) |
| `/not-found` | `NotFound.razor` | NFR-4.2: shared page for missing, expired, AND private pastes |
| `/Error` | `Error.razor` | NFR-4.3 |
| `/raw/{id}` | `Controllers/RawController.cs` | FR-2.4 (returns 501 now) |

### 6.2 An API endpoint

1. Add or extend a controller in `Controllers/`. Copy `HealthController.cs`: `[ApiController]`, `[Route("api/[controller]")]`, `ControllerBase`.
2. Follow the endpoint table in **SRS §5.2**. Don't invent different paths for those operations:
   `POST /api/pastes`, `GET/PATCH/DELETE /api/pastes/{id}`, `GET /api/pastes`, `GET /api/pastes/recent`, `POST /api/users`, `POST/DELETE /api/sessions`, `POST /api/tokens`, `DELETE /api/tokens/{id}`, `GET /raw/{id}`.
3. JSON in and out, with camelCase property names (ASP.NET default). Use small request/response record types. **Don't return EF entity classes directly.** That would leak fields like owner IDs and encrypted blobs.
4. Auth: web uses a session cookie; the CLI sends `Authorization: Bearer <token>` (SRS §5.2). Both resolve to the same claims (`Services/AuthPrincipal.cs`), so put `[Authorize]` on the action and read the user with `User.GetUserId()` (null when signed out). Don't check which scheme was used.
   - The default scheme `ratcache` forwards to the Bearer handler when an `Authorization: Bearer` header is present, otherwise to the cookie (`Services/AuthenticationSetup.cs`).
   - Unauthenticated `/api` requests get a JSON 401 (`sign_in_required`), never a redirect. A bad or revoked token always gets 401 `invalid_token`, even on endpoints that allow anonymous use.
   - A web page signs someone in with `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthPrincipal.Create(user, CookieAuthenticationDefaults.AuthenticationScheme), new() { IsPersistent = true })` after `AccountService.CheckCredentialsAsync`.
5. Errors (NFR-4.1): services return a `ServiceResult`/`ServiceError` (`Services/ServiceResult.cs`); the controller returns `ApiErrors.From(result.Error!)`, which picks the status from `ErrorKind`. Malformed or missing JSON bodies get the same shape (`invalid_request`). Use standard codes (400, 401, 403, 404, 410, 413, 429, 500) with this body. The shape is the starting-point convention; if you change it, update the CLI and this file:
   ```json
   { "error": "paste_not_found", "message": "That paste doesn't exist or has expired." }
   ```
   `error` is a stable snake_case code that the CLI can switch on. `message` is plain language.
6. Add a test in `tests/RatCache.Tests` (NFR-6.2 wants every endpoint covered).

### 6.3 A service

- Put the class in `Services/`, namespace `RatCache.Web.Services`.
- Register it in `Program.cs` under the `// Register your own services` comment. Use `AddScoped<T>()` for anything that uses the DbContext, and `AddSingleton<T>()` only for stateless helpers such as an ID generator.
- An interface is optional. Add one when you need a fake in tests.
- Services throw or return results. **They don't know about HTTP.** Status codes are the controller's job, and the page decides what to render.

### 6.4 Models, the database, and migrations

- **One teammate owns DB/schema work at a time.** Coordinate before adding or changing entities or migrations, and don't create migrations on two branches at once.
- Entities go in `Models/`, namespace `RatCache.Web.Models`. Register each one as a DbSet in `Data/RatCacheDbContext.cs`:
  ```csharp
  public DbSet<Paste> Pastes => Set<Paste>();
  ```
- Migrations:
  ```bash
  dotnet tool install --global dotnet-ef        # once
  dotnet add src/RatCache.Web package Microsoft.EntityFrameworkCore.Design
  dotnet ef migrations add <Name> --project src/RatCache.Web
  dotnet ef database update --project src/RatCache.Web
  ```
  Migrations go in `src/RatCache.Web/Migrations/` and **are committed**. The `.db` file is not.
- **Decided:** pending migrations are applied at startup with `Database.Migrate()` in `Program.cs`. No manual `dotnet ef database update` is needed; the first run creates `ratcache.db`.
- All `DateTime` columns are UTC. The DbContext marks them `DateTimeKind.Utc` on read; always write `TimeProvider`/`DateTime.UtcNow` values.
- `Paste.TitleCipher`/`ContentCipher` are encrypted. Read and write pastes only through `PasteService`.
- If `dotnet ef` isn't found after installing it, add `~/.dotnet/tools` to your `PATH`.
- Tests must not touch the real `ratcache.db`. Use `RatCacheFactory` (tests/RatCache.Tests): it gives each test class its own in-memory SQLite database and a random encryption key.

### 6.5 A CLI command

- All commands live in `cli/ratcache/main.py`. Each one is a `cmd_<name>(args) -> int` function, wired into `build_parser()` with `set_defaults(func=...)`.
- **Only the first argument is checked for a command name.** If it isn't one, the CLI creates a new paste (SRS §5.3: `ratcache file.txt`, `cat x | ratcache --lang python`). This keeps `ratcache --title list` from running the `list` command. If you add a command, add its name to `COMMANDS`.
- Commands and flags are fixed by **SRS §5.3**: `ratcache [file]`, `get <id>`, `login`, `logout`, `list`, `edit <id> [file]`, `delete <id>`, plus `ping`. Flags: `--title`, `--lang`, `--visibility public|unlisted|private`, `--expire 10m|1h|1d|1w|1mo|never`. The shared flags are defined once in `add_paste_options()`.
- **Exit codes (FR-6.6):** `0` success, `1` error, `2` not implemented or usage error (argparse also uses 2). **Errors go to stderr through `error()`** (NFR-4.5). Only real output goes to stdout, so it can be piped.
- All HTTP goes through `cli/ratcache/client.py`. Extend it with `post_json`, `patch_json`, `delete`, and raw-text GET helpers rather than calling urllib from commands. Raise `ApiError` on failures, and once the server sends the JSON error shape (§6.2.5), surface its `message`.
- Server URL: the `RATCACHE_URL` env var, defaulting to `http://localhost:5097`. The API token (FR-6.4) goes in a config file in the user's home directory. The location and format are up to whoever builds `login`, and it must work on Linux, macOS, and Windows (SRS §2.3); `~/.config/ratcache/config.json` is suggested. Record the decision here.
- Test with `unittest` + `unittest.mock`. Mock `client` functions so tests don't need a running server.

### 6.6 Tests

- **Server:** add classes in `tests/RatCache.Tests/`. Use `IClassFixture<RatCacheFactory>` and real HTTP calls, as in `HealthTests.cs`. Get clients from `factory.NewClient()` (https, own cookie jar) or `await factory.SignedInClientAsync()` (a freshly registered, signed-in user). Passwords are bcrypt cost 12, so each registration costs ~0.25 s; don't create users in loops. Name tests `<Thing>_<Behavior>`.
- **CLI:** add tests to `cli/tests/test_cli.py` or a new `cli/tests/test_*.py`.

### 6.7 Language IDs (shared by web, API, DB, CLI)

A paste's language is stored and sent as a short lowercase ID, never as a display label:

`text` (plain text, the default), `bash`, `c`, `cpp`, `css`, `go`, `html`, `java`, `javascript`, `json`, `markdown`, `python`, `rust`, `sql`, `yaml`

- **Server source of truth:** `src/RatCache.Web/Models/Languages.cs`. `Languages.All` holds (Id, Label) pairs, `Languages.Default` is `"text"`, and `Languages.IsValid(id)` is for server-side validation (NFR-3.5). Labels like "C++" are for display only.
- **CLI copy:** `LANGUAGES` in `cli/ratcache/main.py`, used as the `--lang` choices. A CLI test (`LanguageListTests`) fails if the two lists drift apart.
- To add a language, add it in both places and make sure the highlighting library supports it.

## 7. UI and style rules (anti-goals)

The site is "by developers for developers": **plain, minimal, no design.**

- **No** colors (except red for validation errors), gradients, shadows, rounded cards, icons, web fonts, animations, or CSS frameworks (no Bootstrap, no Tailwind).
- Browser defaults first. `wwwroot/app.css` holds everything global: monospace font, a 960px max width, nav spacing, full-width text inputs. Add rules only when something is unusable without them.
- Component-scoped CSS (`X.razor.css`) is allowed for layout tweaks under the same rules.
- Lowercase UI text, matching the existing nav and pages ("new paste", "my pastes").
- The app must work from 360px to 1920px (FR-5.3). Plain flow layout covers this; don't add fixed widths.
- A dark theme (FR-5.4) is a Could. If it's built, use `prefers-color-scheme` with the same plain approach.
- Syntax highlighting (FR-4.2) uses a third-party library (SRS §2.5). Pick one, load it from `wwwroot/` (not a CDN), and keep its theme plain.

## 8. Security rules that apply to every change

These come from the NFRs. Reviewers should reject code that breaks them.

- **XSS (NFR-3.3):** Razor `@value` escapes HTML automatically. **Never render paste content or titles with `MarkupString` / `(MarkupString)`** or `innerHTML` in JS. `/raw/{id}` must return `text/plain`.
- **SQL injection (NFR-3.4):** use EF Core LINQ. Never build SQL with string concatenation or interpolation. If raw SQL is unavoidable, use `FromSql($"...")` (parameterized), never `FromSqlRaw` with user input.
- **Logging (NFR-1.4):** never log paste content, titles, passwords, tokens, or the encryption key.
- **Not found (NFR-4.2):** missing, expired, and private (not yours) pastes must all produce the identical response: the same NotFound page on the web, and the same 404 body on the API.
- **Errors (NFR-4.3):** no stack traces or exception messages in responses. Production uses `/Error`.
- **Passwords (NFR-2.1/2.2/2.4):** Argon2id or bcrypt (cost ≥ 12), minimum 12 characters, and the same error for a wrong username or a wrong password.
- **Cookies (NFR-2.5):** HttpOnly, Secure, SameSite. Antiforgery stays on.
- **API tokens (NFR-2.6):** store only hashes.
- **Encryption (NFR-1.2/1.3):** encrypt content and title of every paste with AES-256-GCM, using one server-wide key from configuration (§5.4). Encryption and decryption happen in a service, never in pages or controllers.
- **Paste IDs (NFR-5.2):** random, URL-safe, at least 8 characters, from a cryptographic RNG (`RandomNumberGenerator`), never sequential.
- **Server-side validation (NFR-3.5):** validate every field on the server even if the form or CLI already checked it. Size caps (NFR-3.1) are 1 MB anonymous and 5 MB registered.

## 9. Decisions already made (don't relitigate in code)

| Decision | Source |
|---|---|
| .NET 10, Blazor, SQLite, Python CLI, UpCloud | SRS §6, team |
| Pages and API in one project; pages call services directly | starting point |
| Static server rendering by default, per-component interactivity | starting point |
| Controllers, not minimal APIs | starting point (matches the course material) |
| No ASP.NET Identity scaffold in the starting point. **Registration is username + password; no email** (FR-1.1). If FR-1 uses Identity, configure it for usernames | SRS, starting point |
| Email password reset (FR-1.7) is a stretch goal | SRS §6 |
| Every paste is encrypted, anonymous included, with one server-wide key | SRS NFR-1.2/1.3 |
| Anonymous pastes can't be edited; delete only via one-time link (FR-2.8) | SRS |
| Paste URLs `/p/{id}`, raw `/raw/{id}` | starting point |
| Language IDs: `text bash c cpp css go html java javascript json markdown python rust sql yaml` (see §6.7) | starting point |
| Minimal, unstyled UI | team |

## 10. Working rules for agents

- **Don't commit or push** unless the human you're working for asks. Leave changes in the working tree.
- Work on a feature branch off this starting point. Keep each branch to the requirement(s) it names, and reference FR/NFR IDs in code comments where it helps (`// FR-3.5`).
- **Files everyone touches:** `Program.cs`, `NavMenu.razor`, `RatCacheDbContext.cs`, `app.css`, `cli/ratcache/main.py`, and this file. Keep edits there small and additive. Add your registration or route line; don't reorganize.
- **Don't rename or move** existing folders, namespaces (`RatCache.Web.*`, `RatCache.Tests`), routes, CLI commands, or the error JSON shape without updating this file and telling the team.
- Don't edit the `.docx` files under `documentation/` programmatically.
- Before you finish: run the full definition of done (§4).

## 11. Changing the starting point itself

The starting point is allowed to change. Here's what to watch:

- **Render mode:** changing to global interactivity breaks static forms and cookie auth. It's a team decision.
- **Pipeline order:** see §5.3. Re-run the 404 check afterward: `curl -i localhost:5097/nope` must return the NotFound HTML, and `curl -i localhost:5097/api/nope` must return a bare 404.
- **Ports:** `launchSettings.json` fixes 5097/7106, and the CLI default and this file reference 5097. Change all three together.
- **Packages:** keep EF Core, OpenAPI, and Mvc.Testing on the same 10.0.x patch line.
- **Template files deliberately removed:** Bootstrap, Counter/Weather samples, and the sidebar layout. Don't bring them back.
- Update this file in the same branch as any structural change.

## 12. Known gaps / open items

- No README setup guide yet (NFR-6.1).
- Not deployed. UpCloud deployment, HTTPS certificates (NFR-1.1), and Docker are future work.
- Not decided yet: the CLI config file location. Whoever builds `ratcache login` records it here. The CLI login flow is `POST /api/sessions` (keep the cookie), then `POST /api/tokens`; save `token` and `id` (`id` is needed for `DELETE /api/tokens/{id}` on logout).
- Not built yet: sign-in lockout after 5 failures (NFR-2.3), JSON 500 bodies for `/api` in production (`UseExceptionHandler` currently re-executes to the HTML `/Error` page), rate limiting (NFR-3.6), and the expired-paste cleanup job (FR-3.5: expired pastes are already hidden but stay in the DB).
- Paste links in API responses are built from the request's scheme and host. Behind nginx on UpCloud, add `UseForwardedHeaders` so they come out as `https://`.
- SRS loose ends: FR-1.7 needs an email that registration no longer collects, NFR-3.5 still mentions email format, and §2.4 still says the stack is undecided.

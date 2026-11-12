"""Entry point for the `ratcache` command (SRS 3.6, 5.3).

Usage:
    ratcache [file]            create a paste from a file or stdin
    ratcache get <id>          print a paste's raw content
    ratcache login | logout
    ratcache list
    ratcache edit <id> [file]
    ratcache delete <id>
    ratcache ping              check that the server is reachable

Exit status (FR-6.6): 0 success, 1 failure (server error, network, not signed in),
2 bad usage. Errors go to stderr (NFR-4.5); only results go to stdout.
"""

import argparse
import getpass
import os
import re
import socket
import sys
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

from ratcache import __version__, client, config

COMMANDS = {"get", "login", "logout", "list", "edit", "delete", "ping"}

# FR-4.3 language IDs. Keep in sync with src/RatCache.Web/Models/Languages.cs.
LANGUAGES = [
    "text", "bash", "c", "cpp", "css", "go", "html", "java",
    "javascript", "json", "markdown", "python", "rust", "sql", "yaml",
]

# Used for --lang when a file is given without one. Unknown extensions use the server default.
EXTENSIONS = {
    ".sh": "bash", ".bash": "bash", ".c": "c", ".h": "c",
    ".cpp": "cpp", ".cc": "cpp", ".cxx": "cpp", ".hpp": "cpp", ".hh": "cpp",
    ".css": "css", ".go": "go", ".html": "html", ".htm": "html", ".java": "java",
    ".js": "javascript", ".mjs": "javascript", ".cjs": "javascript", ".json": "json",
    ".md": "markdown", ".markdown": "markdown", ".py": "python", ".rs": "rust",
    ".sql": "sql", ".yaml": "yaml", ".yml": "yaml", ".txt": "text", ".log": "text",
}

EXIT_OK = 0
EXIT_ERROR = 1
EXIT_USAGE = 2

PASTE_ID = re.compile(r"[A-Za-z0-9]+")


class UsageError(Exception):
    """Bad arguments or input; exits 2."""


class Failure(Exception):
    """Can't do it in the current state (for example, not signed in); exits 1."""


def error(msg: str) -> None:
    print(f"ratcache: {msg}", file=sys.stderr)


def api_failed(e: client.ApiError) -> int:
    """Print a server/network error in plain language and return the failure exit code."""
    if e.code == "invalid_token":
        error("your saved login was rejected (expired or revoked). Run: ratcache login")
    elif e.code == "sign_in_required":
        error("sign in first: ratcache login")
    else:
        error(str(e))
    return EXIT_ERROR


# ---- helpers ---------------------------------------------------------------------------

def parse_paste_ref(ref: str) -> tuple[str, str | None]:
    """Accept an ID or a link (http://host/p/ID, .../p/ID/edit, .../raw/ID).

    Returns (id, server) where server is the link's origin, or None for a bare ID.
    """
    ref = ref.strip()
    if PASTE_ID.fullmatch(ref):
        return ref, None
    parts = urlsplit(ref)
    segs = [s for s in parts.path.split("/") if s]
    if parts.scheme in ("http", "https") and parts.netloc and len(segs) >= 2 \
            and segs[0] in ("p", "raw") and PASTE_ID.fullmatch(segs[1]):
        return segs[1], f"{parts.scheme}://{parts.netloc}"
    raise UsageError(f"not a paste ID or link: {ref}")


def read_content(file: str | None) -> str:
    """Read a file, or stdin when file is None or "-". Pastes must be UTF-8 text."""
    if file is None or file == "-":
        if sys.stdin.isatty():
            raise UsageError("nothing to paste: give a file, or pipe text in (cat x | ratcache)")
        data = sys.stdin.buffer.read()
        name = "standard input"
    else:
        try:
            data = Path(file).read_bytes()
        except OSError as e:
            raise UsageError(f"can't read {file}: {e.strerror}") from None
        name = file
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        raise UsageError(f"{name} isn't UTF-8 text") from None


def guess_language(file: str | None) -> str | None:
    if not file or file == "-":
        return None
    return EXTENSIONS.get(Path(file).suffix.lower())


def paste_options(args) -> dict:
    """The JSON fields for the flags the user actually gave (FR-6.2)."""
    body = {}
    if args.title is not None:
        body["title"] = args.title
    if args.lang is not None:
        body["language"] = args.lang
    if args.visibility is not None:
        body["visibility"] = args.visibility
    if args.expire is not None:
        body["expire"] = args.expire
    return body


def require_token(server: str) -> str:
    token = client.saved_token(server)
    if token is None:
        cfg = config.load()
        if cfg.get("token"):
            raise Failure(f"you're signed in to {cfg.get('url')}, not {server}. Run: ratcache login")
        raise Failure("sign in first: ratcache login")
    return token


def local_time(value: str | None) -> str:
    """Server timestamps are UTC ISO strings; show them as local 'YYYY-MM-DD HH:MM'."""
    if not value:
        return "-"
    m = re.match(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)", value)
    if not m:
        return value
    dt = datetime.strptime(m.group(1), "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc)
    return dt.astimezone().strftime("%Y-%m-%d %H:%M")


# ---- commands --------------------------------------------------------------------------

def cmd_create(args) -> int:
    """FR-6.1 / 6.2: create a paste and print its link."""
    content = read_content(args.file)
    body = {"content": content, **paste_options(args)}
    if "language" not in body and (lang := guess_language(args.file)):
        body["language"] = lang
    server = client.base_url()
    try:
        paste = client.post_json("/api/pastes", body, token=client.saved_token(server), url=server)
    except client.ApiError as e:
        return api_failed(e)
    print(paste["url"])
    return EXIT_OK


def cmd_get(args) -> int:
    """FR-6.3: write a paste's raw content to stdout, byte for byte."""
    paste_id, server = parse_paste_ref(args.id)
    server = server or client.base_url()
    try:
        data = client.get_raw(f"/raw/{paste_id}", token=client.saved_token(server), url=server)
    except client.ApiError as e:
        return api_failed(e)
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()
    return EXIT_OK


def cmd_login(args) -> int:
    """FR-6.4: sign in, create an API token, save it to the config file."""
    server = client.base_url()
    try:
        username = args.username or input_stderr("username: ")
        if args.password_stdin:
            password = sys.stdin.readline().rstrip("\r\n")
        else:
            password = getpass.getpass("password: ", stream=sys.stderr)
    except EOFError:
        raise UsageError("no username or password given") from None
    if not username or not password:
        raise UsageError("username and password are required")

    try:
        # Sign in with a session cookie, use it to create a token, then end the session.
        resp = client.request("POST", "/api/sessions",
                              body={"username": username, "password": password}, url=server)
        cookie = session_cookie(resp)
        if not cookie:
            error("the server didn't return a session cookie")
            return EXIT_ERROR
        auth = {"Cookie": cookie}
        new = client.post_json("/api/tokens", {"name": token_name()}, headers=auth, url=server)
        try:
            client.delete("/api/sessions", headers=auth, url=server)
        except client.ApiError:
            pass  # the session cookie is thrown away anyway
    except client.ApiError as e:
        return api_failed(e)

    old = config.load()
    config.save({**old, "url": server, "username": resp.json()["username"],
                 "token": new["token"], "token_id": new["id"]})

    # Logging in again replaces the old token; revoke it so they don't pile up.
    if old.get("token") and str(old.get("url", "")).rstrip("/") == server and old.get("token_id"):
        try:
            client.delete(f"/api/tokens/{old['token_id']}", token=new["token"], url=server)
        except client.ApiError:
            pass

    print(f"signed in to {server} as {resp.json()['username']}", file=sys.stderr)
    return EXIT_OK


def cmd_logout(args) -> int:
    """FR-6.4: revoke the saved token on the server, then forget it."""
    cfg = config.load()
    if not cfg.get("token"):
        print("not signed in", file=sys.stderr)
        return EXIT_OK
    server = str(cfg.get("url") or client.base_url()).rstrip("/")
    try:
        client.delete(f"/api/tokens/{cfg.get('token_id')}", token=cfg["token"], url=server)
    except client.ApiError as e:
        # 401/404: already revoked or the user was deleted, so there's nothing left to undo.
        if e.status not in (401, 404):
            error(f"{e}\nthe token is still saved; try again when the server is reachable")
            return EXIT_ERROR
    config.clear_login()
    print(f"signed out of {server}", file=sys.stderr)
    return EXIT_OK


def cmd_list(args) -> int:
    """FR-6.5: list the signed-in user's pastes, newest first."""
    server = client.base_url()
    token = require_token(server)
    try:
        pastes = client.get_json("/api/pastes", token=token, url=server)
    except client.ApiError as e:
        return api_failed(e)
    if not pastes:
        print("no pastes", file=sys.stderr)
        return EXIT_OK
    rows = [("id", "created", "expires", "visibility", "lang", "title")]
    for p in pastes:
        rows.append((p["id"], local_time(p.get("createdAt")),
                     local_time(p.get("expiresAt")) if p.get("expiresAt") else "never",
                     p.get("visibility", ""), p.get("language", ""), p.get("title") or ""))
    widths = [max(len(r[i]) for r in rows) for i in range(len(rows[0]) - 1)]
    for r in rows:
        print("  ".join(c.ljust(w) for c, w in zip(r, widths)) + "  " + r[-1])
    return EXIT_OK


def cmd_edit(args) -> int:
    """FR-6.5: replace content (from a file, or "-" for stdin) and/or change options."""
    paste_id, server = parse_paste_ref(args.id)
    server = server or client.base_url()
    body = paste_options(args)
    if args.file is not None:
        body["content"] = read_content(args.file)
    if not body:
        raise UsageError("nothing to change: give a file, - for stdin, or an option like --title")
    token = require_token(server)
    try:
        paste = client.patch_json(f"/api/pastes/{paste_id}", body, token=token, url=server)
    except client.ApiError as e:
        return api_failed(e)
    print(paste["url"])
    return EXIT_OK


def cmd_delete(args) -> int:
    """FR-6.5: delete one of your pastes."""
    paste_id, server = parse_paste_ref(args.id)
    server = server or client.base_url()
    token = require_token(server)
    try:
        client.delete(f"/api/pastes/{paste_id}", token=token, url=server)
    except client.ApiError as e:
        return api_failed(e)
    print(f"deleted {paste_id}", file=sys.stderr)
    return EXIT_OK


def cmd_ping(args) -> int:
    """Check the server is reachable: GET /api/health."""
    try:
        body = client.get_json("/api/health")
    except client.ApiError as e:
        return api_failed(e)
    print(f"{client.base_url()}: {body.get('status', 'unknown')}")
    return EXIT_OK


def input_stderr(prompt: str) -> str:
    """input() but with the prompt on stderr, so stdout stays clean."""
    print(prompt, end="", file=sys.stderr, flush=True)
    line = sys.stdin.readline()
    if not line:
        raise EOFError
    return line.strip()


def session_cookie(resp: client.Response) -> str | None:
    """The ratcache_session cookie(s) from Set-Cookie, as a Cookie header value.

    Parsed by hand because the cookie is Secure, and Python's cookie jar won't send a
    Secure cookie back over http://localhost. ASP.NET may split a big cookie into
    ratcache_sessionC1, C2, ..., so keep every piece.
    """
    pairs = []
    for header in resp.headers.get_all("Set-Cookie") or []:
        pair = header.split(";", 1)[0].strip()
        if pair.startswith("ratcache_session"):
            pairs.append(pair)
    return "; ".join(pairs) or None


def token_name() -> str:
    """Shown on the account page, so the user can tell their tokens apart."""
    try:
        host = socket.gethostname()
    except OSError:
        host = ""
    return (f"cli on {host}" if host else "cli")[:64]


# ---- argument parsing ------------------------------------------------------------------

def add_paste_options(p: argparse.ArgumentParser) -> None:
    """Flags shared by create and edit (FR-6.2)."""
    p.add_argument("--title", help='paste title ("" removes it when editing)')
    p.add_argument("--lang", choices=LANGUAGES, metavar="LANG",
                   help="language for highlighting: " + " ".join(LANGUAGES))
    p.add_argument("--visibility", choices=["public", "unlisted", "private"])
    p.add_argument("--expire", choices=["10m", "1h", "1d", "1w", "1mo", "never"],
                   help="when editing, the clock restarts from now")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="ratcache",
        description="RatCache pastebin client. With no command, creates a paste from FILE or "
                    "stdin. Server: $RATCACHE_URL, else the one you logged in to, else "
                    f"{client.DEFAULT_URL}.",
    )
    parser.add_argument("--version", action="version", version=f"ratcache {__version__}")
    sub = parser.add_subparsers(dest="command", metavar="command")

    p = sub.add_parser("get", help="print a paste's raw content")
    p.add_argument("id", help="paste ID or link")
    p.set_defaults(func=cmd_get)

    p = sub.add_parser("login", help="sign in and save an API token")
    p.add_argument("--username", "-u")
    p.add_argument("--password-stdin", action="store_true",
                   help="read the password from the first line of stdin (for scripts)")
    p.set_defaults(func=cmd_login)

    p = sub.add_parser("logout", help="revoke and remove the saved token")
    p.set_defaults(func=cmd_logout)

    p = sub.add_parser("list", help="list your pastes")
    p.set_defaults(func=cmd_list)

    p = sub.add_parser("edit", help="replace a paste's content or change its options")
    p.add_argument("id", help="paste ID or link")
    p.add_argument("file", nargs="?", help="new content (- for stdin)")
    add_paste_options(p)
    p.set_defaults(func=cmd_edit)

    p = sub.add_parser("delete", help="delete a paste")
    p.add_argument("id", help="paste ID or link")
    p.set_defaults(func=cmd_delete)

    p = sub.add_parser("ping", help="check that the server is reachable")
    p.set_defaults(func=cmd_ping)

    return parser


def build_create_parser() -> argparse.ArgumentParser:
    """`ratcache [file]` with no subcommand creates a paste."""
    p = argparse.ArgumentParser(prog="ratcache", description="Create a paste from FILE or stdin.")
    p.add_argument("file", nargs="?", help="file to paste (default: stdin)")
    add_paste_options(p)
    return p


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv

    # The command must be the first word. Anything else means "create a paste".
    first = argv[0] if argv else None
    if first in COMMANDS or first in ("-h", "--help", "--version"):
        args = build_parser().parse_args(argv)
        run = args.func
    else:
        args = build_create_parser().parse_args(argv)
        run = cmd_create

    try:
        return run(args)
    except UsageError as e:
        error(str(e))
        return EXIT_USAGE
    except Failure as e:
        error(str(e))
        return EXIT_ERROR
    except KeyboardInterrupt:
        print(file=sys.stderr)
        return 130
    except BrokenPipeError:
        # `ratcache get x | head`: the reader went away. Point stdout at devnull so Python's
        # exit-time flush doesn't print a second error.
        os.dup2(os.open(os.devnull, os.O_WRONLY), sys.stdout.fileno())
        return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())

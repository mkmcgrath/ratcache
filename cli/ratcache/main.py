"""Entry point for the `ratcache` command (SRS 3.6, 5.3).

Usage:
    ratcache [file]            create a paste from a file or stdin
    ratcache get <id>          print a paste's raw content
    ratcache login | logout
    ratcache list
    ratcache edit <id> [file]
    ratcache delete <id>
    ratcache ping              check that the server is reachable

Exit status: 0 on success, non-zero on failure (FR-6.6).
Errors go to stderr (NFR-4.5).
"""

import argparse
import sys

from ratcache import __version__, client

COMMANDS = {"get", "login", "logout", "list", "edit", "delete", "ping"}

# FR-4.3 language IDs. Keep in sync with src/RatCache.Web/Models/Languages.cs.
LANGUAGES = [
    "text", "bash", "c", "cpp", "css", "go", "html", "java",
    "javascript", "json", "markdown", "python", "rust", "sql", "yaml",
]

EXIT_OK = 0
EXIT_ERROR = 1
EXIT_NOT_IMPLEMENTED = 2


def error(msg: str) -> None:
    print(f"ratcache: {msg}", file=sys.stderr)


def not_implemented(fr: str) -> int:
    error(f"not implemented yet ({fr})")
    return EXIT_NOT_IMPLEMENTED


def add_paste_options(p: argparse.ArgumentParser) -> None:
    """Flags shared by create and edit (FR-6.2)."""
    p.add_argument("--title")
    p.add_argument("--lang", choices=LANGUAGES)
    p.add_argument("--visibility", choices=["public", "unlisted", "private"])
    p.add_argument("--expire", choices=["10m", "1h", "1d", "1w", "1mo", "never"])


def cmd_create(args) -> int:
    # TODO FR-6.1: read args.file (or stdin), POST /api/pastes, print the link.
    return not_implemented("FR-6.1")


def cmd_get(args) -> int:
    # TODO FR-6.3: GET /raw/{id} (accept an ID or a full link), write to stdout.
    return not_implemented("FR-6.3")


def cmd_login(args) -> int:
    # TODO FR-6.4: sign in, save the API token to a config file in the home directory.
    return not_implemented("FR-6.4")


def cmd_logout(args) -> int:
    # TODO FR-6.4: revoke the token (DELETE /api/tokens/{id}) and remove it locally.
    return not_implemented("FR-6.4")


def cmd_list(args) -> int:
    # TODO FR-6.5: GET /api/pastes with the saved token.
    return not_implemented("FR-6.5")


def cmd_edit(args) -> int:
    # TODO FR-6.5: PATCH /api/pastes/{id}.
    return not_implemented("FR-6.5")


def cmd_delete(args) -> int:
    # TODO FR-6.5: DELETE /api/pastes/{id}.
    return not_implemented("FR-6.5")


def cmd_ping(args) -> int:
    """Working example: call GET /api/health."""
    try:
        body = client.get_json("/api/health")
    except client.ApiError as e:
        error(str(e))
        return EXIT_ERROR
    print(f"{client.base_url()}: {body.get('status', 'unknown')}")
    return EXIT_OK


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="ratcache",
        description="RatCache pastebin client. With no command, creates a paste from FILE or stdin.",
    )
    parser.add_argument("--version", action="version", version=f"ratcache {__version__}")
    sub = parser.add_subparsers(dest="command", metavar="command")

    p = sub.add_parser("get", help="print a paste's raw content")
    p.add_argument("id", help="paste ID or link")
    p.set_defaults(func=cmd_get)

    p = sub.add_parser("login", help="sign in and save an API token")
    p.set_defaults(func=cmd_login)

    p = sub.add_parser("logout", help="revoke and remove the saved token")
    p.set_defaults(func=cmd_logout)

    p = sub.add_parser("list", help="list your pastes")
    p.set_defaults(func=cmd_list)

    p = sub.add_parser("edit", help="replace a paste's content or change its options")
    p.add_argument("id")
    p.add_argument("file", nargs="?")
    add_paste_options(p)
    p.set_defaults(func=cmd_edit)

    p = sub.add_parser("delete", help="delete a paste")
    p.add_argument("id")
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
        return args.func(args)

    args = build_create_parser().parse_args(argv)
    return cmd_create(args)


if __name__ == "__main__":
    sys.exit(main())

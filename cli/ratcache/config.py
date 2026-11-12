"""The CLI's saved login (FR-6.4).

One JSON file in the user's home directory:

    Linux/macOS: $XDG_CONFIG_HOME/ratcache/config.json (default ~/.config/ratcache/config.json)
    Windows:     %APPDATA%\\ratcache\\config.json
    Override:    RATCACHE_CONFIG=/path/to/config.json

    {"url": "http://localhost:5097", "username": "alice", "token": "rc_...", "token_id": 3}

"url" is the server that issued the token. The token is only ever sent to that server.
The file is written with owner-only permissions (0600; folder 0700) because the token is
as good as the password until it's revoked.
"""

import json
import os
import sys
from pathlib import Path

LOGIN_KEYS = ("token", "token_id", "username")


def path() -> Path:
    override = os.environ.get("RATCACHE_CONFIG")
    if override:
        return Path(override).expanduser()
    if sys.platform == "win32" and os.environ.get("APPDATA"):
        base = Path(os.environ["APPDATA"])
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    return base / "ratcache" / "config.json"


def load() -> dict:
    """The saved config, or {} if there isn't one (or it's unreadable)."""
    try:
        data = json.loads(path().read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return data if isinstance(data, dict) else {}


def save(cfg: dict) -> None:
    p = path()
    p.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    tmp = p.with_name(p.name + ".tmp")
    # Create the file 0600 from the start so the token is never readable by others.
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2)
        f.write("\n")
    os.replace(tmp, p)
    try:
        os.chmod(p, 0o600)  # in case an older file had looser permissions
    except OSError:
        pass


def clear_login() -> None:
    """Forget the token but keep the server URL."""
    cfg = load()
    for k in LOGIN_KEYS:
        cfg.pop(k, None)
    if cfg:
        save(cfg)
    else:
        try:
            path().unlink()
        except FileNotFoundError:
            pass

"""Small HTTP helper for talking to the RatCache server API (SRS 5.2)."""

import json
import os
import urllib.error
import urllib.request

# Override with: export RATCACHE_URL=https://your-server
DEFAULT_URL = "http://localhost:5097"


class ApiError(Exception):
    """Raised when the server can't be reached or returns an error."""


def base_url() -> str:
    return os.environ.get("RATCACHE_URL", DEFAULT_URL).rstrip("/")


def get_json(path: str) -> dict:
    """GET base_url()+path and return the decoded JSON body."""
    url = base_url() + path
    try:
        with urllib.request.urlopen(url, timeout=10) as resp:
            return json.load(resp)
    except urllib.error.HTTPError as e:
        raise ApiError(f"{url} returned HTTP {e.code}") from e
    except urllib.error.URLError as e:
        raise ApiError(f"could not reach {url}: {e.reason}") from e

"""Small HTTP helper for talking to the RatCache server API (SRS 5.2).

All HTTP in the CLI goes through this module. Failures raise ApiError, which carries the
server's {"error": "...", "message": "..."} body (NFR-4.1) when there is one.
"""

import json
import os
import urllib.error
import urllib.request
from dataclasses import dataclass

from ratcache import config

# Override with: export RATCACHE_URL=https://your-server
DEFAULT_URL = "http://localhost:5097"
TIMEOUT = 30


class ApiError(Exception):
    """The server couldn't be reached or returned an error.

    status is the HTTP status (None if the server couldn't be reached); code is the
    server's snake_case error code (None if it didn't send one).
    """

    def __init__(self, message: str, status: int | None = None, code: str | None = None):
        super().__init__(message)
        self.status = status
        self.code = code


@dataclass
class Response:
    status: int
    headers: object  # http.client.HTTPMessage
    body: bytes

    def json(self):
        return json.loads(self.body.decode("utf-8")) if self.body else None


def base_url() -> str:
    """RATCACHE_URL if set, else the server you last logged in to, else localhost."""
    url = os.environ.get("RATCACHE_URL") or config.load().get("url") or DEFAULT_URL
    return url.rstrip("/")


def saved_token(url: str | None = None) -> str | None:
    """The saved API token, but only if it came from this server. Never send it elsewhere."""
    cfg = config.load()
    url = (url or base_url()).rstrip("/")
    if cfg.get("token") and str(cfg.get("url", "")).rstrip("/") == url:
        return cfg["token"]
    return None


def request(method: str, path: str, *, body=None, token: str | None = None,
            headers: dict | None = None, url: str | None = None) -> Response:
    """Send one request to (url or base_url()) + path. Raises ApiError on a non-2xx status."""
    full = (url or base_url()).rstrip("/") + path
    data = None
    req = urllib.request.Request(full, method=method)
    req.add_header("Accept", "application/json, text/plain")
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        req.add_header("Content-Type", "application/json")
    # Unredirected: credentials are never forwarded if the server redirects somewhere else.
    for k, v in (headers or {}).items():
        req.add_unredirected_header(k, v)
    if token:
        req.add_unredirected_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req, data=data, timeout=TIMEOUT) as resp:
            return Response(resp.status, resp.headers, resp.read())
    except urllib.error.HTTPError as e:
        raise _error_from(e, full) from None
    except urllib.error.URLError as e:
        raise ApiError(f"could not reach {full}: {e.reason}") from None
    except TimeoutError:
        raise ApiError(f"{full} timed out") from None


def _error_from(e: urllib.error.HTTPError, url: str) -> ApiError:
    code = message = None
    try:
        payload = json.loads(e.read().decode("utf-8"))
        if isinstance(payload, dict):
            code, message = payload.get("error"), payload.get("message")
    except (ValueError, UnicodeDecodeError, OSError):
        pass
    if e.code == 404 and not message:
        message = "paste not found (it may have expired or be private)"
    return ApiError(message or f"{url} returned HTTP {e.code}", status=e.code, code=code)


def get_json(path: str, **kw):
    return request("GET", path, **kw).json()


def post_json(path: str, body, **kw):
    return request("POST", path, body=body, **kw).json()


def patch_json(path: str, body, **kw):
    return request("PATCH", path, body=body, **kw).json()


def delete(path: str, **kw) -> None:
    request("DELETE", path, **kw)


def get_raw(path: str, **kw) -> bytes:
    return request("GET", path, **kw).body

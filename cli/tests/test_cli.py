import io
import json
import os
import stat
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest import mock

from ratcache import client, config, main


class FakeServer:
    """A tiny stand-in for the RatCache API (SRS 5.2), enough to drive every CLI command."""

    def __init__(self):
        self.users = {"alice": "correct horse"}
        self.tokens = {}       # token -> (id, username)
        self.sessions = set()  # cookie values
        self.pastes = {}       # id -> dict
        self.requests = []     # (method, path, headers)
        self.next_id = 1
        server = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_GET(self): server.handle(self, "GET")
            def do_POST(self): server.handle(self, "POST")
            def do_PATCH(self): server.handle(self, "PATCH")
            def do_DELETE(self): server.handle(self, "DELETE")

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        threading.Thread(target=self.httpd.serve_forever, kwargs={"poll_interval": 0.02},
                         daemon=True).start()

    def close(self):
        self.httpd.shutdown()
        self.httpd.server_close()

    # -- request handling -----------------------------------------------------------------

    def handle(self, h, method):
        length = int(h.headers.get("Content-Length") or 0)
        body = json.loads(h.rfile.read(length)) if length else None
        self.requests.append((method, h.path, dict(h.headers)))
        user, bad_token = self.who(h)
        if bad_token:
            return self.send(h, 401, {"error": "invalid_token", "message": "That API token isn't valid."})
        route = (method, h.path)

        if route == ("GET", "/api/health"):
            return self.send(h, 200, {"status": "ok"})
        if route == ("POST", "/api/sessions"):
            if self.users.get(body.get("username")) != body.get("password"):
                return self.send(h, 401, {"error": "bad_credentials", "message": "Wrong username or password."})
            self.sessions.add("s3cret")
            return self.send(h, 200, {"username": body["username"]},
                             cookies=["ratcache_session=s3cret; path=/; secure; samesite=lax; httponly"])
        if route == ("DELETE", "/api/sessions"):
            return self.send(h, 204 if user else 401)
        if route == ("POST", "/api/tokens"):
            if not user:
                return self.send(h, 401, {"error": "sign_in_required", "message": "Sign in first."})
            tid = self.next_id
            self.next_id += 1
            token = f"rc_token{tid}"
            self.tokens[token] = (tid, user)
            return self.send(h, 201, {"id": tid, "name": body.get("name"), "createdAt": "x", "token": token})
        if method == "DELETE" and h.path.startswith("/api/tokens/"):
            tid = int(h.path.rsplit("/", 1)[1])
            for t, (i, u) in list(self.tokens.items()):
                if i == tid and u == user:
                    del self.tokens[t]
                    return self.send(h, 204)
            return self.send(h, 404, {"error": "token_not_found", "message": "No such token."})
        if route == ("POST", "/api/pastes"):
            if body.get("visibility") == "private" and not user:
                return self.send(h, 400, {"error": "sign_in_required_for_private",
                                          "message": "Private pastes need an account. Sign in first."})
            pid = f"P{len(self.pastes) + 1}"
            self.pastes[pid] = {**body, "owner": user}
            return self.send(h, 201, self.summary(pid))
        if route == ("GET", "/api/pastes"):
            if not user:
                return self.send(h, 401, {"error": "sign_in_required", "message": "Sign in first."})
            mine = [self.summary(p) for p, v in self.pastes.items() if v["owner"] == user]
            return self.send(h, 200, mine)
        if h.path.startswith("/api/pastes/"):
            pid = h.path.rsplit("/", 1)[1]
            if pid not in self.pastes:
                return self.send(h, 404, {"error": "paste_not_found", "message": "That paste doesn't exist."})
            if not user:
                return self.send(h, 401, {"error": "sign_in_required", "message": "Sign in first."})
            if self.pastes[pid]["owner"] != user:
                return self.send(h, 403, {"error": "not_owner", "message": "You can only edit your own pastes."})
            if method == "PATCH":
                self.pastes[pid].update(body)
                return self.send(h, 200, self.summary(pid))
            if method == "DELETE":
                del self.pastes[pid]
                return self.send(h, 204)
        if method == "GET" and h.path.startswith("/raw/"):
            p = self.pastes.get(h.path[5:])
            if not p or (p.get("visibility") == "private" and p["owner"] != user):
                return self.send_text(h, 404, b"not found\n")
            return self.send_text(h, 200, p["content"].encode("utf-8"))
        return self.send(h, 404, {"error": "not_found", "message": "no route"})

    def who(self, h):
        """(username or None, sent a bad token?)"""
        auth = h.headers.get("Authorization", "")
        if auth.startswith("Bearer "):
            entry = self.tokens.get(auth[7:])
            return (entry[1], False) if entry else (None, True)
        if "ratcache_session=s3cret" in (h.headers.get("Cookie") or "") and "s3cret" in self.sessions:
            return "alice", False
        return None, False

    def summary(self, pid):
        p = self.pastes[pid]
        return {"id": pid, "url": f"{self.url}/p/{pid}", "rawUrl": f"{self.url}/raw/{pid}",
                "title": p.get("title"), "language": p.get("language", "text"),
                "visibility": p.get("visibility", "public"), "createdAt": "2026-11-12T18:30:00Z",
                "expiresAt": None}

    @staticmethod
    def send(h, status, body=None, cookies=()):
        data = json.dumps(body).encode() if body is not None else b""
        h.send_response(status)
        for c in cookies:
            h.send_header("Set-Cookie", c)
        if body is not None:
            h.send_header("Content-Type", "application/json")
        h.send_header("Content-Length", str(len(data)))
        h.end_headers()
        h.wfile.write(data)

    @staticmethod
    def send_text(h, status, data):
        h.send_response(status)
        h.send_header("Content-Type", "text/plain; charset=utf-8")
        h.send_header("Content-Length", str(len(data)))
        h.end_headers()
        h.wfile.write(data)


class CliTestCase(unittest.TestCase):
    """Runs the CLI against a FakeServer with its own temporary config file."""

    def setUp(self):
        self.server = FakeServer()
        self.addCleanup(self.server.close)
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.tmp = Path(tmp.name)
        self.config_path = self.tmp / "conf" / "config.json"
        env = mock.patch.dict(os.environ, {"RATCACHE_URL": self.server.url,
                                           "RATCACHE_CONFIG": str(self.config_path)})
        env.start()
        self.addCleanup(env.stop)

    def run_cli(self, *argv, stdin: bytes = b"", tty: bool = False):
        """Returns (exit code, stdout text, stderr text)."""
        fake_in = io.TextIOWrapper(io.BytesIO(stdin), encoding="utf-8")
        fake_in.isatty = lambda: tty
        out, err = io.BytesIO(), io.StringIO()
        fake_out = io.TextIOWrapper(out, encoding="utf-8", write_through=True)
        with mock.patch.object(sys, "stdin", fake_in), mock.patch.object(sys, "stdout", fake_out), \
                mock.patch.object(sys, "stderr", err):
            code = main.main(list(argv))
            fake_out.flush()
        return code, out.getvalue().decode("utf-8"), err.getvalue()

    def login(self):
        code, _, err = self.run_cli("login", "-u", "alice", "--password-stdin", stdin=b"correct horse\n")
        self.assertEqual(code, 0, err)

    def write(self, name, text):
        p = self.tmp / name
        p.write_text(text, encoding="utf-8")
        return str(p)


class PingTests(CliTestCase):
    def test_ping_ok(self):
        code, out, _ = self.run_cli("ping")
        self.assertEqual(code, main.EXIT_OK)
        self.assertEqual(out, f"{self.server.url}: ok\n")

    def test_ping_server_down(self):
        with mock.patch.dict(os.environ, {"RATCACHE_URL": "http://127.0.0.1:9"}):
            code, out, err = self.run_cli("ping")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertEqual(out, "")
        self.assertIn("could not reach", err)


class CreateTests(CliTestCase):
    def test_from_stdin_prints_link(self):
        code, out, _ = self.run_cli(stdin=b"hello\n")
        self.assertEqual(code, 0)
        self.assertEqual(out, f"{self.server.url}/p/P1\n")
        self.assertEqual(self.server.pastes["P1"]["content"], "hello\n")
        self.assertIsNone(self.server.pastes["P1"]["owner"])

    def test_from_file_with_flags(self):
        f = self.write("notes.txt", "x = 1")
        code, _, _ = self.run_cli(f, "--title", "list", "--lang", "python",
                                  "--visibility", "unlisted", "--expire", "1h")
        self.assertEqual(code, 0)
        p = self.server.pastes["P1"]
        self.assertEqual((p["title"], p["language"], p["visibility"], p["expire"]),
                         ("list", "python", "unlisted", "1h"))

    def test_language_guessed_from_extension(self):
        self.run_cli(self.write("script.py", "print(1)"))
        self.assertEqual(self.server.pastes["P1"]["language"], "python")

    def test_unknown_extension_leaves_language_to_server(self):
        self.run_cli(self.write("data.xyz", "?"))
        self.assertNotIn("language", self.server.pastes["P1"])

    def test_signed_in_paste_is_owned(self):
        self.login()
        self.run_cli(stdin=b"mine")
        self.assertEqual(self.server.pastes["P1"]["owner"], "alice")

    def test_tty_without_file_is_usage_error(self):
        code, _, err = self.run_cli(tty=True)
        self.assertEqual(code, main.EXIT_USAGE)
        self.assertIn("nothing to paste", err)
        self.assertEqual(self.server.requests, [])

    def test_missing_file(self):
        code, _, err = self.run_cli(str(self.tmp / "nope.txt"))
        self.assertEqual(code, main.EXIT_USAGE)
        self.assertIn("can't read", err)

    def test_binary_file_rejected(self):
        p = self.tmp / "blob.bin"
        p.write_bytes(b"\xff\xfe\x00")
        code, _, err = self.run_cli(str(p))
        self.assertEqual(code, main.EXIT_USAGE)
        self.assertIn("isn't UTF-8", err)

    def test_server_error_message_shown(self):
        code, out, err = self.run_cli("--visibility", "private", stdin=b"x")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertEqual(out, "")
        self.assertIn("Private pastes need an account", err)

    def test_unknown_language_is_rejected(self):
        with self.assertRaises(SystemExit) as cm, mock.patch.object(sys, "stderr", io.StringIO()):
            main.main(["--lang", "klingon"])
        self.assertEqual(cm.exception.code, 2)

    def test_flag_value_is_not_mistaken_for_command(self):
        # `--title list` is a create titled "list", not the `list` command.
        with mock.patch.object(main, "cmd_create", return_value=0) as create:
            self.assertEqual(main.main(["--title", "list"]), 0)
            create.assert_called_once()


class GetTests(CliTestCase):
    def setUp(self):
        super().setUp()
        self.server.pastes["P1"] = {"content": "línea 1\r\nline 2", "owner": None}

    def test_by_id_is_byte_exact(self):
        code, out, _ = self.run_cli("get", "P1")
        self.assertEqual(code, 0)
        self.assertEqual(out, "línea 1\r\nline 2")

    def test_by_link(self):
        for link in (f"{self.server.url}/p/P1", f"{self.server.url}/raw/P1", f"{self.server.url}/p/P1/edit"):
            code, out, _ = self.run_cli("get", link)
            self.assertEqual((code, out), (0, "línea 1\r\nline 2"), link)

    def test_link_uses_its_own_server(self):
        with mock.patch.dict(os.environ, {"RATCACHE_URL": "http://127.0.0.1:9"}):
            code, _, _ = self.run_cli("get", f"{self.server.url}/p/P1")
        self.assertEqual(code, 0)

    def test_not_found(self):
        code, out, err = self.run_cli("get", "nope")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertEqual(out, "")
        self.assertIn("not found", err)

    def test_bad_reference(self):
        for ref in ("../etc", "http://x/other/P1", "ftp://x/p/P1"):
            code, _, err = self.run_cli("get", ref)
            self.assertEqual(code, main.EXIT_USAGE, ref)
        self.assertEqual(self.server.requests, [])

    def test_private_paste_needs_owner_token(self):
        self.server.pastes["P2"] = {"content": "secret", "visibility": "private", "owner": "alice"}
        self.assertEqual(self.run_cli("get", "P2")[0], main.EXIT_ERROR)
        self.login()
        self.assertEqual(self.run_cli("get", "P2")[:2], (0, "secret"))


class LoginTests(CliTestCase):
    def test_login_saves_token_privately(self):
        self.login()
        cfg = json.loads(self.config_path.read_text())
        self.assertEqual(cfg, {"url": self.server.url, "username": "alice",
                               "token": "rc_token1", "token_id": 1})
        if os.name == "posix":
            self.assertEqual(stat.S_IMODE(self.config_path.stat().st_mode), 0o600)
            self.assertEqual(stat.S_IMODE(self.config_path.parent.stat().st_mode), 0o700)

    def test_login_ends_the_cookie_session(self):
        self.login()
        methods = [(m, p) for m, p, _ in self.server.requests]
        self.assertEqual(methods, [("POST", "/api/sessions"), ("POST", "/api/tokens"),
                                   ("DELETE", "/api/sessions")])
        # The Secure cookie is sent back even over http (parsed by hand, not a cookie jar).
        self.assertIn("ratcache_session=s3cret", self.server.requests[1][2]["Cookie"])

    def test_prompts_go_to_stderr(self):
        with mock.patch("getpass.getpass", return_value="correct horse"):
            code, out, err = self.run_cli("login", stdin=b"alice\n")
        self.assertEqual(code, 0)
        self.assertEqual(out, "")
        self.assertIn("username:", err)

    def test_wrong_password(self):
        code, _, err = self.run_cli("login", "-u", "alice", "--password-stdin", stdin=b"wrong\n")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertIn("Wrong username or password", err)
        self.assertFalse(self.config_path.exists())

    def test_login_again_revokes_old_token(self):
        self.login()
        self.login()
        self.assertEqual(list(self.server.tokens), ["rc_token2"])
        self.assertEqual(json.loads(self.config_path.read_text())["token_id"], 2)

    def test_token_not_sent_to_another_server(self):
        self.login()
        other = FakeServer()
        self.addCleanup(other.close)
        with mock.patch.dict(os.environ, {"RATCACHE_URL": other.url}):
            self.run_cli(stdin=b"x")
            code, _, err = self.run_cli("list")
        self.assertNotIn("Authorization", other.requests[0][2])
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertIn("signed in to", err)

    def test_saved_server_used_without_env(self):
        self.login()
        with mock.patch.dict(os.environ):
            del os.environ["RATCACHE_URL"]
            self.assertEqual(client.base_url(), self.server.url)


class LogoutTests(CliTestCase):
    def test_logout_revokes_and_forgets(self):
        self.login()
        code, _, _ = self.run_cli("logout")
        self.assertEqual(code, 0)
        self.assertEqual(self.server.tokens, {})
        self.assertEqual(json.loads(self.config_path.read_text()), {"url": self.server.url})

    def test_logout_when_token_already_revoked(self):
        self.login()
        self.server.tokens.clear()
        self.assertEqual(self.run_cli("logout")[0], 0)
        self.assertNotIn("token", config.load())

    def test_logout_keeps_token_if_server_unreachable(self):
        self.login()
        self.server.close()
        code, _, err = self.run_cli("logout")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertIn("still saved", err)
        self.assertIn("token", config.load())

    def test_logout_when_not_signed_in(self):
        self.assertEqual(self.run_cli("logout")[0], 0)


class ListEditDeleteTests(CliTestCase):
    def setUp(self):
        super().setUp()
        self.login()
        self.run_cli("--title", "first", stdin=b"one")
        self.server.pastes["P9"] = {"content": "theirs", "owner": "bob"}

    def test_list(self):
        code, out, _ = self.run_cli("list")
        self.assertEqual(code, 0)
        lines = out.splitlines()
        self.assertTrue(lines[0].startswith("id"))
        self.assertEqual(len(lines), 2)
        self.assertTrue(lines[1].startswith("P1 "))
        self.assertTrue(lines[1].endswith("first"))
        self.assertIn("never", lines[1])

    def test_list_needs_login(self):
        self.run_cli("logout")
        code, out, err = self.run_cli("list")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertIn("ratcache login", err)

    def test_revoked_token_explained(self):
        self.server.tokens.clear()
        code, _, err = self.run_cli("list")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertIn("rejected", err)

    def test_edit_options_only(self):
        code, out, _ = self.run_cli("edit", "P1", "--title", "", "--expire", "1d")
        self.assertEqual(code, 0)
        self.assertEqual(out, f"{self.server.url}/p/P1\n")
        p = self.server.pastes["P1"]
        self.assertEqual((p["title"], p["content"], p["expire"]), ("", "one", "1d"))

    def test_edit_content_from_file_and_stdin(self):
        self.run_cli("edit", "P1", self.write("new.txt", "two"))
        self.assertEqual(self.server.pastes["P1"]["content"], "two")
        self.run_cli("edit", f"{self.server.url}/p/P1", "-", stdin=b"three")
        self.assertEqual(self.server.pastes["P1"]["content"], "three")

    def test_edit_with_nothing_to_change(self):
        code, _, err = self.run_cli("edit", "P1")
        self.assertEqual(code, main.EXIT_USAGE)
        self.assertIn("nothing to change", err)

    def test_edit_someone_elses_paste(self):
        code, _, err = self.run_cli("edit", "P9", "--title", "mine now")
        self.assertEqual(code, main.EXIT_ERROR)
        self.assertIn("your own pastes", err)

    def test_delete(self):
        code, _, _ = self.run_cli("delete", "P1")
        self.assertEqual(code, 0)
        self.assertNotIn("P1", self.server.pastes)
        self.assertEqual(self.run_cli("delete", "P1")[0], main.EXIT_ERROR)


class ConfigPathTests(unittest.TestCase):
    def test_xdg_and_home(self):
        with mock.patch.dict(os.environ, {"XDG_CONFIG_HOME": "/x"}, clear=True), \
                mock.patch.object(sys, "platform", "linux"):
            self.assertEqual(config.path(), Path("/x/ratcache/config.json"))
        with mock.patch.dict(os.environ, {}, clear=True), mock.patch.object(sys, "platform", "linux"), \
                mock.patch.object(Path, "home", return_value=Path("/h")):
            self.assertEqual(config.path(), Path("/h/.config/ratcache/config.json"))

    def test_windows_appdata(self):
        with mock.patch.dict(os.environ, {"APPDATA": "/appdata"}, clear=True), \
                mock.patch.object(sys, "platform", "win32"):
            self.assertEqual(config.path(), Path("/appdata/ratcache/config.json"))


class LanguageListTests(unittest.TestCase):
    def test_matches_server_list(self):
        # The CLI's --lang choices must match the server's Models/Languages.cs.
        import re
        cs = Path(__file__).resolve().parents[2] / "src/RatCache.Web/Models/Languages.cs"
        server_ids = re.findall(r'\("([a-z]+)", "', cs.read_text())
        self.assertEqual(server_ids, main.LANGUAGES)

    def test_extension_map_uses_known_ids(self):
        self.assertLessEqual(set(main.EXTENSIONS.values()), set(main.LANGUAGES))


if __name__ == "__main__":
    unittest.main()

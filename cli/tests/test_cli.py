import unittest
from unittest import mock

from ratcache import client, main


class PingTests(unittest.TestCase):
    def test_ping_ok(self):
        with mock.patch.object(client, "get_json", return_value={"status": "ok"}):
            self.assertEqual(main.main(["ping"]), main.EXIT_OK)

    def test_ping_server_down(self):
        with mock.patch.object(client, "get_json", side_effect=client.ApiError("down")):
            self.assertEqual(main.main(["ping"]), main.EXIT_ERROR)


class StubTests(unittest.TestCase):
    def test_unimplemented_command_exits_nonzero(self):
        self.assertEqual(main.main(["list"]), main.EXIT_NOT_IMPLEMENTED)

    def test_no_command_means_create(self):
        self.assertEqual(main.main(["somefile.txt", "--lang", "python"]), main.EXIT_NOT_IMPLEMENTED)

    def test_flag_value_is_not_mistaken_for_command(self):
        # `--title list` is a create titled "list", not the `list` command.
        with mock.patch.object(main, "cmd_create", return_value=0) as create:
            self.assertEqual(main.main(["--title", "list"]), 0)
            create.assert_called_once()

    def test_unknown_language_is_rejected(self):
        with self.assertRaises(SystemExit) as cm:
            main.main(["--lang", "klingon"])
        self.assertEqual(cm.exception.code, 2)


class LanguageListTests(unittest.TestCase):
    def test_matches_server_list(self):
        # The CLI's --lang choices must match the server's Models/Languages.cs.
        import pathlib, re
        cs = pathlib.Path(__file__).resolve().parents[2] / "src/RatCache.Web/Models/Languages.cs"
        server_ids = re.findall(r'\("([a-z]+)", "', cs.read_text())
        self.assertEqual(server_ids, main.LANGUAGES)


if __name__ == "__main__":
    unittest.main()

import json
import unittest
from pathlib import Path
from urllib.parse import urlparse


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
PLUGIN_ROOT = REPOSITORY_ROOT / "plugins" / "autofpl"


class PluginPackageTests(unittest.TestCase):
    def test_autofpl_plugin_points_to_the_public_mcp_boundary(self) -> None:
        manifest = json.loads(
            (PLUGIN_ROOT / ".codex-plugin" / "plugin.json").read_text(
                encoding="utf-8"
            )
        )
        mcp_configuration = json.loads(
            (PLUGIN_ROOT / ".mcp.json").read_text(encoding="utf-8")
        )

        self.assertEqual("autofpl", manifest["name"])
        self.assertEqual("./.mcp.json", manifest["mcpServers"])
        self.assertEqual("autoFPL", manifest["interface"]["displayName"])
        prompts = manifest["interface"]["defaultPrompt"]
        self.assertEqual(3, len(prompts))
        self.assertTrue(all(prompt and len(prompt) <= 128 for prompt in prompts))

        self.assertEqual(
            {"autofpl"},
            set(mcp_configuration["mcpServers"]),
        )
        server = mcp_configuration["mcpServers"]["autofpl"]
        self.assertEqual("http", server["type"])
        endpoint = urlparse(server["url"])
        self.assertEqual("https", endpoint.scheme)
        self.assertEqual("autofpl.pownet.uk", endpoint.hostname)
        self.assertEqual("/mcp", endpoint.path)
        self.assertFalse(endpoint.query)
        self.assertFalse(endpoint.fragment)

    def test_autofpl_plugin_contains_no_scaffold_placeholders(self) -> None:
        package_text = "\n".join(
            path.read_text(encoding="utf-8")
            for path in PLUGIN_ROOT.rglob("*")
            if path.is_file()
        )

        self.assertNotIn("[TODO:", package_text)
        self.assertNotIn("Local developer", package_text)
        self.assertNotIn("local Codex plugin scaffold", package_text)


if __name__ == "__main__":
    unittest.main()

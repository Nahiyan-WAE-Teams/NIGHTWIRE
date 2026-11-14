from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "examples" / "Episode_001"
PROJECT = ROOT / "tools" / "Nightwire.EpisodeValidator" / "Nightwire.EpisodeValidator.csproj"
DLL = ROOT / "tools" / "Nightwire.EpisodeValidator" / "bin" / "Release" / "net10.0" / "Nightwire.EpisodeValidator.dll"


class EpisodeValidatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if shutil.which("dotnet") is None:
            raise RuntimeError(".NET 10 SDK is required to run validator integration tests.")
        build = subprocess.run(
            ["dotnet", "build", str(PROJECT), "--configuration", "Release"],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        if build.returncode != 0:
            raise RuntimeError(f"Validator build failed:\n{build.stdout}\n{build.stderr}")

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="nightwire-episode-test-")
        self.package = Path(self.temp.name) / "Episode_001"
        shutil.copytree(SAMPLE, self.package)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def run_validator(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["dotnet", str(DLL), str(self.package)],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

    def refresh_manifest_entry(self, relative_path: str) -> None:
        path = self.package / relative_path
        content = path.read_bytes()
        manifest_path = self.package / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        entry = next(item for item in manifest["files"] if item["path"] == relative_path)
        entry["size_bytes"] = len(content)
        entry["sha256"] = hashlib.sha256(content).hexdigest()
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    def test_sample_episode_passes(self) -> None:
        result = self.run_validator()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Validation passed", result.stdout)

    def test_modified_file_fails_integrity_check(self) -> None:
        localization = self.package / "Localization" / "en.json"
        localization.write_text(localization.read_text(encoding="utf-8") + "\n", encoding="utf-8")
        result = self.run_validator()
        self.assertEqual(result.returncode, 1)
        self.assertIn("SHA-256 mismatch", result.stderr)

    def test_missing_choice_target_fails_semantic_check(self) -> None:
        relative = "Characters/Anisha/Conversations/day01.json"
        conversation_path = self.package / relative
        conversation = json.loads(conversation_path.read_text(encoding="utf-8"))
        conversation["messages"][1]["options"][0]["next_message_id"] = "missing.message"
        conversation_path.write_text(json.dumps(conversation, indent=2) + "\n", encoding="utf-8")
        self.refresh_manifest_entry(relative)
        result = self.run_validator()
        self.assertEqual(result.returncode, 1)
        self.assertIn("choice points to missing message", result.stderr)

    def test_call_invitation_must_resolve(self) -> None:
        relative = "Characters/Anisha/Conversations/day01.json"
        conversation_path = self.package / relative
        conversation = json.loads(conversation_path.read_text(encoding="utf-8"))
        conversation["messages"].append({
            "message_id": "anisha.day01.call01",
            "type": "call_invitation",
            "sender_id": "anisha",
            "call_id": "missing.call",
        })
        conversation_path.write_text(json.dumps(conversation, indent=2) + "\n", encoding="utf-8")
        self.refresh_manifest_entry(relative)
        result = self.run_validator()
        self.assertEqual(result.returncode, 1)
        self.assertIn("does not resolve", result.stderr)

    def test_minigame_sequence_must_use_declared_symbols(self) -> None:
        relative = "Minigames/signal_sequence.json"
        minigame_path = self.package / relative
        minigame = json.loads(minigame_path.read_text(encoding="utf-8"))
        minigame["configuration"]["sequence"][1] = "undeclared"
        minigame_path.write_text(json.dumps(minigame, indent=2) + "\n", encoding="utf-8")
        self.refresh_manifest_entry(relative)
        result = self.run_validator()
        self.assertEqual(result.returncode, 1)
        self.assertIn("not declared in symbols", result.stderr)

    def test_post_author_must_resolve(self) -> None:
        relative = "Characters/Anisha/Posts/post01.json"
        post_path = self.package / relative
        post = json.loads(post_path.read_text(encoding="utf-8"))
        post["author_id"] = "missing_character"
        post_path.write_text(json.dumps(post, indent=2) + "\n", encoding="utf-8")
        self.refresh_manifest_entry(relative)
        result = self.run_validator()
        self.assertEqual(result.returncode, 1)
        self.assertIn("does not resolve to a character", result.stderr)

    def test_path_traversal_in_manifest_is_rejected(self) -> None:
        manifest_path = self.package / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["files"][0]["path"] = "../episode.json"
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        result = self.run_validator()
        self.assertEqual(result.returncode, 1)
        self.assertIn("unsafe relative path", result.stderr)


if __name__ == "__main__":
    unittest.main()

"""Fault injection is isolated in temporary fixtures; production assets are never written."""
import hashlib
import json
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from validate_project import PROJECT, SCENE, validate_project
from fixture_paths import new_fixture, remove_fixture


class ProjectFailureTests(unittest.TestCase):
    def setUp(self):
        self.project = new_fixture()
        self.addCleanup(remove_fixture, self.project)
        for relative in (SCENE, "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/ProjectVersion.txt",
                         "Packages/manifest.json", "Packages/packages-lock.json",
                         "Assets/MyImageLibrary.asset", "Assets/Wakayama_Root.prefab",
                         "Assets/Script/ScreenshotManager.cs", "Assets/Script/CameraMaskController.cs"):
            target = self.project / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(PROJECT / relative, target)
            if relative.startswith("Assets/"):
                shutil.copyfile(Path(str(PROJECT / relative) + ".meta"), Path(str(target) + ".meta"))
        for directory in (self.project / "Assets").rglob("*"):
            if directory.is_dir():
                guid = hashlib.md5(str(directory.relative_to(self.project)).encode()).hexdigest()
                Path(str(directory) + ".meta").write_text(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n")

    def errors(self):
        return validate_project(self.project)["errors"]

    def test_complete_fixture_passes(self):
        self.assertEqual(self.errors(), [])

    def test_malformed_json_has_structured_failure_and_nonzero_exit(self):
        (self.project / "Packages/manifest.json").write_text('{"dependencies":')
        script = Path(__file__).resolve().parents[1] / "validate_project.py"
        result = subprocess.run([sys.executable, "-B", str(script), "--project", str(self.project), "--json"],
                                capture_output=True, text=True, encoding="utf-8")
        self.assertNotEqual(result.returncode, 0)
        report = json.loads(result.stdout)
        self.assertTrue(any("Invalid JSON" in error for error in report["errors"]))
        self.assertNotIn("Traceback", result.stderr)

    def test_invalid_json_shapes_are_rejected(self):
        for document in ([], None, {}, {"dependencies": []}):
            with self.subTest(document=document):
                (self.project / "Packages/packages-lock.json").write_text(json.dumps(document))
                self.assertTrue(any("dependencies object" in error for error in self.errors()))

    def test_invalid_lock_entry_is_rejected(self):
        path = self.project / "Packages/packages-lock.json"
        data = json.loads(path.read_text())
        data["dependencies"]["com.unity.xr.arfoundation"] = None
        path.write_text(json.dumps(data))
        self.assertTrue(any("lock" in error.lower() for error in self.errors()))

    def test_package_version_mismatch_is_rejected(self):
        path = self.project / "Packages/manifest.json"
        data = json.loads(path.read_text())
        data["dependencies"]["com.unity.xr.arfoundation"] = "0.0.0"
        path.write_text(json.dumps(data))
        self.assertTrue(any("Manifest/lock mismatch" in error for error in self.errors()))

    def test_missing_required_file_is_rejected(self):
        (self.project / SCENE).unlink()
        self.assertTrue(any("Missing:" in error for error in self.errors()))

    def test_missing_asset_meta_is_rejected(self):
        (self.project / "Assets/MyImageLibrary.asset.meta").unlink()
        self.assertTrue(any("Missing meta" in error for error in self.errors()))

    def test_missing_folder_meta_is_rejected(self):
        (self.project / "Assets/Scenes.meta").unlink()
        self.assertTrue(any("Missing meta" in error for error in self.errors()))

    def test_invalid_meta_guid_is_rejected(self):
        (self.project / "Assets/MyImageLibrary.asset.meta").write_text("guid: invalid\n")
        self.assertTrue(any("Invalid meta GUID" in error for error in self.errors()))

    def test_duplicate_guid_is_rejected(self):
        shutil.copyfile(self.project / "Assets/MyImageLibrary.asset.meta", self.project / "Assets/Script.meta")
        self.assertTrue(any("Duplicate GUID" in error for error in self.errors()))

    def test_wrong_ar_route_is_rejected(self):
        path = self.project / SCENE
        path.write_text(path.read_text(encoding="utf-8").replace("4538b0873ffe4514c8f6a73751e4e259", "0" * 32), encoding="utf-8")
        self.assertTrue(any("AR m_SerializedLibrary" in error for error in self.errors()))

    def test_wrong_script_guid_is_rejected(self):
        path = self.project / SCENE
        path.write_text(path.read_text(encoding="utf-8").replace("43fb04ff823b1a34a93c76137a7f4de1", "0" * 32), encoding="utf-8")
        self.assertTrue(any("ScreenshotManager: wrong" in error for error in self.errors()))

    def test_invalid_utf8_is_a_reported_failure(self):
        (self.project / "Packages/manifest.json").write_bytes(b"\xff\xfe")
        self.assertTrue(any("Cannot read" in error for error in self.errors()))

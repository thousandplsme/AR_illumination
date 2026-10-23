"""Exercise the real PowerShell failure path in an isolated copy; never launch Unity."""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

from fixture_paths import new_fixture, remove_fixture

REPO = Path(__file__).resolve().parents[2]
PROJECT_NAME = "WakayamaAR_Photo(proto)"


@unittest.skipUnless(shutil.which("powershell.exe"), "Windows PowerShell is required")
class RunnerFailureTests(unittest.TestCase):
    def test_missing_xml_stops_later_steps_and_writes_failed_summary(self):
        root = new_fixture()
        self.addCleanup(remove_fixture, root)
        tools = root / "Tools"
        tools.mkdir()
        for name in ("develop.ps1", "validate_project.py", "run_report.py"):
            shutil.copyfile(REPO / "Tools" / name, tools / name)
        # Only the wrapper's orchestration is tested here; do not recursively run this test suite.
        (tools / "run_tool_tests.py").write_text(
            'print(\'{"testsRun":1,"failures":0,"errors":0,"successful":true}\')\n', encoding="utf-8")
        project = root / PROJECT_NAME
        source = REPO / PROJECT_NAME
        for relative in ("Assets/Scenes/SampleScene.unity", "Assets/MyImageLibrary.asset", "Assets/Wakayama_Root.prefab",
                         "Assets/Script/ScreenshotManager.cs", "Assets/Script/CameraMaskController.cs",
                         "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/ProjectVersion.txt",
                         "Packages/manifest.json", "Packages/packages-lock.json"):
            target = project / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source / relative, target)
            if relative.startswith("Assets/"):
                shutil.copyfile(Path(str(source / relative) + ".meta"), Path(str(target) + ".meta"))
        for directory in (project / "Assets").rglob("*"):
            if directory.is_dir():
                guid = hashlib.md5(str(directory.relative_to(project)).encode()).hexdigest()
                Path(str(directory) + ".meta").write_text("guid: " + guid + "\nfolderAsset: yes\n")
        fake_bin = root / "bin"
        fake_bin.mkdir()
        (fake_bin / "unity.cmd").write_text("@echo off\nexit /b 0\n", encoding="ascii")
        environment = dict(os.environ)
        environment["PATH"] = str(fake_bin) + os.pathsep + environment["PATH"]
        result = subprocess.run([shutil.which("powershell.exe"), "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                                 str(tools / "develop.ps1"), "-Task", "Test"], cwd=root, env=environment,
                                capture_output=True, text=True, errors="replace", timeout=60)
        self.assertNotEqual(result.returncode, 0)
        runs = list((root / "Artifacts/Runs").iterdir())
        self.assertEqual(len(runs), 1)
        report = json.loads((runs[0] / "summary.json").read_text(encoding="utf-8"))
        self.assertEqual(report["outcome"], "failed", result.stdout + result.stderr)
        self.assertEqual(report["failureStage"], "EditMode report")
        self.assertEqual(next(s["status"] for s in report["steps"] if s["step"] == "PlayMode"), "not_run")
        self.assertEqual(report["apk"]["status"], "not_requested")
        self.assertTrue((runs[0] / "summary.md").is_file())

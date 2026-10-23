"""Read-only checks for this project's serialized configuration (stdlib only)."""
import argparse
import json
import math
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "WakayamaAR_Photo(proto)"
SCENE = "Assets/Scenes/SampleScene.unity"


def blocks(text):
    return {
        m[2]: m[3]
        for m in re.finditer(
            r"^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)",
            text, re.M | re.S
        )
    }


def reference(block, field):
    match = re.search(r"^  " + re.escape(field) + r": \{fileID: (-?\d+)\}", block, re.M)
    return match[1] if match else None


def validate_scene(text):
    objects = blocks(text)
    errors, checks = [], []
    found = {}
    for name, fields in {
        "ScreenshotManager": {"titlePanel": "GameObject:", "photoPanel": "GameObject:",
                              "resultPanel": "GameObject:", "previewImage": "UnityEngine.UI.RawImage"},
        "CameraMaskController": {"photoTopMask": "RectTransform:", "photoBottomMask": "RectTransform:"},
    }.items():
        matches = [(key, value) for key, value in objects.items() if "::" + name + "\n" in value]
        if len(matches) != 1:
            errors.append(f"{name}: expected one component, got {len(matches)}")
            continue
        key, value = matches[0]
        found[name] = (key, value)
        for field, expected_type in fields.items():
            target = objects.get(reference(value, field), "")
            if expected_type not in target:
                errors.append(f"{name}.{field}: missing or wrong-type reference")
            else:
                checks.append(f"{name}.{field}")

    if len(found) == 2:
        manager_id, manager = found["ScreenshotManager"]
        mask_id, mask = found["CameraMaskController"]
        photo = objects.get(reference(manager, "photoPanel"), "")
        if f"component: {{fileID: {mask_id}}}" not in photo:
            errors.append("photoPanel must own CameraMaskController")
        if "  topMargin:" in manager:
            errors.append("ScreenshotManager still has a separate topMargin; use CameraMaskController")
        margin = re.search(r"^  topMargin: ([^\n]+)", mask, re.M)
        try:
            margin_value = float(margin[1]) if margin else float("nan")
        except ValueError:
            margin_value = float("nan")
        if not math.isfinite(margin_value) or margin_value < 0:
            errors.append("CameraMaskController.topMargin must be non-negative")
        for method in ("OnClickStart", "OnClickShutter", "OnClickRetake", "OnClickSave"):
            calls = re.findall(
                r"m_Target: \{fileID: (-?\d+)\}\n\s+m_TargetAssemblyTypeName: ([^\n]+)"
                r"\n\s+m_MethodName: " + method + r"\n", text
            )
            if len(calls) != 1 or calls[0][0] != manager_id:
                errors.append(f"{method}: expected one connection to ScreenshotManager")
            elif calls[0][1] != "ScreenshotManager, WakayamaAR.Runtime":
                errors.append(f"{method}: wrong target assembly")
            else:
                checks.append(method)
    return errors, checks


def validate_project(project=PROJECT):
    project = Path(project)
    errors, checks, notes = [], [], []
    def read(path):
        try:
            return (project / path).read_text(encoding="utf-8-sig")
        except (OSError, UnicodeError) as error:
            errors.append(f"Cannot read {path}: {error}")
            return None

    def dependencies(path):
        content = read(path)
        if content is None:
            return None
        try:
            document = json.loads(content)
        except json.JSONDecodeError as error:
            errors.append(f"Invalid JSON: {path} (line {error.lineno}, column {error.colno})")
            return None
        if not isinstance(document, dict) or not isinstance(document.get("dependencies"), dict):
            errors.append(f"Invalid dependencies object: {path}")
            return None
        return document["dependencies"]

    for path in (SCENE, "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/ProjectVersion.txt",
                 "Packages/manifest.json", "Packages/packages-lock.json"):
        if not (project / path).is_file():
            errors.append(f"Missing: {path}")
    if not (project / "Assets").is_dir():
        errors.append("Missing: Assets directory")
    if errors:
        return {"errors": errors, "checks": checks, "notes": notes}
    scene_text = read(SCENE)
    build = read("ProjectSettings/EditorBuildSettings.asset")
    editor_version = read("ProjectSettings/ProjectVersion.txt")
    if errors:
        return {"errors": errors, "checks": checks, "notes": notes}
    scene_errors, scene_checks = validate_scene(scene_text)
    errors.extend(scene_errors)
    checks.extend(scene_checks)
    if re.findall(r"- enabled: 1\n\s+path: ([^\n]+)", build) != [SCENE]:
        errors.append("Enabled build scenes must be exactly: " + SCENE)
    else:
        checks.append("Build scene")
    if not re.search(r"^m_EditorVersion: 6000\.2\.10f1$", editor_version, re.M):
        errors.append("Expected Unity 6000.2.10f1; review editor version before continuing")
    else:
        checks.append("Editor version")
    guid_paths = {}
    meta_errors_before = len(errors)
    for asset in (project / "Assets").rglob("*"):
        if asset.name.endswith(".meta") or any(part.startswith(".") for part in asset.relative_to(project).parts):
            continue
        if not Path(str(asset) + ".meta").is_file():
            errors.append(f"Missing meta: {asset.relative_to(project)}")
    for meta in (project / "Assets").rglob("*.meta"):
        content = read(meta.relative_to(project))
        if content is None:
            continue
        match = re.search(r"^guid: ([0-9a-f]{32})$", content, re.M)
        if match:
            guid_paths.setdefault(match[1], []).append(meta)
        else:
            errors.append(f"Invalid meta GUID: {meta.relative_to(project)}")
    for guid, paths in guid_paths.items():
        if len(paths) > 1:
            errors.append(f"Duplicate GUID {guid}: {paths}")
    if len(errors) == meta_errors_before:
        checks.append(f"Assets meta completeness/unique GUIDs: {len(guid_paths)}")
    for name in ("ScreenshotManager", "CameraMaskController"):
        component = next((b for b in blocks(scene_text).values() if "::" + name + "\n" in b), "")
        match = re.search(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}),", component)
        expected = project / f"Assets/Script/{name}.cs.meta"
        if not match or guid_paths.get(match[1]) != [expected] or not Path(str(expected)[:-5]).is_file():
            errors.append(f"{name}: wrong or missing script GUID reference")
        else:
            checks.append(name + " script GUID")
    for field, expected in (("m_SerializedLibrary", "Assets/MyImageLibrary.asset"),
                            ("m_TrackedImagePrefab", "Assets/Wakayama_Root.prefab")):
        match = re.search(field + r": \{fileID: -?\d+, guid: ([0-9a-f]{32}),", scene_text)
        if not match or guid_paths.get(match[1]) != [project / (expected + ".meta")] or not (project / expected).is_file():
            errors.append(f"AR {field}: expected {expected}; review any intentional routing change")
        else:
            checks.append(field)
    manifest = dependencies("Packages/manifest.json")
    lock = dependencies("Packages/packages-lock.json")
    if manifest is not None and lock is not None:
        package_errors_before = len(errors)
        for package, version in manifest.items():
            entry = lock.get(package)
            if not isinstance(version, str) or not isinstance(entry, dict) or entry.get("version") != version:
                errors.append(f"Manifest/lock mismatch: {package}")
        for package, entry in lock.items():
            if not isinstance(entry, dict) or not isinstance(entry.get("version"), str):
                errors.append(f"Invalid lock entry: {package}")
        if "com.unity.test-framework" not in lock:
            errors.append("Test Framework is missing")
        if len(errors) == package_errors_before:
            checks.append("Package manifest/lock")
    notes.append("ARContentChanger is not the production scene's content routing entry point.")
    notes.append("Static checks do not prove compilation, rendering, tracking or device gallery save.")
    return {"errors": errors, "checks": checks, "notes": notes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--project", type=Path, default=PROJECT, help="Unity project to inspect (read-only)")
    args = parser.parse_args()
    result = validate_project(args.project)
    if args.json:
        print(json.dumps(result, ensure_ascii=True, indent=2))
    else:
        for error in result["errors"]:
            print("ERROR: " + error)
        print(f"Static checks: {len(result['checks'])} passed, {len(result['errors'])} errors")
        for note in result["notes"]:
            print("NOTE: " + note)
    return bool(result["errors"])


if __name__ == "__main__":
    raise SystemExit(main())

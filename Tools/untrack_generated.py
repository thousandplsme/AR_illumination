"""Preview or untrack known generated files; never delete working-tree files."""
import argparse
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROJECT = "WakayamaAR_Photo(proto)"
TARGETS = [
    "D3D12", "MonoBleedingEdge", "UnityCrashHandler64.exe", "UnityPlayer.dll",
    PROJECT + "_Data", PROJECT + "_BurstDebugInformation_DoNotShip",
    PROJECT + "/Santa_BurstDebugInformation_DoNotShip",
    PROJECT + "/Wakayama_ARphoto(proto)_BurstDebugInformation_DoNotShip",
    PROJECT + "/WakayamaAR_Photo(proto)_BurstDebugInformation_DoNotShip",
    PROJECT + "/test_a_BackUpThisFolder_ButDontShipItWithYourGame",
    PROJECT + "/新しいフォルダー/test_BackUpThisFolder_ButDontShipItWithYourGame",
    PROJECT + "/新しいフォルダー/WakayamaAR_Photo(proto)_BurstDebugInformation_DoNotShip",
    PROJECT + "/UserSettings", PROJECT + "/WakayamaAR_Photo(proto).slnx",
]


def git(*args):
    return subprocess.check_output(["git", "-C", str(REPO), *args])


def checked_path(relative):
    path = (REPO / relative).resolve()
    if not path.is_relative_to(REPO) or path == REPO:
        raise ValueError("Path is outside the repository: " + relative)
    return path


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify(manifest_path):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for record in manifest["files"]:
        path = checked_path(record["path"])
        if not path.is_file() or sha256(path) != record["sha256"]:
            raise RuntimeError("Preservation check failed: " + record["path"])
    print(f"Preserved working files verified: {len(manifest['files'])}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    choice = parser.add_mutually_exclusive_group()
    choice.add_argument("--untrack", action="store_true")
    choice.add_argument("--verify", type=Path)
    args = parser.parse_args()
    if args.verify:
        verify(args.verify)
        return
    for target in TARGETS:
        checked_path(target)
        if any(target == PROJECT + "/" + name or target.startswith(PROJECT + "/" + name + "/")
               for name in ("Assets", "Packages", "ProjectSettings")):
            raise RuntimeError("Refusing to untrack project source/settings: " + target)
    entries = []
    for row in git("ls-files", "--stage", "-z", "--", *TARGETS).decode("utf-8").split("\0"):
        if not row:
            continue
        attributes, name = row.split("\t", 1)
        mode, blob, stage = attributes.split()
        if stage != "0" or mode not in ("100644", "100755"):
            raise RuntimeError("Refusing non-regular or unmerged file: " + name)
        path = checked_path(name)
        if not path.is_file():
            raise RuntimeError("Working file is missing; restore/review first: " + name)
        entries.append({"path": name, "gitBlob": blob, "bytes": path.stat().st_size})
    print(f"Generated/local tracked files: {len(entries)}, {sum(e['bytes'] for e in entries):,} bytes")
    if not args.untrack or not entries:
        print("No working-tree files changed.")
        return
    ignored = subprocess.run(
        ["git", "-C", str(REPO), "check-ignore", "--no-index", "-z", "--stdin"],
        input=("\0".join(e["path"] for e in entries) + "\0").encode("utf-8"),
        stdout=subprocess.PIPE, check=False,
    )
    ignored_paths = set(ignored.stdout.decode("utf-8").split("\0"))
    not_ignored = [e["path"] for e in entries if e["path"] not in ignored_paths]
    if ignored.returncode not in (0, 1) or not_ignored:
        raise RuntimeError("Add precise .gitignore rules before untracking: " + repr(not_ignored[:5]))
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    manifest_path = REPO / "Artifacts" / "GitCleanup" / (stamp + ".json")
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    for record in entries:
        record["sha256"] = sha256(checked_path(record["path"]))
    manifest_path.write_text(json.dumps({
        "sourceCommit": git("rev-parse", "HEAD").decode().strip(),
        "targets": TARGETS, "files": entries,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    # --cached changes only the index. No -f, working-tree delete, or history rewrite.
    result = subprocess.run(["git", "-C", str(REPO), "rm", "-r", "--cached", "--ignore-unmatch", "-q", "--", *TARGETS])
    if result.returncode:
        raise RuntimeError("Index update failed; preservation manifest: " + str(manifest_path))
    verify(manifest_path)
    print("Manifest: " + str(manifest_path))
    print("Index removals are staged; review before committing. Working files remain in place.")


if __name__ == "__main__":
    main()

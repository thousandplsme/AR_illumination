"""Validate NUnit reports and summarize one recorded run, using stdlib only."""
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path


def read_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig")), None
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        return None, f"Cannot read JSON {path.name}: {error}"


def test_report(path, suite=None):
    report = {"path": str(path), "valid": False, "passed": 0, "failed": 0, "total": 0, "skipped": 0}
    try:
        root = ET.parse(path).getroot()
        if root.tag != "test-run":
            raise ValueError("Expected NUnit test-run root")
        counts = {name: int(root.attrib[name]) for name in ("total", "passed", "failed", "skipped", "inconclusive")}
        cases = root.findall(".//test-case")
        if any(value < 0 for value in counts.values()):
            raise ValueError("Negative test count")
        if counts["total"] != sum(counts[n] for n in ("passed", "failed", "skipped", "inconclusive")):
            raise ValueError("Inconsistent aggregate test counts")
        if len(cases) != counts["total"] or any(
            sum(c.get("result") == label for c in cases) != counts[name]
            for label, name in (("Passed", "passed"), ("Failed", "failed"), ("Skipped", "skipped"), ("Inconclusive", "inconclusive"))
        ):
            raise ValueError("Test case results do not match aggregate counts")
        report.update(counts)
        report["result"] = root.get("result")
        report["failures"] = [
            {"name": case.get("fullname", case.get("name")), "message": case.findtext("failure/message", "")}
            for case in cases if case.get("result") == "Failed"
        ]
        if suite and any(not c.get("fullname", "").startswith(suite + ".") for c in cases):
            raise ValueError("Unexpected test suite")
        if root.get("result") != "Passed" or counts["passed"] == 0 or counts["failed"] or counts["inconclusive"]:
            raise ValueError("Expected a non-empty passing test run")
        report["valid"] = True
    except (OSError, ET.ParseError, ValueError, KeyError) as error:
        report["error"] = str(error)
    return report


def log_diagnostics(directory):
    found = []
    seen = set()
    for path in sorted(directory.glob("*.log")):
        for number, line in enumerate(path.read_text(encoding="utf-8-sig", errors="replace").splitlines(), 1):
            kind = None
            severity = "warning"
            if re.search(r"\berror CS\d+", line):
                kind, severity = "compiler", "error"
            elif re.search(r"\bwarning CS\d+", line):
                kind = "compiler"
            elif "Low scoring image report" in line or re.search(r"\| score: \d+/100", line):
                kind = "marker-score"
            elif "[Licensing::" in line and re.search(r"failed|Error|Timed-out|refused", line, re.I):
                kind, severity = "licensing", "diagnostic"
            elif "FAILURE: Build failed" in line or "BuildFailedException:" in line:
                kind, severity = "build", "error"
            key = (path.name, line.strip())
            if kind and key not in seen:
                seen.add(key)
                found.append({"file": path.name, "line": number, "kind": kind, "severity": severity, "message": line.strip()})
    return found


def summarize(directory):
    state, state_error = read_json(directory / "run-state.json")
    if not isinstance(state, dict):
        state = {}
    task = state.get("task")
    issues = [state_error] if state_error else []
    expected = ["Static validation", "Tool regression tests"]
    if task in ("Test", "BuildAndroid"):
        expected += ["EditMode", "EditMode report", "PlayMode", "PlayMode report"]
    if task == "BuildAndroid":
        expected += ["Android build", "APK verification"]
    if task not in ("Check", "Test", "BuildAndroid"):
        issues.append("Unknown or missing requested task")
    raw_steps = state.get("steps", [])
    if not isinstance(raw_steps, list):
        raw_steps = []
        issues.append("Invalid recorded steps")
    steps = []
    for name in expected:
        matches = [s for s in raw_steps if isinstance(s, dict) and s.get("step") == name]
        step = matches[0] if len(matches) == 1 else {"step": name, "status": "not_run", "seconds": None, "exitCode": None}
        steps.append(step)
        if step.get("status") != "passed" or step.get("exitCode") != 0:
            issues.append(name + ": not verified as passed")

    static, error = read_json(directory / "StaticValidation.json")
    if error:
        issues.append(error)
    if not isinstance(static, dict) or not isinstance(static.get("errors"), list) or not isinstance(static.get("checks"), list) or static["errors"]:
        issues.append("Static validation failed or produced no valid report")
    tools, error = read_json(directory / "ToolTests.json")
    if error:
        issues.append(error)
    if (not isinstance(tools, dict) or tools.get("successful") is not True or
            not isinstance(tools.get("testsRun"), int) or tools["testsRun"] <= 0 or
            tools.get("failures") != 0 or tools.get("errors") != 0):
        issues.append("Tool tests failed or produced no non-empty report")

    tests = {}
    if task in ("Test", "BuildAndroid"):
        for mode, suite in (("EditMode", "PhotoGeometryTests"), ("PlayMode", "PhotoWorkflowTests")):
            tests[mode] = test_report(directory / (mode + ".xml"), suite)
            if not tests[mode]["valid"]:
                issues.append(mode + ": " + tests[mode].get("error", "invalid report"))

    apk = {"status": "not_requested"}
    if task == "BuildAndroid":
        apk = {"status": "not_verified"}
        record = state.get("apk")
        if isinstance(record, dict) and any(s.get("step") == "APK verification" and s.get("status") == "passed" for s in steps):
            path = Path(record.get("path", ""))
            if path.is_file():
                with path.open("rb") as stream:
                    digest = hashlib.file_digest(stream, "sha256").hexdigest().upper()
                if digest == record.get("sha256"):
                    apk = {**record, "status": "verified", "bytes": path.stat().st_size}
        if apk["status"] != "verified":
            issues.append("No verified APK from this run; an existing APK is not a new build result")

    if state.get("outcome") != "passed":
        issues.append("Run did not finish successfully")
    failure = state.get("failure")
    if isinstance(failure, dict):
        issues.insert(0, str(failure.get("stage", "Unknown stage")) + ": " + str(failure.get("message", "Failed")))
    return {
        "schemaVersion": 1, "run": directory.name, "task": task,
        "outcome": "passed" if not issues else "failed",
        "startedAtUtc": state.get("startedAtUtc"), "finishedAtUtc": state.get("finishedAtUtc"),
        "failureStage": failure.get("stage") if isinstance(failure, dict) else next((s["step"] for s in steps if s.get("status") != "passed"), None),
        "steps": steps, "static": static, "toolTests": tools, "unityTests": tests, "apk": apk,
        "diagnostics": log_diagnostics(directory), "issues": issues,
        "limits": ["Log diagnostics use known patterns; consult full logs for other messages.",
                   "Passing checks do not verify real camera pixels, tracking, device save, visuals or iOS."],
    }


def write_summary(directory, report):
    (directory / "summary.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = ["# 検証結果", "", f"- 実行: {report['run']}", f"- タスク: {report['task']}",
             f"- 結果: {report['outcome']}"]
    if report["failureStage"]:
        lines.append("- 失敗段階: " + report["failureStage"])
    lines += ["", "## 実行段階", ""]
    for step in report["steps"]:
        lines.append(f"- {step['step']}: {step.get('status')} / {step.get('seconds')}秒 / exit={step.get('exitCode')}")
    if isinstance(report["static"], dict):
        lines += ["", f"静的検査: {len(report['static'].get('checks', [])) if isinstance(report['static'].get('checks'), list) else '?'}項目通過、{len(report['static'].get('errors', [])) if isinstance(report['static'].get('errors'), list) else '?'}件エラー。"]
    if isinstance(report["toolTests"], dict):
        lines.append(f"ツール回帰テスト: {report['toolTests'].get('testsRun')}件、スキップ={report['toolTests'].get('skipped', 0)}、成功={report['toolTests'].get('successful')}。")
    for mode, test in report["unityTests"].items():
        lines.append(f"{mode}: 合計{test['total']} / 成功{test['passed']} / 失敗{test['failed']} / スキップ{test['skipped']} / 検証={test['valid']}。")
    if report["apk"]["status"] == "verified":
        lines += ["", "## この実行のAPK", "", "- " + report["apk"]["path"], "- SHA-256: " + report["apk"]["sha256"]]
    else:
        lines += ["", "この実行でのAPK生成確認: " + report["apk"]["status"]]
    if report["issues"]:
        lines += ["", "## 未通過・未完了", ""] + ["- " + str(issue) for issue in report["issues"]]
    if report["diagnostics"]:
        lines += ["", "## ログの診断情報", ""]
        for item in report["diagnostics"]:
            lines.append(f"- [{item['file']}:{item['line']}] {item['severity']} / {item['kind']}: {item['message']}")
    lines += ["", "## 検証の境界", "", "既知のログパターンのみ抽出しています。その他の診断は元ログを参照してください。",
              "実カメラのGPU読取り、認識精度、端末保存、目視、iOS動作はこの結果では確認できません。", ""]
    (directory / "summary.md").write_text("\n".join(lines), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser("validate-tests")
    validate.add_argument("path", type=Path)
    validate.add_argument("--suite")
    summary = commands.add_parser("summarize")
    summary.add_argument("directory", type=Path)
    args = parser.parse_args()
    if args.command == "validate-tests":
        report = test_report(args.path, args.suite)
        print(json.dumps(report, ensure_ascii=True))
        return 0 if report["valid"] else 1
    report = summarize(args.directory)
    write_summary(args.directory, report)
    print(f"Run summary: {report['outcome']} - {args.directory / 'summary.md'}")
    return 0 if report["outcome"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())

import json
import hashlib
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_report import summarize, test_report, write_summary
from fixture_paths import new_fixture, remove_fixture


def passing_xml(path, suite="PhotoWorkflowTests"):
    root = ET.Element("test-run", result="Passed", total="1", passed="1", failed="0", skipped="0", inconclusive="0")
    ET.SubElement(root, "test-case", fullname=suite + ".Case", result="Passed")
    ET.ElementTree(root).write(path, encoding="utf-8")


class TestReportTests(unittest.TestCase):
    def setUp(self):
        self.directory = new_fixture()
        self.addCleanup(remove_fixture, self.directory)
        self.path = self.directory / "tests.xml"

    def test_nonempty_passing_report_is_accepted(self):
        passing_xml(self.path)
        self.assertTrue(test_report(self.path, "PhotoWorkflowTests")["valid"])

    def test_missing_report_is_rejected(self):
        self.assertFalse(test_report(self.path)["valid"])

    def test_malformed_report_is_rejected(self):
        self.path.write_text("<test-run")
        self.assertFalse(test_report(self.path)["valid"])

    def test_zero_tests_are_rejected(self):
        self.path.write_text('<test-run result="Passed" total="0" passed="0" failed="0" skipped="0" inconclusive="0"/>')
        self.assertFalse(test_report(self.path)["valid"])

    def test_failed_cases_are_rejected(self):
        passing_xml(self.path)
        data = self.path.read_text().replace('result="Passed"', 'result="Failed"').replace('passed="1"', 'passed="0"').replace('failed="0"', 'failed="1"')
        self.path.write_text(data)
        report = test_report(self.path)
        self.assertFalse(report["valid"])
        self.assertEqual(report["failed"], 1)

    def test_inconsistent_case_count_is_rejected(self):
        passing_xml(self.path)
        self.path.write_text(self.path.read_text().replace('total="1"', 'total="2"'))
        self.assertFalse(test_report(self.path)["valid"])

    def test_fake_pass_without_passed_cases_is_rejected(self):
        passing_xml(self.path)
        tree = ET.parse(self.path)
        tree.getroot().find("test-case").set("result", "Failed")
        tree.write(self.path)
        self.assertFalse(test_report(self.path)["valid"])

    def test_wrong_suite_is_rejected(self):
        passing_xml(self.path, "OtherSuite")
        self.assertFalse(test_report(self.path, "PhotoWorkflowTests")["valid"])

    def test_invalid_counts_and_root_are_rejected(self):
        for data in ('<other/>', '<test-run total="x"/>', '<test-run/>'):
            with self.subTest(data=data):
                self.path.write_text(data)
                self.assertFalse(test_report(self.path)["valid"])

    def test_cli_returns_nonzero_and_json_for_missing_xml(self):
        result = subprocess.run([sys.executable, "-B", str(Path(__file__).resolve().parents[1] / "run_report.py"),
                                 "validate-tests", str(self.path)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertFalse(json.loads(result.stdout)["valid"])
        self.assertNotIn("Traceback", result.stderr)


class RunSummaryTests(unittest.TestCase):
    def setUp(self):
        self.directory = new_fixture()
        self.addCleanup(remove_fixture, self.directory)
        self.state = {"task": "Check", "outcome": "passed", "failure": None, "steps": [
            {"step": name, "status": "passed", "exitCode": 0, "seconds": 0.1}
            for name in ("Static validation", "Tool regression tests")
        ]}
        self.dump("StaticValidation.json", {"errors": [], "checks": ["one"]})
        self.dump("ToolTests.json", {"testsRun": 1, "failures": 0, "errors": 0, "successful": True})
        self.dump_state()

    def dump(self, name, value):
        (self.directory / name).write_text(json.dumps(value), encoding="utf-8")

    def dump_state(self):
        self.dump("run-state.json", self.state)

    def prepare_test_run(self):
        self.state["task"] = "Test"
        for mode, suite in (("EditMode", "PhotoGeometryTests"), ("PlayMode", "PhotoWorkflowTests")):
            passing_xml(self.directory / (mode + ".xml"), suite)
            for name in (mode, mode + " report"):
                self.state["steps"].append({"step": name, "status": "passed", "exitCode": 0, "seconds": 0.1})
        self.dump_state()

    def test_check_summary_is_passed(self):
        report = summarize(self.directory)
        self.assertEqual(report["outcome"], "passed")
        write_summary(self.directory, report)
        self.assertTrue((self.directory / "summary.md").is_file())
        self.assertTrue((self.directory / "summary.json").is_file())

    def test_failure_preserves_stage_and_unexecuted_steps(self):
        self.state["task"] = "Test"
        self.state["outcome"] = "failed"
        self.state["failure"] = {"stage": "Static validation", "message": "Bad reference"}
        self.state["steps"][0].update(status="failed", exitCode=1)
        self.dump_state()
        report = summarize(self.directory)
        self.assertEqual(report["outcome"], "failed")
        self.assertEqual(report["failureStage"], "Static validation")
        self.assertTrue(any(step["status"] == "not_run" for step in report["steps"]))

    def test_old_apk_is_not_attached_to_a_test_run(self):
        self.prepare_test_run()
        path = self.directory / "old.apk"
        path.write_bytes(b"previous build")
        self.state["apk"] = {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper()}
        self.dump_state()
        self.assertEqual(summarize(self.directory)["apk"]["status"], "not_requested")

    def test_build_apk_hash_must_match_the_recorded_hash(self):
        self.prepare_test_run()
        path = self.directory / "current.apk"
        path.write_bytes(b"artifact fixture; this test does not validate Android packaging")
        self.state["task"] = "BuildAndroid"
        self.state["steps"] += [{"step": name, "status": "passed", "exitCode": 0}
                                for name in ("Android build", "APK verification")]
        self.state["apk"] = {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper()}
        self.dump_state()
        self.assertEqual(summarize(self.directory)["outcome"], "passed")
        path.write_bytes(b"changed after the run")
        report = summarize(self.directory)
        self.assertEqual(report["outcome"], "failed")
        self.assertEqual(report["apk"]["status"], "not_verified")

    def test_missing_test_xml_fails_even_if_exit_codes_passed(self):
        self.prepare_test_run()
        (self.directory / "PlayMode.xml").unlink()
        self.assertEqual(summarize(self.directory)["outcome"], "failed")

    def test_no_apk_record_cannot_prove_a_build(self):
        self.prepare_test_run()
        self.state["task"] = "BuildAndroid"
        self.state["steps"] += [{"step": name, "status": "passed", "exitCode": 0}
                                for name in ("Android build", "APK verification")]
        self.dump_state()
        report = summarize(self.directory)
        self.assertEqual(report["outcome"], "failed")
        self.assertEqual(report["apk"]["status"], "not_verified")

    def test_known_warnings_do_not_override_a_completed_test_run(self):
        self.prepare_test_run()
        (self.directory / "PlayMode.log").write_text(
            "[Licensing::Module] LicensingClient has failed validation; ignoring\n"
            "Low scoring image report\n- marker | score: 30/100\n", encoding="utf-8")
        report = summarize(self.directory)
        self.assertEqual(report["outcome"], "passed")
        self.assertEqual({item["kind"] for item in report["diagnostics"]}, {"licensing", "marker-score"})

    def test_missing_state_is_not_a_pass(self):
        (self.directory / "run-state.json").unlink()
        self.assertEqual(summarize(self.directory)["outcome"], "failed")

    def test_malformed_state_is_not_a_pass(self):
        (self.directory / "run-state.json").write_text("{")
        self.assertEqual(summarize(self.directory)["outcome"], "failed")

    def test_tool_failure_cannot_be_reported_as_success(self):
        self.dump("ToolTests.json", {"testsRun": 1, "failures": 1, "errors": 0, "successful": True})
        self.assertEqual(summarize(self.directory)["outcome"], "failed")

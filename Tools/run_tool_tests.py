"""Run tool regression tests and emit a machine-readable verdict on stdout."""
import json
import unittest
from pathlib import Path

if __name__ == "__main__":
    suite = unittest.defaultTestLoader.discover(str(Path(__file__).parent / "tests"))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    passed = result.wasSuccessful() and result.testsRun > 0
    print(json.dumps({"testsRun": result.testsRun, "failures": len(result.failures),
                      "errors": len(result.errors), "skipped": len(result.skipped), "successful": passed,
                      "details": [{"test": test.id(), "traceback": detail} for test, detail in result.failures + result.errors]}))
    raise SystemExit(0 if passed else 1)

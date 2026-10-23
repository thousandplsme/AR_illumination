import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from validate_project import PROJECT, SCENE, validate_scene


class ValidatorRegressionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.scene = (PROJECT / SCENE).read_text(encoding="utf-8-sig")

    def test_current_scene_passes(self):
        errors, _ = validate_scene(self.scene)
        self.assertEqual(errors, [])

    def test_non_finite_margin_is_detected(self):
        import re
        broken = re.sub(r"topMargin: [^\n]+", "topMargin: nan", self.scene)
        errors, _ = validate_scene(broken)
        self.assertTrue(any("non-negative" in error for error in errors))

    def test_missing_preview_reference_is_detected(self):
        import re
        broken = re.sub(r"previewImage: \{fileID: \d+\}", "previewImage: {fileID: 0}", self.scene)
        errors, _ = validate_scene(broken)
        self.assertTrue(any("previewImage" in error for error in errors))

    def test_wrong_button_method_is_detected(self):
        errors, _ = validate_scene(self.scene.replace("m_MethodName: OnClickSave", "m_MethodName: Typo"))
        self.assertTrue(any("OnClickSave" in error for error in errors))

    def test_independent_margin_is_detected(self):
        broken = self.scene.replace("  previewImage:", "  topMargin: 200\n  previewImage:")
        errors, _ = validate_scene(broken)
        self.assertTrue(any("separate topMargin" in error for error in errors))


if __name__ == "__main__":
    unittest.main()

"""Disposable, explicitly scoped test data inside the writable workspace."""
import shutil
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / "Artifacts" / "ToolTests"


def new_fixture():
    if not ROOT.resolve().is_relative_to(REPO.resolve()):
        raise ValueError("Test fixture root escaped the workspace")
    ROOT.mkdir(parents=True, exist_ok=True)
    path = ROOT / ("fixture-" + uuid.uuid4().hex)
    path.mkdir()
    return path


def remove_fixture(path):
    resolved = path.resolve()
    if not resolved.is_relative_to(ROOT.resolve()) or resolved == ROOT.resolve() or not path.name.startswith("fixture-"):
        raise ValueError("Refusing to remove a path outside the disposable fixture")
    shutil.rmtree(resolved)

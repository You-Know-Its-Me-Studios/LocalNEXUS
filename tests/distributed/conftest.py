"""Shared fixtures for the distributed package's tests.

The package lives in vendor/python rather than being installed, so the path is arranged here
once instead of in every test file. That mirrors how the application runs it: PYTHONPATH points
at the folder holding the package, and nothing is installed anywhere.
"""
import pathlib
import sys

import pytest

_PACKAGE_PARENT = pathlib.Path(__file__).resolve().parents[2] / "vendor" / "python"

if str(_PACKAGE_PARENT) not in sys.path:
    sys.path.insert(0, str(_PACKAGE_PARENT))


@pytest.fixture(scope="session")
def real_model_dir() -> pathlib.Path | None:
    """A real safetensors model on this machine, or None.

    Tests that need real weights skip without one rather than failing. A checkout has no models
    in it and never will, so a machine that cannot run those tests is the ordinary case and not
    a broken one.
    """
    import os

    root = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "LocalNEXUS" / "models" / "safetensors"

    if not root.is_dir():
        return None

    for candidate in sorted(root.iterdir()):
        if (candidate / "config.json").is_file():
            return candidate

    return None

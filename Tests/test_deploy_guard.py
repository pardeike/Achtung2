import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
GUARD = ROOT / "scripts/rimworld-deploy-guard.sh"


class DeployGuardTests(unittest.TestCase):
    def test_rejects_legacy_app_path_for_17(self):
        with tempfile.TemporaryDirectory() as temp:
            install = Path(temp) / "RimWorld"
            legacy = install / "RimWorldMac.app" / "Mods"
            legacy.mkdir(parents=True)
            (install / "Mods").symlink_to(legacy, target_is_directory=True)

            def validate(path):
                return subprocess.run(
                    [str(GUARD), "validate", str(path), str(ROOT / "Source/Achtung.csproj"), "Achtung"],
                    capture_output=True,
                    text=True,
                )

            self.assertEqual(0, validate(install / "Mods").returncode)
            wrong = validate(legacy)
            self.assertNotEqual(0, wrong.returncode)
            self.assertIn("targets RimWorld 1.7", wrong.stderr)


if __name__ == "__main__":
    unittest.main()

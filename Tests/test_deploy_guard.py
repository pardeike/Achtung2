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


    def test_running_game_blocks_only_its_own_installation(self):
        with tempfile.TemporaryDirectory() as temp:
            running = Path(temp) / "Running"
            other = Path(temp) / "Other"
            exe = running / "RimWorld.app" / "Contents" / "MacOS" / "RimWorld"
            exe.parent.mkdir(parents=True)
            exe.write_bytes(Path("/bin/sleep").read_bytes())
            exe.chmod(0o755)
            # macOS kills a copied system binary unless it is re-signed.
            subprocess.run(["codesign", "-s", "-", "-f", str(exe)], check=True, capture_output=True)
            (running / "Mods").mkdir()
            (other / "Mods").mkdir(parents=True)
            game = subprocess.Popen([str(exe), "30"])
            try:
                def check(*args):
                    return subprocess.run([str(GUARD), "check-stopped", *args], capture_output=True, text=True)

                own = check(str(running / "Mods"))
                self.assertEqual(2, own.returncode)
                self.assertIn(str(exe), own.stderr)
                self.assertNotIn(str(exe), check(str(other / "Mods")).stderr)
                self.assertIn(str(exe), check().stderr)
                # Another path to the same Mods folder must still block.
                alias = Path(temp) / "Alias"
                alias.mkdir()
                (alias / "Mods").symlink_to(running / "Mods", target_is_directory=True)
                self.assertIn(str(exe), check(str(alias / "Mods")).stderr)
            finally:
                game.kill()
                game.wait()


if __name__ == "__main__":
    unittest.main()

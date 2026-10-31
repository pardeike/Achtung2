import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts/check-settings-layout-release-gate.py"
spec = importlib.util.spec_from_file_location("settings_layout_gate", SCRIPT)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class VersionedWordingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.original_root = gate.REPO_ROOT
        gate.REPO_ROOT = self.root
        self.git("init", "-q")
        self.git("config", "user.name", "Test")
        self.git("config", "user.email", "test@example.invalid")
        self.write("Languages/English/Keyed/Text.xml", "<LanguageData><Title>Old</Title></LanguageData>")
        self.git("add", ".")
        self.git("commit", "-qm", "released")
        self.git("tag", "v1.0.0")

    def tearDown(self):
        gate.REPO_ROOT = self.original_root
        self.directory.cleanup()

    def git(self, *args):
        subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)

    def write(self, name, content):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def test_moved_unchanged_wording_is_not_a_change(self):
        self.write("1.7/Languages/English/Keyed/Text.xml", "<LanguageData><Title>Old</Title></LanguageData>")
        self.assertEqual([], gate.changed_wording_files("v1.0.0", None))
        self.assertEqual(["English"], gate.expected_languages(gate.working_wording_files()))

    def test_versioned_change_is_detected_in_worktree_and_commit(self):
        name = "1.7/Languages/English/Keyed/Text.xml"
        self.write(name, "<LanguageData><Title>New</Title></LanguageData>")
        self.assertEqual([name], gate.changed_wording_files("v1.0.0", None))
        self.git("add", ".")
        self.git("commit", "-qm", "versioned")
        self.assertEqual([name], gate.changed_wording_files("v1.0.0", "HEAD"))

    def test_missing_versioned_language_is_detected(self):
        self.write("1.7/Languages/German/Keyed/Text.xml", "<LanguageData><Title>Neu</Title></LanguageData>")
        self.assertEqual(
            ["Languages/English/Keyed/Text.xml", "1.7/Languages/German/Keyed/Text.xml"],
            gate.changed_wording_files("v1.0.0", None),
        )


if __name__ == "__main__":
    unittest.main()

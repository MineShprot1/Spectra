"""Run with the same Python environment as tools/testing_keys.py."""
import base64
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("testing_keys", ROOT / "tools/testing_keys.py")
keys = importlib.util.module_from_spec(spec)
spec.loader.exec_module(keys)


class GeneratorTests(unittest.TestCase):
    def test_generator_and_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            private, public = Path(directory) / "private.pem", Path(directory) / "public.pem"
            keys.initialize(private, public)
            with self.assertRaises(ValueError):
                keys.initialize(private, public)
            with self.assertRaises(ValueError):
                keys.issue(private, "Test", 90, "")
            with self.assertRaises(ValueError):
                keys.issue(private, "Test", 0, "github_pat_fixture_only")
            token = keys.issue(private, "Python integration", 1, "github_pat_fixture_only")
            prefix, data, signature = token.split(".")
            raw = base64.urlsafe_b64decode(signature + "=" * (-len(signature) % 4))
            keys.load_key(private).public_key().verify(raw, (prefix + "." + data).encode(),
                                                      keys.ec.ECDSA(keys.hashes.SHA256()))
            payload = json.loads(base64.urlsafe_b64decode(data + "=" * (-len(data) % 4)))
            self.assertEqual(payload["expires"] - payload["issued"], 86400)
            self.assertNotEqual(token, keys.issue(private, "Python integration", 1, "github_pat_fixture_only"))
            (ROOT / "test-results").mkdir(exist_ok=True)
            (ROOT / "test-results/testing-key-fixture.json").write_text(
                json.dumps({"key": token, "public": public.read_text()}), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()

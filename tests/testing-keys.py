"""Run with the same Python environment as tools/testing_keys.py."""
import base64
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import ctypes

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("testing_keys", ROOT / "tools/testing_keys.py")
keys = importlib.util.module_from_spec(spec)
spec.loader.exec_module(keys)


class GeneratorTests(unittest.TestCase):
    def test_clipboard_uses_valid_owner_and_unicode_without_touching_system(self):
        class Call:
            def __init__(self, function): self.function = function
            def __call__(self, *args): return self.function(*args)
        buffer = ctypes.create_string_buffer(256)
        copied, owners = [], []
        class Kernel:
            GetConsoleWindow = Call(lambda: 12345)
            GlobalAlloc = Call(lambda flags, size: ctypes.addressof(buffer))
            GlobalLock = Call(lambda handle: handle)
            GlobalUnlock = Call(lambda handle: 1)
            GlobalFree = Call(lambda handle: 0)
        class User:
            GetForegroundWindow = Call(lambda: 9999)
            OpenClipboard = Call(lambda owner: owners.append(owner) or 1)
            EmptyClipboard = Call(lambda: 1)
            CloseClipboard = Call(lambda: 1)
            SetClipboardData = Call(lambda kind, handle: copied.append((kind, handle)) or handle)
        class Fake:
            kernel32, user32 = Kernel(), User()
        with patch.object(keys.ctypes, 'windll', Fake()):
            keys.clipboard('Ключ SPTEST2')
        self.assertEqual(owners, [12345])
        self.assertEqual(copied[0][0], 13)
        self.assertEqual(buffer.raw[:len('Ключ SPTEST2'.encode('utf-16-le'))].decode('utf-16-le'), 'Ключ SPTEST2')

    def test_generator_and_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            private, public = Path(directory) / "private.pem", Path(directory) / "public.pem"
            keys.initialize(private, public)
            with self.assertRaises(ValueError):
                keys.initialize(private, public)
            with self.assertRaises(ValueError):
                keys.issue(private, "Test", 10, "http://invalid")
            with self.assertRaises(ValueError):
                keys.issue(private, "Test", 0, "https://testing.example")
            token = keys.issue(private, "Python integration", 10, "https://testing.example")
            prefix, data, signature = token.split(".")
            raw = base64.urlsafe_b64decode(signature + "=" * (-len(signature) % 4))
            keys.load_key(private).public_key().verify(raw, (prefix + "." + data).encode(),
                                                      keys.ec.ECDSA(keys.hashes.SHA256()))
            payload = json.loads(base64.urlsafe_b64decode(data + "=" * (-len(data) % 4)))
            self.assertEqual(payload["expires"] - payload["issued"], 600)
            self.assertNotEqual(token, keys.issue(private, "Python integration", 10, "https://testing.example"))
            (ROOT / "test-results").mkdir(exist_ok=True)
            (ROOT / "test-results/testing-key-fixture.json").write_text(
                json.dumps({"key": token, "public": public.read_text()}), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()

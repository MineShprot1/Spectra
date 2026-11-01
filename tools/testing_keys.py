"""Offline Spectra testing access keys. The private signer never enters a build."""
import argparse
import base64
import getpass
import ctypes
import hashlib
import urllib.request
import urllib.error
from urllib.parse import urlsplit
import json
import os
from pathlib import Path
import secrets
import sys
import time

try:
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import ec
except ImportError:
    sys.exit("Install the generator dependency: py -m pip install cryptography")

HOME = Path(os.environ.get("LOCALAPPDATA", Path.home())) / "SpectraDeveloper"
DEFAULT_PRIVATE = HOME / "testing-signing-key.pem"
DEFAULT_PUBLIC = Path(__file__).resolve().parents[1] / "src/Spectra/TestingPublicKey.pem"


def encode(value):
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def load_key(path):
    key = serialization.load_pem_private_key(path.read_bytes(), password=None)
    if not isinstance(key, ec.EllipticCurvePrivateKey) or key.curve.name != "secp256r1":
        raise ValueError("Expected a P-256 signing key")
    return key


def initialize(private, public):
    # Never silently replace the trust root or the developer's existing secret.
    if private.exists() or public.exists():
        raise ValueError("Key files already exist; use issue to create an access key")
    private.parent.mkdir(parents=True, exist_ok=True)
    public.parent.mkdir(parents=True, exist_ok=True)
    key = ec.generate_private_key(ec.SECP256R1())
    with private.open("xb") as out:
        out.write(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
                                    serialization.NoEncryption()))
    os.chmod(private, 0o600)
    with public.open("xb") as out:
        out.write(key.public_key().public_bytes(serialization.Encoding.PEM,
                                               serialization.PublicFormat.SubjectPublicKeyInfo))
    print("Private signer saved locally:", private)
    print("Public verification key for the launcher:", public)


def issue(private, label, minutes, service):
    url = urlsplit(service)
    if not 1 <= minutes <= 1440 or not label.strip() or len(label) > 80:
        raise ValueError("Use a label of 1–80 characters and a lifetime of 1–1440 minutes")
    if url.scheme != "https" or not url.netloc or url.username or url.password or url.path not in ("", "/") or url.query or url.fragment:
        raise ValueError("Use the HTTPS root URL of Spectra Network Dev")
    now = int(time.time())
    payload = {"v": 2, "purpose": "spectra-testing-enroll", "id": secrets.token_hex(16),
               "label": label.strip(), "issued": now, "expires": now + minutes * 60,
               "service": service.rstrip("/")}
    data = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    signed = b"SPTEST2." + encode(data).encode("ascii")
    signature = load_key(private).sign(signed, ec.ECDSA(hashes.SHA256()))
    return signed.decode("ascii") + "." + encode(signature)


class Blob(ctypes.Structure):
    _fields_ = [("size", ctypes.c_ulong), ("data", ctypes.c_void_p)]


def protect(raw, decrypt=False):
    if os.name != "nt":
        raise OSError("Developer settings use Windows DPAPI")
    buffer = ctypes.create_string_buffer(raw)
    source, target = Blob(len(raw), ctypes.cast(buffer, ctypes.c_void_p)), Blob()
    crypt = ctypes.windll.crypt32
    if decrypt:
        ok = crypt.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 0, ctypes.byref(target))
    else:
        ok = crypt.CryptProtectData(ctypes.byref(source), "Spectra developer settings", None, None, None, 0, ctypes.byref(target))
    if not ok:
        raise OSError("Could not protect developer settings")
    try:
        return ctypes.string_at(target.data, target.size)
    finally:
        ctypes.windll.kernel32.LocalFree(ctypes.c_void_p(target.data))


def clipboard(text):
    if os.name != "nt":
        raise OSError("Clipboard output requires Windows; use --no-copy --output instead")
    user, kernel = ctypes.windll.user32, ctypes.windll.kernel32
    kernel.GetConsoleWindow.restype = ctypes.c_void_p
    user.GetForegroundWindow.restype = ctypes.c_void_p
    user.OpenClipboard.argtypes = [ctypes.c_void_p]
    owner = kernel.GetConsoleWindow() or user.GetForegroundWindow()
    if not owner:
        raise OSError("Open the generator in a terminal to copy a key")
    kernel.GlobalAlloc.restype = ctypes.c_void_p
    kernel.GlobalAlloc.argtypes = [ctypes.c_uint, ctypes.c_size_t]
    kernel.GlobalLock.restype = ctypes.c_void_p
    kernel.GlobalLock.argtypes = [ctypes.c_void_p]
    kernel.GlobalUnlock.argtypes = [ctypes.c_void_p]
    kernel.GlobalFree.argtypes = [ctypes.c_void_p]
    user.SetClipboardData.argtypes = [ctypes.c_uint, ctypes.c_void_p]
    user.SetClipboardData.restype = ctypes.c_void_p
    raw = text.encode("utf-16-le") + b"\0\0"
    for attempt in range(20):
        if user.OpenClipboard(owner):
            break
        time.sleep(0.05)
    else:
        raise OSError("Clipboard is busy; run the generator again")
    handle = None
    try:
        user.EmptyClipboard()
        handle = kernel.GlobalAlloc(0x0002, len(raw))
        if not handle:
            raise OSError("Clipboard allocation failed")
        pointer = kernel.GlobalLock(handle)
        if not pointer:
            raise OSError("Clipboard lock failed")
        ctypes.memmove(pointer, raw, len(raw))
        kernel.GlobalUnlock(handle)
        if not user.SetClipboardData(13, handle):
            raise OSError("Could not copy the key")
        handle = None  # Windows owns the allocation after SetClipboardData.
    finally:
        if handle:
            kernel.GlobalFree(handle)
        user.CloseClipboard()


SETTINGS = HOME / "testing-service.dat"


def settings(setup=False):
    if SETTINGS.exists() and not setup:
        return json.loads(protect(SETTINGS.read_bytes(), decrypt=True))
    service = input("Spectra Network Dev HTTPS URL: ").strip().rstrip("/")
    if urlsplit(service).scheme != "https" or not urlsplit(service).netloc:
        raise ValueError("Enter the deployed Worker's HTTPS URL")
    admin = getpass.getpass("ADMIN_KEY (leave empty to create one): ").strip()
    if not admin:
        admin = secrets.token_hex(32)
        clipboard(admin)
        print("New ADMIN_KEY copied. Save it as the ADMIN_KEY secret in Cloudflare first.")
        input("After saving the secret, press Enter: ")
    value = {"service": service, "admin": admin}
    SETTINGS.parent.mkdir(parents=True, exist_ok=True)
    SETTINGS.write_bytes(protect(json.dumps(value).encode()))
    return value


def register(value, token):
    data = json.loads(base64.urlsafe_b64decode(token.split(".")[1] + "=" * (-len(token.split(".")[1]) % 4)))
    request = urllib.request.Request(value["service"] + "/admin/keys", method="POST",
        data=json.dumps({"keyHash": hashlib.sha256(token.encode()).hexdigest(), "expires": data["expires"], "label": data["label"]}).encode(),
        headers={"Authorization": "Bearer " + value["admin"], "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            if response.status != 201:
                raise ValueError("The server did not register the key")
    except urllib.error.HTTPError as error:
        raise ValueError("Key registration failed: HTTP " + str(error.code)) from None
    except urllib.error.URLError:
        raise ValueError("Spectra Network Dev is unreachable; check deployment and the saved URL") from None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--private", type=Path, default=DEFAULT_PRIVATE)
    commands = parser.add_subparsers(dest="command", required=True)
    init = commands.add_parser("init", help="Create a new signing identity only before rebuilding both launchers")
    init.add_argument("--public", type=Path, default=DEFAULT_PUBLIC)
    commands.add_parser("setup", help="Save the deployed service URL and admin secret locally")
    create = commands.add_parser("issue", help="Register a new single-use key and copy it to the clipboard")
    create.add_argument("--label", default="Developer")
    create.add_argument("--minutes", type=int, default=10)
    create.add_argument("--output", type=Path)
    create.add_argument("--no-copy", action="store_true")
    args = parser.parse_args(sys.argv[1:] or ["issue"])
    try:
        if args.command == "init":
            initialize(args.private, args.public)
        elif args.command == "setup":
            settings(setup=True)
            print("Developer settings saved with Windows protection.")
        else:
            value = settings()
            token = issue(args.private, args.label, args.minutes, value["service"])
            register(value, token)
            if args.output:
                with args.output.open("x", encoding="utf-8") as out:
                    out.write(token + "\n")
            if not args.no_copy:
                clipboard(token)
            print("New single-use key registered" + (" and copied to the clipboard." if not args.no_copy else "."))
    except (OSError, ValueError) as error:
        parser.exit(1, str(error) + "\n")


if __name__ == "__main__":
    main()

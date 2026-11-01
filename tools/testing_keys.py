"""Offline Spectra testing access keys. The private signer never enters a build."""
import argparse
import base64
import getpass
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


def issue(private, label, days, github_token):
    if not 1 <= days <= 3650 or not label.strip() or len(label) > 80:
        raise ValueError("Use a label of 1–80 characters and a lifetime of 1–3650 days")
    if not github_token.startswith("github_pat_") or len(github_token) > 512:
        raise ValueError("Set SPECTRA_TESTING_GITHUB_TOKEN to a fine-grained GitHub token for Spectra-Testing (Contents: Read)")
    now = int(time.time())
    payload = {"v": 1, "purpose": "spectra-testing", "id": secrets.token_hex(16),
               "label": label.strip(), "issued": now, "expires": now + days * 86400,
               "githubToken": github_token}
    data = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    signed = b"SPTEST1." + encode(data).encode("ascii")
    signature = load_key(private).sign(signed, ec.ECDSA(hashes.SHA256()))
    return signed.decode("ascii") + "." + encode(signature)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--private", type=Path, default=DEFAULT_PRIVATE)
    commands = parser.add_subparsers(dest="command", required=True)
    init = commands.add_parser("init", help="Create the local signing identity once, before building")
    init.add_argument("--public", type=Path, default=DEFAULT_PUBLIC)
    create = commands.add_parser("issue", help="Create a signed testing access key")
    create.add_argument("--label", default="Developer")
    create.add_argument("--days", type=int, default=90)
    create.add_argument("--output", type=Path, help="Save key to a local file instead of stdout")
    create.add_argument("--token-env", default="SPECTRA_TESTING_GITHUB_TOKEN", help="Environment variable holding the repo-only read token")
    create.add_argument("--prompt-token", action="store_true", help="Enter the GitHub read token without echoing it")
    args = parser.parse_args()
    try:
        if args.command == "init":
            initialize(args.private, args.public)
        else:
            github_token = getpass.getpass("Spectra-Testing Contents: Read token: ") if args.prompt_token else os.environ.get(args.token_env, "")
            token = issue(args.private, args.label, args.days, github_token.strip())
            if args.output:
                with args.output.open("x", encoding="utf-8") as out:
                    out.write(token + "\n")
                print("Access key saved:", args.output)
            else:
                print(token)
    except (OSError, ValueError) as error:
        parser.exit(1, str(error) + "\n")


if __name__ == "__main__":
    main()

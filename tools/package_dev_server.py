import argparse
from pathlib import Path
import shutil
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("destination", type=Path)
args = parser.parse_args()
destination = args.destination.resolve()
destination.mkdir(parents=True, exist_ok=True)
names = ["worker.mjs", "schema.sql", "wrangler.jsonc", "README.md", "requirements.txt",
         "Создать ключ.cmd", "Настроить генератор.cmd", "Обновить сервер.cmd"]
for name in names:
    shutil.copy2(root / "dev-server" / name, destination / name)
shutil.copy2(root / "tools/testing_keys.py", destination / "testing_keys.py")
shutil.copy2(root / "src/Spectra/TestingPublicKey.pem", destination / "TestingPublicKey.pem")
archive = destination.with_suffix(".zip")
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
    for name in names + ["testing_keys.py", "TestingPublicKey.pem"]:
        output.write(destination / name, destination.name + "/" + name)
print(destination)
print(archive)

#!/usr/bin/env python3
"""Validate a Linux release checksum and archive before extraction/privileged install."""
import argparse
import hashlib
import hmac
import pathlib
import re
import shutil
import tarfile

SEMVER = re.compile(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\Z")
MODEL_HASH = "b2bc52f40e8e1c532427d5bde3575a5d5b571b739fab2c6df443733ed1589cbd"
REQUIRED = {"VERSION", "README.md", "app/ezviz-headless", "app/Models/yolov8n.onnx",
            "app/ezviz-headless.deps.json", "installer/linux/common.sh", "installer/linux/install.sh",
            "installer/linux/update.sh", "installer/linux/uninstall.sh", "installer/linux/ezviz-local-monitor.service"}


def verify(archive, checksum, destination=None):
    archive, checksum = pathlib.Path(archive), pathlib.Path(checksum)
    expected_line = checksum.read_text(encoding="ascii").strip()
    match = re.fullmatch(r"([0-9a-fA-F]{64})  ([^/\\\r\n]+)", expected_line)
    if not match or match[2] != archive.name:
        raise ValueError("Checksum must name the exact archive.")
    with archive.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
    if not hmac.compare_digest(actual, match[1].lower()):
        raise ValueError("Archive checksum mismatch.")
    with tarfile.open(archive, "r:gz") as bundle:
        members = bundle.getmembers()
        names = set()
        total = 0
        for item in members:
            path = pathlib.PurePosixPath(item.name)
            if (path.is_absolute() or ".." in path.parts or not path.parts or "\\" in item.name
                    or path.parts[0] not in ("app", "installer", "VERSION", "README.md")
                    or item.name.rstrip("/") != path.as_posix()
                    or (path.parts[0] in ("VERSION", "README.md") and len(path.parts) != 1)):
                raise ValueError("Unsafe archive path.")
            if item.name in names:
                raise ValueError("Duplicate archive member.")
            names.add(item.name)
            if not (item.isfile() or item.isdir()):
                raise ValueError("Archive links/special files are not supported.")
            if path.name == ".env" or path.suffix in (".protected", ".db", ".key", ".pdb") or path.name.endswith((".db-wal", ".db-shm")):
                raise ValueError("Local state or debug files must not be distributed.")
            total += item.size
            if len(names) > 2000 or total > 1024 * 1024 * 1024:
                raise ValueError("Archive exceeds supported limits.")
        if not REQUIRED.issubset(names):
            raise ValueError("Required package files missing.")
        if any(not bundle.getmember(name).isfile() for name in REQUIRED):
            raise ValueError("Required package entries must be regular files.")
        if bundle.getmember("VERSION").size > 128:
            raise ValueError("Package version is too large.")
        version_bytes = bundle.extractfile("VERSION").read()
        version = version_bytes.decode("ascii").strip()
        if version_bytes != (version + "\n").encode("ascii") or not SEMVER.fullmatch(version):
            raise ValueError("Invalid package version.")
        if archive.name != f"EZVIZ-Local-Monitor-Linux-Headless-x64-v{version}.tar.gz":
            raise ValueError("Archive filename/version mismatch.")
        if hashlib.file_digest(bundle.extractfile("app/Models/yolov8n.onnx"), "sha256").hexdigest() != MODEL_HASH:
            raise ValueError("Model hash mismatch.")
        for name in ("app/ezviz-headless", "installer/linux/install.sh", "installer/linux/update.sh", "installer/linux/uninstall.sh"):
            if not bundle.getmember(name).mode & 0o111:
                raise ValueError("Executable package files need executable permissions.")
        if destination is not None:
            destination = pathlib.Path(destination)
            if destination.is_symlink() or (destination.exists() and any(destination.iterdir())):
                raise ValueError("Extraction destination must be a new/empty real directory.")
            destination.mkdir(parents=True, exist_ok=True)
            # All members have been checked before any extraction occurs.
            for item in members:
                target = destination.joinpath(*pathlib.PurePosixPath(item.name).parts)
                if item.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                    target.chmod(0o755)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with bundle.extractfile(item) as source, target.open("xb") as output:
                        shutil.copyfileobj(source, output)
                    target.chmod(0o755 if item.mode & 0o111 else 0o644)
    return version


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("archive")
    parser.add_argument("checksum")
    parser.add_argument("--extract")
    args = parser.parse_args()
    try:
        version = verify(args.archive, args.checksum, args.extract)
        print(f"PASS: checksum, safe archive, model and package version {version}")
    except (ValueError, OSError, tarfile.TarError, UnicodeError) as error:
        parser.exit(1, f"Package rejected: {error}\n")

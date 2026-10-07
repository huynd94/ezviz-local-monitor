#!/usr/bin/env python3
import hashlib
import importlib.util
import io
import pathlib
import tarfile
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("verify_package", ROOT / "scripts/linux/Verify-Package.py")
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class SecurityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        self.archive = self.root / "fixture.tar.gz"
        self.checksum = self.root / "fixture.tar.gz.sha256sum"

    def tearDown(self):
        self.temp.cleanup()

    def bundle(self, name, kind=tarfile.REGTYPE):
        with tarfile.open(self.archive, "w:gz") as archive:
            item = tarfile.TarInfo(name)
            item.type = kind
            if kind == tarfile.REGTYPE:
                item.size = 1
                archive.addfile(item, io.BytesIO(b"x"))
            else:
                item.linkname = "/tmp/outside"
                archive.addfile(item)
        digest = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.checksum.write_text(f"{digest}  {self.archive.name}\n")

    def test_traversal_and_absolute_paths_never_extract(self):
        for name in ("../outside", "/tmp/outside", "app/../../outside", "app\\..\\outside", "unknown/payload"):
            with self.subTest(name=name):
                self.bundle(name)
                target = self.root / "extract"
                with self.assertRaisesRegex(ValueError, "Unsafe archive path"):
                    VERIFY.verify(self.archive, self.checksum, target)
                self.assertFalse(target.exists())

    def test_links_and_special_files_never_extract(self):
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.FIFOTYPE, tarfile.CHRTYPE):
            with self.subTest(kind=kind):
                self.bundle("app/payload", kind)
                with self.assertRaisesRegex(ValueError, "links/special"):
                    VERIFY.verify(self.archive, self.checksum)

    def test_wrong_checksum_or_archive_name_rejected(self):
        self.bundle("app/payload")
        self.checksum.write_text("0" * 64 + f"  {self.archive.name}\n")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            VERIFY.verify(self.archive, self.checksum)
        self.checksum.write_text("0" * 64 + "  other.tar.gz\n")
        with self.assertRaisesRegex(ValueError, "exact archive"):
            VERIFY.verify(self.archive, self.checksum)

    def test_protected_state_is_rejected(self):
        for name in ("app/settings.protected", "app/events.db", "app/master.key", "app/.env", "app/events.db-wal"):
            with self.subTest(name=name):
                self.bundle(name)
                with self.assertRaisesRegex(ValueError, "Local state"):
                    VERIFY.verify(self.archive, self.checksum)

    def test_noncanonical_paths_and_nested_root_files_rejected(self):
        for name in ("app/./payload", "app//payload", "VERSION/child", "README.md/child"):
            with self.subTest(name=name):
                self.bundle(name)
                with self.assertRaisesRegex(ValueError, "Unsafe archive path"):
                    VERIFY.verify(self.archive, self.checksum)


if __name__ == "__main__":
    unittest.main(verbosity=2)

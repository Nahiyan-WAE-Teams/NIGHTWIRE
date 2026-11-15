#!/usr/bin/env python3
"""Validate the built NIGHTWIRE Android secure-store AAR and backup exclusions."""

from __future__ import annotations

import sys
import zipfile
from io import BytesIO
from pathlib import Path
import xml.etree.ElementTree as ET

ANDROID = "{http://schemas.android.com/apk/res/android}"
PREFS_FILE = "nightwire_catalogue_sequence_v1.xml"


def validate(aar_path: Path) -> list[str]:
    errors: list[str] = []
    if not aar_path.is_file():
        return [f"AAR artifact does not exist: {aar_path}"]

    try:
        with zipfile.ZipFile(aar_path) as aar:
            names = set(aar.namelist())
            required = {
                "AndroidManifest.xml",
                "classes.jar",
                "res/xml/nightwire_backup_rules.xml",
                "res/xml/nightwire_data_extraction_rules.xml",
            }
            missing = sorted(required - names)
            if missing:
                errors.append("AAR is missing required entries: " + ", ".join(missing))
                return errors

            manifest = ET.fromstring(aar.read("AndroidManifest.xml"))
            application = manifest.find("application")
            if application is None:
                errors.append("AAR manifest has no application element.")
            else:
                if application.get(ANDROID + "fullBackupContent") != "@xml/nightwire_backup_rules":
                    errors.append("AAR manifest does not apply the legacy backup exclusion rules.")
                if application.get(ANDROID + "dataExtractionRules") != "@xml/nightwire_data_extraction_rules":
                    errors.append("AAR manifest does not apply Android 12+ data extraction rules.")

            legacy = ET.fromstring(aar.read("res/xml/nightwire_backup_rules.xml"))
            legacy_excludes = {
                (node.get("domain"), node.get("path"))
                for node in legacy.findall(".//exclude")
            }
            if ("sharedpref", PREFS_FILE) not in legacy_excludes:
                errors.append("Legacy backup rules do not exclude the StrongBox sequence preference file.")

            extraction = ET.fromstring(aar.read("res/xml/nightwire_data_extraction_rules.xml"))
            for section in ("cloud-backup", "device-transfer"):
                node = extraction.find(section)
                excludes = set() if node is None else {
                    (item.get("domain"), item.get("path"))
                    for item in node.findall(".//exclude")
                }
                if ("sharedpref", PREFS_FILE) not in excludes:
                    errors.append(f"{section} rules do not exclude the device-bound StrongBox sequence file.")

            with zipfile.ZipFile(BytesIO(aar.read("classes.jar"))) as classes:
                class_name = "com/nightwire/securestore/NightwireSequenceStore.class"
                if class_name not in classes.namelist():
                    errors.append("AAR classes.jar does not contain NightwireSequenceStore.")
    except (OSError, zipfile.BadZipFile, ET.ParseError, KeyError) as exc:
        errors.append(f"Could not inspect AAR: {exc}")

    return errors


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: validate_android_aar.py path/to/NightwireSecureStore.aar")
        return 2
    errors = validate(Path(sys.argv[1]))
    if errors:
        for error in errors:
            print("FAIL:", error)
        return 1
    print("PASS: Android secure-store AAR contains the JNI provider and StrongBox backup exclusions.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

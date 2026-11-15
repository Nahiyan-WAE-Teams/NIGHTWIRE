#!/usr/bin/env python3
"""Static contract checks for the NIGHTWIRE Unity UI source tree."""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UNITY = ROOT / "unity"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def validate_project(require_staged_runtime: bool = False) -> list[str]:
    errors: list[str] = []

    version_file = UNITY / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.exists() or "m_EditorVersion: 6000.3.28f1" not in _read(version_file):
        errors.append("Unity project must be pinned to patched Unity 6000.3.28f1.")

    manifest_file = UNITY / "Packages" / "manifest.json"
    try:
        manifest = json.loads(_read(manifest_file))
        dependencies = manifest.get("dependencies")
        if not isinstance(dependencies, dict):
            errors.append("Unity Packages/manifest.json must contain a dependencies object.")
        else:
            required_modules = {
                "com.unity.modules.audio",
                "com.unity.modules.uielements",
                "com.unity.modules.video",
            }
            missing_modules = sorted(required_modules - set(dependencies))
            if missing_modules:
                errors.append("Unity package manifest is missing built-in modules: " + ", ".join(missing_modules))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"Unity package manifest is missing or invalid: {exc}")

    asmdef_file = UNITY / "Assets" / "Nightwire" / "Scripts" / "Nightwire.Unity.asmdef"
    try:
        asmdef = json.loads(_read(asmdef_file))
        required_references = {
            "UnityEngine.UIElementsModule",
            "UnityEngine.VideoModule",
            "UnityEngine.AudioModule",
            "Unity.Purchasing",
        }
        missing_references = sorted(required_references - set(asmdef.get("references", [])))
        if missing_references:
            errors.append("Nightwire.Unity.asmdef is missing assembly references: " + ", ".join(missing_references))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"Unity assembly definition is missing or invalid: {exc}")

    ui_pairs = [
        (
            UNITY / "Assets" / "Nightwire" / "UI" / "EpisodeLibraryView.uxml",
            UNITY / "Assets" / "Nightwire" / "Scripts" / "EpisodeLibraryViewController.cs",
        ),
        (
            UNITY / "Assets" / "Nightwire" / "UI" / "MessengerScreen.uxml",
            UNITY / "Assets" / "Nightwire" / "Scripts" / "MessengerScreenController.cs",
        ),
    ]

    for uxml_path, controller_path in ui_pairs:
        try:
            tree = ET.parse(uxml_path)
            root = tree.getroot()
            names = {
                element.attrib["name"]
                for element in root.iter()
                if "name" in element.attrib
            }
            for element in root.iter():
                if not element.tag.endswith("Style"):
                    continue
                source = element.attrib.get("src", "")
                if source.startswith("project://database/"):
                    source = source[len("project://database/"):]
                if source and not (UNITY / source).exists():
                    errors.append(f"{uxml_path.relative_to(ROOT)} references missing stylesheet: {source}")
        except (OSError, ET.ParseError) as exc:
            errors.append(f"UXML is missing or invalid ({uxml_path.relative_to(ROOT)}): {exc}")
            continue

        try:
            controller = _read(controller_path)
        except OSError as exc:
            errors.append(f"UI controller is missing ({controller_path.relative_to(ROOT)}): {exc}")
            continue

        queried_names = set(re.findall(r'\.Q<[^>]+>\("([^"]+)"\)', controller))
        missing = sorted(queried_names - names)
        if missing:
            errors.append(
                f"{controller_path.name} queries UXML names that do not exist: {', '.join(missing)}"
            )

    for uss_path in (UNITY / "Assets" / "Nightwire" / "UI").glob("*.uss"):
        source = _read(uss_path)
        without_comments = re.sub(r"/\*.*?\*/", "", source, flags=re.S)
        if without_comments.count("{") != without_comments.count("}"):
            errors.append(f"USS braces are unbalanced: {uss_path.relative_to(ROOT)}")

    required_files = [
        ROOT / "tools" / "prepare-unity-runtime.sh",
        ROOT / "tools" / "Prepare-UnityRuntime.ps1",
        UNITY / "Assets" / "Editor" / "NightwireProjectBootstrapper.cs",
        UNITY / "Assets" / "Nightwire" / "Scripts" / "NightwireDevelopmentPlatformServices.cs",
        UNITY / "Assets" / "Nightwire" / "Scripts" / "NightwireBundledEpisodeSeeder.cs",
        UNITY / "Assets" / "Nightwire" / "Scripts" / "NightwireAtmosphereController.cs",
    ]
    for path in required_files:
        if not path.is_file():
            errors.append(f"Required Unity integration file is missing: {path.relative_to(ROOT)}")

    if require_staged_runtime:
        runtime_dll = UNITY / "Assets" / "Plugins" / "Nightwire" / "Nightwire.Runtime.dll"
        if not runtime_dll.is_file():
            errors.append("Nightwire.Runtime.dll was not staged into unity/Assets/Plugins/Nightwire.")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-staged-runtime", action="store_true")
    args = parser.parse_args()
    errors = validate_project(args.require_staged_runtime)
    if errors:
        for error in errors:
            print("FAIL:", error)
        return 1
    print("PASS: Unity project baseline and package manifest")
    print("PASS: Unity module/IAP assembly references and runtime staging scripts")
    print("PASS: UXML documents, controller selectors, and USS references")
    print("PASS: Unity editor scene bootstrapper and bundled demo integration")
    if args.require_staged_runtime:
        print("PASS: Portable runtime assembly staged for Unity")
    return 0


if __name__ == "__main__":
    sys.exit(main())

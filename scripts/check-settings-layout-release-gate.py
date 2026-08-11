#!/usr/bin/env python3
"""Require fresh live settings-layout evidence only for releases with wording changes."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import NoReturn


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_EVIDENCE = Path("TestEvidence/SettingsLayout.json")
WORDING_PATH_PATTERN = re.compile(r"Languages/[^/]+/Keyed/[^/]+\.xml")
RELEASE_TAG_PATTERN = re.compile(r"v\d+(?:\.\d+)+")
AUDIT_SOURCE_PATHS = (
    Path("Source/Settings.cs"),
    Path("Source/Tools.cs"),
    Path("Source/BridgeTools/AchtungBridgeTools.Localization.cs"),
)
EXPECTED_GEOMETRY = {
    "columnWidth": 399,
    "headerWidth": 423,
    "subheadingWidth": 387,
    "minimumGap": 12,
}
EXPECTED_TITLE_VALUE_CHECKS = 33
EXPECTED_FIXED_TEXT_CHECKS = 8
EXPECTED_MEASUREMENT_TOOL = "achtung/audit_settings_layout"


def fail(message: str) -> NoReturn:
    print(f"settings-layout-release-gate: {message}", file=sys.stderr)
    raise SystemExit(1)


def git(*arguments: str) -> str:
    result = subprocess.run(
        ["git", *arguments],
        cwd=REPO_ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip() or "git command failed"
        fail(f"git {' '.join(arguments)}: {detail}")
    return result.stdout.strip()


def git_bytes(*arguments: str) -> bytes:
    result = subprocess.run(
        ["git", *arguments],
        cwd=REPO_ROOT,
        check=False,
        capture_output=True,
    )
    if result.returncode != 0:
        detail = result.stderr.decode("utf-8", errors="replace").strip() or "git command failed"
        fail(f"git {' '.join(arguments)}: {detail}")
    return result.stdout


def commit_for(ref: str) -> str:
    return git("rev-parse", "--verify", f"{ref}^{{commit}}")


def resolve_previous_release_tag(target_ref: str | None) -> str:
    target_commit = commit_for(target_ref or "HEAD")
    tags = git("tag", "--merged", target_commit, "--sort=-version:refname").splitlines()
    for tag in tags:
        if RELEASE_TAG_PATTERN.fullmatch(tag) is None:
            continue
        if commit_for(tag) == target_commit:
            continue
        return tag
    fail(f"could not find a previous semantic release tag reachable from {target_ref or 'HEAD'}")


def wording_files_at_ref(ref: str) -> list[Path]:
    return sorted(
        Path(path)
        for path in git("ls-tree", "-r", "--name-only", ref, "--", "Languages").splitlines()
        if WORDING_PATH_PATTERN.fullmatch(path)
    )


def working_wording_files() -> list[Path]:
    return sorted(
        path.relative_to(REPO_ROOT)
        for path in REPO_ROOT.glob("Languages/*/Keyed/*.xml")
        if path.is_file() and WORDING_PATH_PATTERN.fullmatch(path.relative_to(REPO_ROOT).as_posix())
    )


def content_for(path: Path, ref: str | None) -> bytes:
    if ref is None:
        try:
            return (REPO_ROOT / path).read_bytes()
        except OSError as error:
            fail(f"cannot read {path}: {error}")
    return git_bytes("show", f"{ref}:{path.as_posix()}")


def translation_entries(path: Path, content: bytes, ref: str) -> dict[str, str]:
    try:
        root = ET.fromstring(content)
    except ET.ParseError as error:
        fail(f"cannot parse {path} from {ref}: {error}")
    entries: dict[str, str] = {}
    for element in root:
        if not isinstance(element.tag, str):
            continue
        if element.tag in entries:
            fail(f"{path} from {ref} contains duplicate key {element.tag}")
        entries[element.tag] = "".join(element.itertext())
    return entries


def changed_wording_files(base_ref: str, target_ref: str | None) -> list[str]:
    base_files = set(wording_files_at_ref(base_ref))
    target_files = set(wording_files_at_ref(target_ref)) if target_ref else set(working_wording_files())
    changed: list[str] = []
    for path in sorted(base_files | target_files):
        if path not in base_files or path not in target_files:
            changed.append(path.as_posix())
            continue
        base_entries = translation_entries(path, content_for(path, base_ref), base_ref)
        target_name = target_ref or "working-tree"
        target_entries = translation_entries(path, content_for(path, target_ref), target_name)
        if base_entries != target_entries:
            changed.append(path.as_posix())
    return changed


def path_exists_at_ref(path: Path, ref: str) -> bool:
    return bool(git("ls-tree", "--name-only", ref, "--", path.as_posix()))


def audit_input_files(target_ref: str | None) -> list[Path]:
    wording_files = wording_files_at_ref(target_ref) if target_ref else working_wording_files()
    if not wording_files:
        fail("no keyed language files were found")

    paths = [*wording_files, *AUDIT_SOURCE_PATHS]
    if target_ref:
        missing = [str(path) for path in paths if not path_exists_at_ref(path, target_ref)]
    else:
        missing = [str(path) for path in paths if not (REPO_ROOT / path).is_file()]
    if missing:
        fail("layout audit inputs are missing: " + ", ".join(missing))
    return paths


def input_digest(paths: list[Path], target_ref: str | None) -> str:
    digest = hashlib.sha256()
    for path in paths:
        digest.update(path.as_posix().encode("utf-8"))
        digest.update(b"\0")
        digest.update(content_for(path, target_ref))
        digest.update(b"\0")
    return "sha256:" + digest.hexdigest()


def expected_languages(paths: list[Path]) -> list[str]:
    return sorted({path.parts[1] for path in paths if path.parts[0] == "Languages"})


def read_evidence(path: Path, target_ref: str | None) -> dict[str, object]:
    full_path = path if path.is_absolute() else REPO_ROOT / path
    try:
        if target_ref is not None and not path.is_absolute():
            if not path_exists_at_ref(path, target_ref):
                fail(
                    f"wording changed, but {path} is missing from {target_ref}; "
                    "rerun the live 12-language settings-layout matrix"
                )
            content = content_for(path, target_ref).decode("utf-8")
        else:
            if not full_path.is_file():
                label = full_path.relative_to(REPO_ROOT) if full_path.is_relative_to(REPO_ROOT) else full_path
                fail(
                    f"wording changed, but {label} is missing; "
                    "rerun the live 12-language settings-layout matrix"
                )
            content = full_path.read_text(encoding="utf-8")
        value = json.loads(content)
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        fail(f"cannot read layout evidence {full_path}: {error}")
    if not isinstance(value, dict):
        fail(f"layout evidence {full_path} must contain one JSON object")
    return value


def verify_evidence(
    evidence: dict[str, object],
    base_ref: str,
    digest: str,
    languages: list[str],
) -> None:
    errors: list[str] = []
    if evidence.get("schemaVersion") != 1:
        errors.append("schemaVersion must be 1")
    if evidence.get("baseTag") != base_ref:
        errors.append(f"baseTag must be {base_ref!r}")
    if evidence.get("inputDigest") != digest:
        errors.append("inputDigest is stale for the current wording/layout inputs")
    if evidence.get("passed") is not True:
        errors.append("passed must be true")
    if evidence.get("measurementTool") != EXPECTED_MEASUREMENT_TOOL:
        errors.append(f"measurementTool must be {EXPECTED_MEASUREMENT_TOOL!r}")

    geometry = evidence.get("geometry")
    if geometry != EXPECTED_GEOMETRY:
        errors.append(f"geometry must equal {EXPECTED_GEOMETRY!r}")

    results = evidence.get("results")
    if not isinstance(results, list):
        errors.append("results must be an array")
        results = []

    result_languages = [result.get("language") for result in results if isinstance(result, dict)]
    if not all(isinstance(language, str) for language in result_languages):
        errors.append("every language result must have a string language")
    elif sorted(result_languages) != languages:
        errors.append(f"results must contain exactly these languages: {', '.join(languages)}")

    for result in results:
        if not isinstance(result, dict):
            errors.append("every language result must be an object")
            continue
        language = result.get("language", "<unknown>")
        active_language = result.get("activeLanguage")
        if not isinstance(active_language, str) or not (
            active_language == language or active_language.startswith(f"{language} (")
        ):
            errors.append(f"{language}: activeLanguage does not match")
        if result.get("success") is not True:
            errors.append(f"{language}: success must be true")

        assertions = result.get("assertions")
        if not isinstance(assertions, dict):
            errors.append(f"{language}: assertions must be an object")
            continue
        expected_assertions = {
            "requestedLanguageActive": True,
            "allTitleValuePairsFit": True,
            "allFixedWidthHeadersFit": True,
            "titleValueCheckCount": EXPECTED_TITLE_VALUE_CHECKS,
            "fixedTextCheckCount": EXPECTED_FIXED_TEXT_CHECKS,
            "overlapCount": 0,
            "fixedTextClipCount": 0,
        }
        for key, expected in expected_assertions.items():
            if assertions.get(key) != expected:
                errors.append(f"{language}: assertions.{key} must be {expected!r}")

    expected_total = len(languages) * (EXPECTED_TITLE_VALUE_CHECKS + EXPECTED_FIXED_TEXT_CHECKS)
    if evidence.get("totalChecks") != expected_total:
        errors.append(f"totalChecks must be {expected_total}")

    if errors:
        fail("layout evidence is not release-ready:\n  - " + "\n  - ".join(errors))


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Skip when no keyed wording changed since the previous release; otherwise require "
            "fresh passing live settings-layout evidence."
        )
    )
    parser.add_argument("--base-ref", help="Previous release ref; auto-detected when omitted.")
    parser.add_argument(
        "--target-ref",
        help="Committed release target. When omitted, compare the base with the current working tree.",
    )
    parser.add_argument(
        "--evidence",
        type=Path,
        default=DEFAULT_EVIDENCE,
        help=f"Evidence JSON path (default: {DEFAULT_EVIDENCE}).",
    )
    parser.add_argument(
        "--print-input-digest",
        action="store_true",
        help="Print the current gate inputs as JSON without verifying evidence.",
    )
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    base_ref = arguments.base_ref or resolve_previous_release_tag(arguments.target_ref)
    commit_for(base_ref)
    if arguments.target_ref is not None:
        commit_for(arguments.target_ref)

    wording_files = changed_wording_files(base_ref, arguments.target_ref)
    if not arguments.print_input_digest and not wording_files:
        print(f"settings-layout-release-gate: skipped; no keyed wording changed since {base_ref}")
        return

    inputs = audit_input_files(arguments.target_ref)
    digest = input_digest(inputs, arguments.target_ref)
    languages = expected_languages(inputs)

    if arguments.print_input_digest:
        print(
            json.dumps(
                {
                    "baseTag": base_ref,
                    "target": arguments.target_ref or "working-tree",
                    "wordingChanged": bool(wording_files),
                    "wordingFiles": wording_files,
                    "inputDigest": digest,
                    "inputFiles": [path.as_posix() for path in inputs],
                    "languages": languages,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return

    evidence = read_evidence(arguments.evidence, arguments.target_ref)
    verify_evidence(evidence, base_ref, digest, languages)
    print(
        f"settings-layout-release-gate: passed; {len(languages)} languages and "
        f"{len(languages) * (EXPECTED_TITLE_VALUE_CHECKS + EXPECTED_FIXED_TEXT_CHECKS)} constrained states "
        f"were measured after wording changes since {base_ref}"
    )


if __name__ == "__main__":
    main()

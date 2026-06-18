"""
Cloud Meadow - Full Text Dumper
Extracts ALL TextAssets (dialogs, quests, etc.) from data.unity3d
Output: pluginsEdit/dump_output/  (one JSON per TextAsset, plus combined_all.json)
"""

import sys as _sys
_sys.path.insert(0, "C:/Users/io357/AppData/Roaming/Python/Python313/site-packages")
import UnityPy as unitypy
import json
import os
import re
import sys
from pathlib import Path

GAME_DIR = Path(r"c:\Users\io357\Downloads\gamer\2\Cloud_Meadow-v0.2.6.0b-GoG\Cloud Meadow-v0.2.6.0b-GoG")
DATA_DIR = GAME_DIR / "Cloud Meadow_Data"
OUT_DIR  = Path(__file__).parent / "dump_output"

# Files to scan (in order of priority)
SCAN_FILES = [
    DATA_DIR / "data.unity3d",
    DATA_DIR / "resources.assets",
]
# Also scan sharedassets*.assets
for f in DATA_DIR.glob("sharedassets*"):
    if f.suffix != ".resource":
        SCAN_FILES.append(f)
# Also scan all files in StreamingAssets
STREAMING = DATA_DIR / "StreamingAssets"
if STREAMING.exists():
    for f in STREAMING.rglob("*"):
        if f.is_file() and f.suffix not in (".meta", ".manifest", ".keep"):
            SCAN_FILES.append(f)


def is_human_text(s: str) -> bool:
    """Return True if string contains at least one letter and is non-trivial."""
    if not s or len(s) < 2:
        return False
    return any(c.isalpha() for c in s)


def extract_strings_from_obj(obj, prefix="", result=None):
    """Recursively extract human-readable strings from a parsed JSON object."""
    if result is None:
        result = {}
    if isinstance(obj, str):
        if is_human_text(obj):
            result[prefix] = obj
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            extract_strings_from_obj(item, f"{prefix}[{i}]", result)
    elif isinstance(obj, dict):
        for k, v in obj.items():
            extract_strings_from_obj(v, f"{prefix}.{k}" if prefix else k, result)
    return result


def dump_text_asset(name: str, text: str, out_dir: Path) -> dict:
    """Parse text as JSON and extract human strings. Returns dict of {key: value}."""
    strings = {}
    try:
        parsed = json.loads(text)
        strings = extract_strings_from_obj(parsed)
    except json.JSONDecodeError:
        # Not JSON — try line-by-line for plain text dialogs
        for i, line in enumerate(text.splitlines()):
            line = line.strip()
            if is_human_text(line) and len(line) > 3:
                strings[f"line_{i}"] = line

    if not strings:
        return {}

    # Save individual file
    safe_name = re.sub(r'[^\w\-]', '_', name)
    out_path = out_dir / f"{safe_name}.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(strings, f, ensure_ascii=False, indent=2)

    return strings


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    print(f"Output dir: {OUT_DIR}")

    combined = {}   # asset_name -> {key: value}
    total_assets = 0
    total_files = 0

    for src_file in SCAN_FILES:
        if not src_file.exists():
            continue
        print(f"\nScanning: {src_file.name} ({src_file.stat().st_size // 1024 // 1024} MB)")
        try:
            env = unitypy.load(str(src_file))
        except Exception as e:
            print(f"  [SKIP] Cannot load: {e}")
            continue

        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            total_assets += 1
            try:
                data = obj.read()
                # UnityPy 1.x uses m_Name / m_Script
                name = getattr(data, "m_Name", None) or getattr(data, "name", None) or ""
                text = getattr(data, "m_Script", None) or getattr(data, "script", None) or b""
                if isinstance(text, (bytes, bytearray)):
                    try:
                        text = text.decode("utf-8")
                    except Exception:
                        continue
                if not text or len(text) < 5:
                    continue

                strings = dump_text_asset(name, text, OUT_DIR)
                if strings:
                    combined[name] = strings
                    total_files += 1
                    print(f"  [{total_files}] {name}: {len(strings)} strings")
            except Exception as e:
                print(f"  [ERR] {e}")

    # Save combined file
    combined_path = OUT_DIR / "COMBINED_ALL.json"
    flat = {}
    for asset_name, strings in combined.items():
        for k, v in strings.items():
            flat[f"{asset_name}__{k}"] = v
    with open(combined_path, "w", encoding="utf-8") as f:
        json.dump(flat, f, ensure_ascii=False, indent=2)

    print(f"\n{'='*60}")
    print(f"Total TextAssets found : {total_assets}")
    print(f"Dialog files dumped    : {total_files}")
    print(f"Total strings          : {len(flat)}")
    print(f"Combined output        : {combined_path}")
    print(f"Individual files       : {OUT_DIR}")


if __name__ == "__main__":
    main()

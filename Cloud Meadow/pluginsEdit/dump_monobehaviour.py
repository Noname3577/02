"""
Cloud Meadow - MonoBehaviour string extractor
Extracts human-readable strings from all MonoBehaviour assets (ScriptableObjects)
including ability names, monster names, quest titles, status effects, etc.
"""
import sys
sys.path.insert(0, "C:/Users/io357/AppData/Roaming/Python/Python313/site-packages")
import UnityPy
import json
import re
from pathlib import Path
from collections import defaultdict

GAME_DATA = Path(r"c:\Users\io357\Downloads\gamer\2\Cloud_Meadow-v0.2.6.0b-GoG\Cloud Meadow-v0.2.6.0b-GoG\Cloud Meadow_Data\data.unity3d")
OUT_DIR = Path(__file__).parent / "dump_output"
OUT_DIR.mkdir(exist_ok=True)

# String fields that are likely UI/display text
UI_FIELD_NAMES = {
    "m_Name", "displayName", "description", "name", "title",
    "abilityName", "statusName", "questName", "questDescription",
    "flavorText", "tooltipText", "shortDescription", "longDescription",
    "effectDescription", "traitName", "traitDescription",
    "itemName", "itemDescription", "skillName", "label", "text",
}

def is_human_text(s):
    if not isinstance(s, str) or len(s) < 2:
        return False
    return any(c.isalpha() for c in s)

def extract_strings_from_bytes(raw: bytes, asset_name: str) -> dict:
    """Extract UTF-8 strings of length >= 3 from raw binary data."""
    result = {}
    idx = 0
    entry_idx = 0
    MIN_LEN = 3
    while idx < len(raw) - 4:
        # Unity stores strings as: int32 length + UTF-8 bytes
        length = int.from_bytes(raw[idx:idx+4], "little")
        if 3 <= length <= 2000:
            end = idx + 4 + length
            if end <= len(raw):
                try:
                    s = raw[idx+4:end].decode("utf-8")
                    if is_human_text(s) and not s.startswith("{") and "\x00" not in s:
                        result[f"{entry_idx}"] = s
                        entry_idx += 1
                    idx = end
                    # align to 4 bytes
                    remainder = (end) % 4
                    if remainder:
                        idx += 4 - remainder
                    continue
                except Exception:
                    pass
        idx += 1
    return result

def extract_strings_from_dict(d, prefix="", result=None, depth=0):
    if result is None:
        result = {}
    if depth > 6:
        return result
    if isinstance(d, dict):
        for k, v in d.items():
            new_prefix = f"{prefix}.{k}" if prefix else k
            if isinstance(v, str):
                if is_human_text(v):
                    result[new_prefix] = v
            elif isinstance(v, (dict, list)):
                extract_strings_from_dict(v, new_prefix, result, depth + 1)
    elif isinstance(d, list):
        for i, item in enumerate(d):
            extract_strings_from_dict(item, f"{prefix}[{i}]", result, depth + 1)
    return result

def main():
    print(f"Loading {GAME_DATA.name}...")
    env = UnityPy.load(str(GAME_DATA))

    # Group by script type name
    by_type = defaultdict(list)
    total_mb = 0

    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        total_mb += 1
        try:
            data = obj.read()
            asset_name = getattr(data, "m_Name", "") or ""

            # Try to_dict() first
            raw = {}
            if hasattr(data, "to_dict"):
                try:
                    raw = data.to_dict() or {}
                except Exception:
                    pass

            # If to_dict empty, read raw bytes and extract UTF-8 strings
            if not raw:
                try:
                    raw_bytes = obj.get_raw_data() if hasattr(obj, "get_raw_data") else b""
                    if not raw_bytes and hasattr(obj, "raw_data"):
                        raw_bytes = obj.raw_data
                    if raw_bytes:
                        strings_found = extract_strings_from_bytes(raw_bytes, asset_name)
                        if strings_found:
                            by_type["__raw__"].append({
                                "asset_name": asset_name,
                                "strings": strings_found
                            })
                        continue
                except Exception:
                    pass
                continue

            strings = extract_strings_from_dict(raw)
            filtered = {k: v for k, v in strings.items() if is_human_text(v)}

            if filtered:
                # Get class name
                class_name = ""
                script_ref = getattr(data, "m_Script", None)
                if script_ref:
                    try:
                        sc = script_ref.read()
                        class_name = getattr(sc, "m_ClassName", "") or ""
                    except Exception:
                        pass
                by_type[class_name].append({
                    "asset_name": asset_name,
                    "strings": filtered
                })
        except Exception:
            pass

    print(f"Total MonoBehaviours: {total_mb}")
    print(f"Types with text: {len(by_type)}")
    print()

    # Show summary by type
    type_summary = sorted(by_type.items(), key=lambda x: -sum(len(e["strings"]) for e in x[1]))
    for class_name, entries in type_summary[:30]:
        total_strings = sum(len(e["strings"]) for e in entries)
        print(f"  {class_name or '(unknown)'}: {len(entries)} objects, {total_strings} strings")

    # Save by category
    all_ui = {}
    for class_name, entries in by_type.items():
        for entry in entries:
            asset_name = entry["asset_name"] or class_name or "unknown"
            for k, v in entry["strings"].items():
                flat_key = f"{asset_name}__{k}"
                all_ui[flat_key] = v

    out_path = OUT_DIR / "UI_MonoBehaviour_ALL.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(all_ui, f, ensure_ascii=False, indent=2)

    print(f"\nTotal UI strings: {len(all_ui)}")
    print(f"Saved: {out_path}")

if __name__ == "__main__":
    main()

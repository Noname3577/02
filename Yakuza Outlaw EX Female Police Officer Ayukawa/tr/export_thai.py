"""
export_thai.py
==============
Export เฉพาะ entry ที่แปลเป็นไทยแล้วออกมาเป็น thai_ui.json / thai_dialog.json
สำหรับใช้กับ ThaiPatcher plugin

วิธีใช้:
  python export_thai.py                      # export ทั้ง UI และ Dialog
  python export_thai.py --type ui            # เฉพาะ UI
  python export_thai.py --type dialog        # เฉพาะ Dialog
"""

import json
import re
import argparse
from pathlib import Path

GAME_DIR = Path(r"c:\Users\io357\Downloads\gamer\1\Yakuzarogue_v1.1.2\Yakuzarogue_v1.1.2")
TR_DIR = Path(__file__).parent
OUT_DIR = GAME_DIR / "BepInEx" / "ThaiPatcher"

DUMP_FILES = {
    "ui":     TR_DIR / "L10nUI_English_20260617_122311.json",
    "dialog": TR_DIR / "L10nDialog_English_20260617_135240.json",
}

OUTPUT_FILES = {
    "ui":     OUT_DIR / "thai_ui.json",
    "dialog": OUT_DIR / "thai_dialog.json",
}

THAI_PATTERN = re.compile(r'[฀-๿]')


def is_thai(text: str) -> bool:
    return bool(THAI_PATTERN.search(text))


def export(type_key: str):
    src = DUMP_FILES[type_key]
    dst = OUTPUT_FILES[type_key]

    if not src.exists():
        print(f"ไม่พบไฟล์ {src}")
        return

    with open(src, encoding="utf-8-sig") as f:
        data = json.load(f)

    # เอาเฉพาะ entry ที่มีข้อความไทย
    thai_only = {k: v for k, v in data.items() if v and is_thai(v)}

    if not thai_only:
        print(f"[{type_key}] ยังไม่มี entry ที่แปลไทย")
        return

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    with open(dst, "w", encoding="utf-8") as f:
        json.dump(thai_only, f, ensure_ascii=False, indent=2)

    print(f"[{type_key}] export {len(thai_only)} entries -> {dst}")
    for k, v in thai_only.items():
        print(f"  {k}: {v}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--type", choices=["ui", "dialog", "all"], default="all")
    args = parser.parse_args()

    types = ["ui", "dialog"] if args.type == "all" else [args.type]
    for t in types:
        export(t)

    print("\nเสร็จแล้ว! เปิดเกมใหม่ ThaiPatcher จะ inject คำแปลอัตโนมัติ")


if __name__ == "__main__":
    main()

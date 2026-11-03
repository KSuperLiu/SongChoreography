"""Publish test metrics without redistributing per-frame animation samples."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "Docs"
DEST = DOCS / "ReleaseValidation"
POSE_KEYS = {"leftHand", "rightHand", "leftFoot", "rightFoot", "hips", "rawHand", "cachedHand", "guardedHand"}


def summarize(value):
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            if isinstance(item, list) and any(isinstance(row, dict) and POSE_KEYS.intersection(row) for row in item):
                result[key + "OmittedCount"] = len(item)
            else:
                result[key] = summarize(item)
        return result
    if isinstance(value, list):
        return [summarize(item) for item in value]
    return value


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    names = []
    for path in sorted(DOCS.glob("*.json")):
        if not (path.name.endswith("VERIFICATION.json") or path.name.endswith("PLAYMODE.json")):
            continue
        data = summarize(json.loads(path.read_text(encoding="utf-8-sig")))
        data["publicationNote"] = "Metrics summary. Full per-frame motion traces remain local and are not part of the public source distribution."
        (DEST / path.name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        names.append(path.name)
    for path in DOCS.glob("*.md"):
        text = path.read_text(encoding="utf-8-sig")
        for name in names:
            text = text.replace("](" + name + ")", "](ReleaseValidation/" + name + ")")
        path.write_text(text, encoding="utf-8")
    print(f"Prepared {len(names)} public metric summaries; original local traces unchanged.")


if __name__ == "__main__":
    main()

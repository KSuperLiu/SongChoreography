"""Register verified local Mixamo downloads as unreviewed Unity candidates.

This command uses local files only. It neither downloads nor approves motions.
Run GeneratedAssetBuilder.Import in Unity afterwards.
"""
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def category(name, description="", pack=""):
    text = f"{name} {description}".lower()
    if "pose" in text:
        return "pose"
    if "skinning test" in text:
        return "test"
    if pack == "gestures_basic" or "gesture" in text:
        return "gesture"
    for word, tag in [("hip hop", "hip-hop"), ("hip-hop", "hip-hop"),
                      ("breakdance", "breakdance"), ("bboy", "breakdance"),
                      ("b-boy", "breakdance"), ("uprock", "breakdance"),
                      ("jazz", "jazz"), ("salsa", "salsa"), ("samba", "samba"),
                      ("rumba", "rumba"), ("swing", "swing"), ("house", "house"),
                      ("ballet", "ballet"), ("belly", "belly-dance"),
                      ("northern soul", "northern-soul")]:
        if word in text:
            return tag
    if pack == "breakdance":
        return "breakdance"
    if any(word in text for word in ["danc", "moonwalk", "flair", "shuffling", "thriller", "gangnam", "boogaloo", "can can", "ymca", "hokey pokey"]):
        return "dance-other"
    if any(word in text for word in ["walk", "run ", "run-", "run_", "catwalk", "locomotion", "strafe"]):
        return "locomotion"
    return "other"


def prepare(root=ROOT):
    manifest = json.loads((root / ".generation/mixamo-batch.json").read_text(encoding="utf-8-sig"))
    if manifest.get("status") != "complete" or manifest.get("remaining") != 0:
        raise ValueError("Download batch is not complete.")
    entries = manifest["downloaded"]
    if len({entry["id"] for entry in entries}) != len(entries):
        raise ValueError("Duplicate search result IDs.")
    catalog_path = root / "Generation/library.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
    if catalog.get("schemaVersion") != 2:
        raise ValueError("Expected catalog schemaVersion 2.")
    by_file = {item["sourceFile"]: item for item in catalog["motions"]}
    by_id = {item["id"]: item for item in catalog["characters"] + catalog["motions"]}
    counts = Counter()
    added = 0
    # Validate all candidates before writing the catalog.
    for entry in entries:
        pack = entry["id"] if entry.get("kind") == "animation-pack" else ""
        files = entry.get("members", []) if pack else [entry]
        if not files:
            raise ValueError(f"Pack has no extracted members: {entry['id']}")
        for member in files:
            relative = member["file"]
            path = (root / relative).resolve()
            if not path.is_relative_to((root / "Assets/Generated/Motions").resolve()) or path.suffix.lower() != ".fbx":
                raise ValueError(f"Invalid motion source path: {relative}")
            contents = path.read_bytes()
            digest = hashlib.sha256(contents).hexdigest()
            if digest != member["sha256"] or not contents.startswith(b"Kaydara FBX Binary  \x00\x1a\x00"):
                raise ValueError(f"File hash/header mismatch: {relative}")
            name = path.stem if pack else entry["name"]
            description = name if pack else entry.get("description", name)
            tag = category(name, description, pack)
            counts[tag] += 1
            if relative in by_file:
                if by_file[relative]["sha256"] != digest:
                    raise ValueError(f"Existing catalog source changed: {relative}")
                continue
            slug = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")[:25]
            pack_slug = re.sub(r"[^a-z0-9]+", "-", pack.lower()).strip("-")
            motion_id = path.stem if not pack else f"mixamo-pack-{pack_slug}-{slug}-{digest[:8]}"
            motion_id = motion_id[:64].rstrip("-")
            if not re.fullmatch(r"[a-z][a-z0-9-]{1,63}", motion_id) or motion_id in by_id:
                raise ValueError(f"Invalid or conflicting ID: {motion_id}")
            definition = dict(id=motion_id, name=description, provider="mixamo", sourceFile=relative,
                              sha256=digest, requestId=entry["id"],
                              sourceUrl="https://www.mixamo.com/#/?page=1&query=hip+hop",
                              termsUrl="https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html",
                              model="", prompt="", clipName="", tags=[tag, "draft"],
                              beats=8, sourceBpm=120, energy=.5, beatTimes=[], reviewedCharacters=[])
            catalog["motions"].append(definition)
            by_file[relative] = definition
            by_id[motion_id] = definition
            added += 1
    # The backup is only created once and never replaces an earlier recovery copy.
    backup = root / ".generation/library-before-mixamo-registration.json"
    if not backup.exists():
        backup.write_bytes(catalog_path.read_bytes())
    temporary = catalog_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(catalog_path)
    summary = dict(searchResults=len(entries), added=added, totalCatalogMotions=len(catalog["motions"]),
                   sourceCategories=dict(sorted(counts.items())), approvedAutomatically=0,
                   note="Tags derive from source titles/descriptions; New Jazz quality and beat timing require review.")
    (root / ".generation/mixamo-registration.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return summary


if __name__ == "__main__":
    print(json.dumps(prepare(), ensure_ascii=False, indent=2))

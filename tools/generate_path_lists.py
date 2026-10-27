#!/usr/bin/env python3
import sys
from pathlib import Path

import msgpack


def timed_quest_ids(quest_dir: Path) -> set[str]:
    ids = set()
    for baseinfo in quest_dir.glob("*/baseinfo.msg"):
        record = msgpack.unpackb(baseinfo.read_bytes(), raw=False, strict_map_key=False)
        try:
            if int(record.get("time_", "0")) > 0:
                ids.add(baseinfo.parent.name)
        except (TypeError, ValueError):
            pass
    return ids


def write_list(path: Path, entries: list[str]) -> None:
    path.write_text("".join(f"{entry}\n" for entry in entries), encoding="utf-8")
    print(f"{path.name}: {len(entries)} entries")


def main() -> None:
    if len(sys.argv) != 3:
        sys.exit("usage: generate_path_lists.py <extracted game data dir> <output mod dir>")

    data = Path(sys.argv[1])
    out = Path(sys.argv[2])
    quest_dir = data / "quest"
    fsm_dir = data / "system" / "fsm" / "quest"

    quests = sorted(p.parent.name for p in quest_dir.glob("*/baseinfo.msg"))
    write_list(out / "quest-baseinfo-paths.txt", [f"quest/{q}/baseinfo.msg" for q in quests])

    timed = timed_quest_ids(quest_dir)
    fsm_files = sorted(p.name for p in fsm_dir.glob("quest_*_fsm_ingame.msg"))
    write_list(out / "quest-fsm-paths.txt",
               [f"system/fsm/quest/{name}" for name in fsm_files if name.split("_")[1] in timed])
    write_list(out / "story-fsm-paths.txt",
               [f"system/fsm/quest/{name}" for name in fsm_files if name.startswith("quest_1")])


if __name__ == "__main__":
    main()

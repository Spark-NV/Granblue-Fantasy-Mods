# Granblue Fantasy Mods

A Reloaded-II mod for **Granblue Fantasy: Relink** (v2.0.6, Steam).

## Features

Every feature can be turned off or adjusted; see [Configuration](#configuration). The
values below are the defaults.

- **Quest timers +60 seconds.** Every timed quest gets an extra minute. Survive/defend quests,
  where running out the clock is the win, are left unchanged.
- **No thrown-object objectives.** Every "Defeat N foe(s) with thrown objects" side goal
  becomes "Defeat 10 foes". Its reward is unchanged.
- **Rewards:** MSP +50%, EXP and rupies +20% (rounded up).
- **Item cap 9999** for every item category normally capped at 999.
- **All fate episodes unlocked**: no story, level or character-mission requirements.
- **Easier quest grades:** S++ and the other grade thresholds are 20% easier.
- **Any party in the main story:** the captain no longer has to be in your party.
  Cutscenes still show the captain, and a few scripted solo fights and set-crew
  sections are unchanged.
- **Crewmate cards for 1 Cobblestone:** Treasure Trade at Siero's Knickknack Shack sells
  the Crewmate Card.

## Requirements

- [Reloaded-II](https://github.com/Reloaded-Project/Reloaded-II/releases)
- [Granblue Fantasy Relink Mod Manager](https://github.com/WistfulHopes/gbfrelink.utility.manager/releases)
  2.0.1 or newer (`gbfrelink.utility.manager`)
- Reloaded File Redirector (`reloaded.universal.redirector`), installed automatically as
  a dependency

## Install

1. Download `gbfr.spark.granblue-fantasy-mods-<version>.zip` from Releases.
2. Drag it onto the Reloaded-II window, or extract it into `Reloaded-II/Mods/`.
3. Enable **Granblue Fantasy Mods** and launch the game through Reloaded-II.

The first launch takes a couple of seconds longer while the mod builds its cache.

## Configuration

In Reloaded-II, select **Granblue Fantasy Mods**, then right-click it and choose
**Configure Mod**. Settings are read at startup, so restart the game after changing them.

| Setting | Default | Range | Effect |
|---|---|---|---|
| Extend quest timers | On | | Adds time to every timed quest, its scripted time-up checks and its "clear within X:XX" side goals |
| Extra seconds | 60 | 1–600 | Seconds added to each quest timer |
| Also extend survive/defend quests | Off | | Extends quests that are won when the clock runs out. Their enemy waves follow the original length, so this only adds waiting time |
| Replace thrown-object objectives | On | | Turns "Defeat N foe(s) with thrown objects" side goals into "Defeat N foes" |
| Foes to defeat | 10 | 1–100 | Kill count for the replacement side goal. Some quests spawn only about 12 foes |
| MSP bonus (%) | 50 | 0–900 | Extra mastery points, rounded up. 0 is vanilla |
| EXP and rupies bonus (%) | 20 | 0–900 | Extra EXP and rupies, rounded up. 0 is vanilla |
| Raise item cap | On | | Raises the held-item cap for categories normally capped at 999 |
| Item cap | 9999 | 999–9999 | The new held-item cap |
| Unlock all fate episodes | On | | Removes story, level and character-mission requirements |
| Easier grades (%) | 20 | 0–90 | How much easier every grade threshold gets. 0 is vanilla |
| Captain optional in the main story | On | | The captain no longer has to be in your story party |
| Cheap crewmate cards | On | | Treasure Trade sells crewmate cards for Cobblestones, unlimited |
| Cobblestones per card | 1 | 1–99 | Cost of each card |

## How it works

Everything is applied in memory and in the mod's own folder at startup. The game
installation and your save files are never modified. Disable the mod to return to
vanilla.
Quest scripts are overridden through the GBFR Mod Manager. The "captain must be in the party" check is
patched in code; the patch is only written if the exact expected code is found.

After a game update, anything that no longer matches is skipped with a warning in the
Reloaded-II console, and that part of the game stays vanilla.

## Notes

- **Item cap:** if you hold more than 999 of an item and then disable the mod, the game
  reduces it to 999 and the excess is lost.
- **Online play:** these changes alter quest timers, rewards and grading. Only attempt to use them in
  solo play only.
- **Conflicts:** other mods that replace the same tables (`trade.tbl`, `reward_point.tbl`,
  `item_category.tbl`, `fate_episode.tbl`, `formation_slot.tbl`,
  `quest_baseinfo_ex_data.tbl`, `item_material_list.tbl`) or quest scripts may conflict.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) or newer.

Windows (PowerShell):

```powershell
./build.ps1
```

Linux:

```bash
./build.sh
```

Both create `package/gbfr.spark.granblue-fantasy-mods/` and
`dist/gbfr.spark.granblue-fantasy-mods-<version>.zip`.

## Credits

- [GBFRDataTools](https://github.com/Nenkai/GBFRDataTools) by Nenkai: archive format, table
  headers, file list and the table-key hash.
- [Granblue Fantasy Relink Mod Manager](https://github.com/WistfulHopes/gbfrelink.utility.manager)
  by WistfulHopes and Nenkai.
- [GBFRelinkMod](https://github.com/alexfrljuckic/GBFRelinkMod) research by alexfrljuckic:
  2.0 table layouts, the shop cost chain and the 999 item-cap fields.

## Repository layout

| Path | Contents |
|---|---|
| `src/` | C# source of the Reloaded-II mod |
| `mod/` | `ModConfig.json` and the quest file lists the mod reads at startup |
| `tools/generate_path_lists.py` | Regenerates the lists in `mod/` from extracted game data after a game update (requires Python `msgpack`) |
| `tools/gbhash.py` | The game's 32-bit table-key hash, for looking up table keys |

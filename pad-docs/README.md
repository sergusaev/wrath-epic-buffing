# Buff It 2 The Limit (Pad): gamepad menu

[Русская версия](README.ru.md)

This fork of [Buff It 2 The Limit](https://github.com/Gh05d/wrath-epic-buffing) 1.21.2 (MIT; authors Vek17, factubsio, Gh05d) adds a menu that is driven entirely by the gamepad, for Pathfinder: Wrath of the Righteous and Pathfinder: Kingmaker in the console interface. The original mod is configured only from the PC spellbook screen, which does not exist in the console interface.

## Installing

1. Install Unity Mod Manager for the game.
2. Build the mod (see [Building](#building)) or take a release.
3. WotR: copy `BuffIt2TheLimit.dll` and `Info.json` into `<game>/Mods/BuffIt2TheLimit/` (the Steam Deck guide uses `mods`; Proton ignores case). UMM lists the mod as "Buff It 2 The Limit (Pad)". It replaces the original mod: both use the same id and the same settings files. It also replaces BubbleBuffs, but does not read the BubbleBuffs settings (`bubblebuff-*.json`).
4. Kingmaker: copy `PadBuffsKingmaker.dll` and `Info.json` into `<game>/Mods/PadBuffsKingmaker/`.

## Opening the menu

The menu opens with the menu key from the mod settings:

- **Kingmaker:** F7 by default (written into a new settings file).
- **WotR:** the key the original mod calls "open buff menu". Set it in the PC spellbook screen in mouse and keyboard mode, or write it into the settings file (`OpenBuffMenuKey`, see [Files](#files)); the [Steam Deck guide](https://github.com/sergusaev/pathfinder-mods/blob/main/docs/steam-deck.md#3-installing-the-mods) has a ready command that sets F7.

**Steam Deck from scratch** — Unity Mod Manager for both games, `startup.json`, the whole Steam Input layout (UMM window on R4, this menu on L5, mouse on the right trackpad): [pathfinder-mods/docs/steam-deck.md](https://github.com/sergusaev/pathfinder-mods/blob/main/docs/steam-deck.md).

A gamepad has no F keys, so bind the key to a free button in Steam Input. On the Steam Deck: Controller settings → Edit layout → Back buttons → L5 → keyboard key F7, a single regular press without delays. The mod recognises the gestures itself (`PadGestures.cs`):

| Menu button | Action |
|---|---|
| short press | open or close the menu (fires after 0.35 s: the mod waits for a possible second press) |
| hold for 0.6 s | apply the built-in Long group ("10 min/level and longer") |
| double press | apply the built-in Important group ("Rounds") |

The result of every cast, from a gesture or from the menu, pops up at the top of the screen for 5 s with the reasons of failures, and goes to the game's combat log. Activatables count like casts: switched on = applied, already on = already active.

Why the gestures are not done in Steam Input: with a Long Press binding Steam also sends the regular press key, and a Double Press never reached the game. Avoid key chords with Shift: on a short tap Steam Input does not deliver a key with a start delay, only the Shift arrives.

## Controls

**Main page**

| Button | Action |
|---|---|
| ↑↓ (D-pad or left stick, holding repeats) | select a row |
| A on a group | apply the group |
| Y on a group | group members |
| A on "Cast in combat" | toggle casting in combat |
| A on "Groups" | the list of groups |
| A on "Buff targets" | the target editor |
| A on "Help" | detailed help with an abstract example; up/down scroll, LB/RB jump between sections |
| X | apply all shown groups one after another |
| B | close |

A group row shows how many buffs are enabled, how many of the wanted buffs are active (yellow: some expired, green: all active), when the nearest one expires and how many are switched off by their checkbox. After a cast it shows how many buffs were applied and how many were already active, and the failures with reasons.

**Groups**

| Button | Action |
|---|---|
| A on a group | group members |
| A on "+ New group" | create one (the name page opens) |
| Y | rename and change the duration |
| X | show or hide in the main menu |
| RB twice within 3 s | delete a custom group; built-in groups cannot be deleted and show no RB hint |
| B | back |

**Group members**

| Button | Action |
|---|---|
| ↑↓ | buff: the group's buffs with checkboxes on top, the others grey below, the ones matching the duration first |
| A | add to the group or remove from it |
| X | checkbox: switch the buff off or on in this group only, its targets stay |
| Y | fill by duration: every party buff with the group's duration, added switched off |
| → | targets of the selected buff (the target editor) |
| B | back |

Columns: name, duration (rounds / minutes / 10 min / hours / toggle), who (self / party / one performer / no targets), targets "set/possible".

**Group name** — an on-screen keyboard: D-pad selects a key, A types, X erases, Y finishes, LB/RB change the duration, B cancels. Special keys: "Aa" (capital), "EN/RU" (layout), "Space". The Steam keyboard (Steam + X) works too: the mod reads `Input.inputString`. An empty name means the group is named by its duration.

**Buff targets**

| Button | Action |
|---|---|
| ↑↓ | buff (holding repeats: 0.35 s before the first repeat, then every 0.08 s) |
| ←→ | character in the party strip |
| A | toggle the target for the selected character |
| Y | the whole party (if everyone is already selected, clear all) |
| X | automatic targets (self / party) |
| LB / RB | tabs: Assigned, All, then every group |
| B | back (to the group members, if you came from there) |

The party strip: green — the target is set; gold — the cursor; a struck-through reddish name — the buff cannot be cast on this character.

Every change is saved to the settings file at once.

## Groups

- **Built-in groups** (`BuffGroup.Long/Quick/Important`) are named by duration: "10 min/level and longer", "Minutes (min/level)", "Rounds". Holding the menu button casts Long, a double press casts Important. They can be renamed, given another duration and hidden.
- **Custom groups** are the slots `Custom1…Custom12` of the same `BuffGroup` enum (`BubbleBuffer.cs`), so casting, reports and saving need no separate code path. Groups are described in `SavedBufferState.Groups` (`SavedGroup`: `Id`, `Name` — `null` means "named by duration", `Duration`, `Hidden`).
- **Membership** works as in the original: `InGroups` of a buff; a buff is in a group if it is listed there and has targets. Every buff starts with `InGroups = {Long}`, so a target set on a buff outside any group puts it into Long.
- **The checkbox** is `DisabledIn` of the buff and of `SavedBuffState`. Only `BubbleBuff.ActiveIn(group)` is cast: in the group, not switched off, has targets (`BuffExecutor.Execute`, slot priority in `Recalculate`, the group tooltip).
- **Targets are shared** by a buff across all groups. Adding a buff without targets sets them automatically (`PadGroups.AutoTarget`): everyone in the party it `CanTarget`; for a self-only buff these are exactly its casters; songs get one performer. Removing a buff from its last group or clearing its last target clears the targets and takes the buff out of all groups.
- **Buff duration** is `AbilityCombinedEffects.Duration`: the maximum `DurationRate` over the effects (`IBeneficialEffect.cs`, `ExtentionMethods.Duration`); `Permanent` and a day count as hours, activatables as toggles. Without an exact duration the old `IsLong` is used.
- **Fill by duration** (`PadGroups.AutoFill`) takes the visible buffs with casters that match the duration (`PadGroups.Fits`; "10 min/level and longer" = 10 min plus hours) and adds them switched off.
- Deleting a custom group also clears it from saved buffs of characters who are not in the party right now.

## Files

- **Settings**, one file per playthrough: `<mod folder>/UserSettings/bi2tl-<GameId>.json`, where GameId comes from `header.json` of the save, without dashes.
  - `Wanted` — UniqueIds of the targets; `InGroups` — groups.
  - `Version` must be `1`, otherwise a migration wipes the targets.
  - Keys: `OpenBuffMenuKey`, `ShortcutKeys` (`Long`, `Quick`, `Important`), each `{"Key": "F7", "Ctrl": false, "Shift": false, "Alt": false}`.
  - Edit the file only while the game is closed: the mod saves over it.
- **Log** (lines tagged `[PAD]`):
  - WotR on Proton: `steamapps/compatdata/1184370/pfx/drive_c/users/steamuser/AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Player.log`; on Windows `%USERPROFILE%\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\Player.log`.
  - Kingmaker on Linux: `~/.config/unity3d/Owlcat Games/Pathfinder Kingmaker/Player.log` (also `[PadBuffsKingmaker]`).
- **WotR in gamepad mode on the Steam Deck:** without `startup.json` containing `{"ForceControllerMode":"gamepad"}` in the game folder, WotR offers to switch to the keyboard on every key press, including the menu key sent by Steam Input.

## Kingmaker

The same menu (groups, checkboxes, automatic targets, help, gestures) is built for Kingmaker by the separate project `BuffIt2TheLimit.Kingmaker/`. It compiles the shared files of `BuffIt2TheLimit/` with the `KINGMAKER` symbol; the WotR PC spellbook UI (`BubbleBuffer.cs`, `UIHelpers.cs`, `Main.cs`) is not part of that build.

- **Mouse mode and gamepad mode.** In gamepad mode the menu takes the buttons through the game's console input layer; in mouse mode there is no such layer, so the gamepad is polled directly and the keyboard works too (arrows, Enter, Backspace).
- **Steam Input layout.** Kingmaker keeps its layout in Steam Cloud; the steps from [Opening the menu](#opening-the-menu) work the same.

**What differs from WotR**

| Part | Kingmaker |
|---|---|
| Buff sources | spells from spellbooks, class abilities, activatables, songs. Scrolls, potions, wands and items are not scanned |
| Casting | `KmExecutionEngine`: target check → `RuleCastSpell` → the slot is spent right after the rule (Kingmaker has no "before trigger" hook). The slot is spent even if the spell fails, as in the game |
| Not in the game | Shifter's Fury, mounts, Arcanist (reservoir, Share Transmutation), Magic Deceiver, Azata Zippy Magic, mythic levels, Extend rods |
| Pets | one pet per unit (`Descriptor.Pet`), no reserve party |
| Combat log | the result is written by the mod itself (`KmCombatLog`): the console log in gamepad mode, the PC log in mouse mode |

**Where the code differs**

- `KmHost.cs` — UMM entry point, `GlobalBubbleBuffer`, settings storage, event subscriptions (area load, party change, combat start), a stub for cast results.
- `KmExecutionEngine.cs` — casting.
- `KmCompat.cs` — `PetType`, `GetPet`, the `SimpleBlueprint` → `BlueprintScriptableObject` alias.
- Blueprint GUIDs are structs in WotR and strings in Kingmaker; shared code calls `blueprint.Gid()` (WotR — `CoreTypes.cs`, Kingmaker — `KmHost.cs`).
- Shared types (`BuffGroup`, `Bubble`, `AbilityCache`, `RoundLimitHandler` …) moved from `BubbleBuffer.cs` to `CoreTypes.cs`.
- Everything else is `#if KINGMAKER` / `#if !KINGMAKER` blocks in the shared files.

## What the fork changes

- `PadQuickMenu.cs` — the window: main page, groups, members, name (on-screen keyboard), buff targets, help.
  - Its own overlay canvas: the game's PC canvas is hidden in gamepad mode.
  - Buttons go through the game's console input layer (`GamePad.Instance.PushLayer`), so the game ignores them while the window is open.
  - Directions are polled from `GamePad.Instance.Player` (Rewired) with auto-repeat.
  - Hints at the bottom of every page and the tab arrows use the game's own button icons: `GamePadIcons.Instance.GetIcon(RewiredActionType)`, built in `MakeHintBar`/`MakeIcon`. Without an icon the text `[A]` is shown. The game font has no ↑↓←→ arrows.
- `PadGroups.cs` — the group model: custom groups, names by duration, checkboxes, automatic targets (self/party/song), fill by duration.
- `PadHelp.cs` — help markup and button icons in text. The help text is the key `pad.help.body` (plus `pad.help.km` in Kingmaker); the markup is `#` section, `##` subsection, `- ` item, `1. ` step, `> ` tip, `{A}| text` button row, `**bold**`. The tokens `{A} {B} {X} {Y} {LB} {RB} {UP} {DOWN} {LEFT} {RIGHT}` become sprites of the game's TMP asset (`<sprite name="<prefix><action>">`, the prefix comes from `ConsoleBindingTemplate`: `Steam_`, `XBox_`, `PS4_`), or letters in brackets without the asset; `{L5}` is the menu button icon, `{G1} {G2} {G3}` are the names of the built-in groups. The asset found is logged as `[PAD] icons:`. The example in the help is abstract (Fighter, Cleric, Wizard, Bard, Rogue) and does not depend on the player's party.
- `PadGestures.cs` — menu button gestures and the `PadToast` pop-up.
- `CoreTypes.cs` — shared types moved out of `BubbleBuffer.cs` for the Kingmaker build.
- `BuffExecutor.cs`: the `RoutineFinished` event with the cast result; the `ScheduledRoutines` and `FinishedRoutines` counters (without the latter, when there was nothing to cast, "casting…" covered the result for 30 s); in gamepad mode the menu key opens the new window; F6/F7/F9 presses are logged.
- `InstantExecutionEngine.cs` — pauses between batches of casts use real time (`WaitForSecondsRealtime`); otherwise a cast "hangs" while the game is paused.
- `BubbleBuffer.cs`: `Awake` of the spellbook controller no longer throws in gamepad mode, where there is no spellbook (the original threw `NullReferenceException` in `TryFixEILayout`).
- `IBeneficialEffect.cs`: `OwnBuffGuids` for the remaining time; `Duration` for the buff duration class.
- `SaveState.cs`: `Groups` (custom groups) and `DisabledIn` (checkboxes) in the settings file.
- `Info.json`: the name "Buff It 2 The Limit (Pad)", the fork's home page, no update repository (the original's update feed would replace the fork).

## Building

### Third-party tools

| Tool | What for | macOS | Linux | Windows |
|---|---|---|---|---|
| .NET SDK 8 or newer | building both projects | `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0` (to `~/.dotnet`) or `brew install --cask dotnet-sdk` | the same script, or the distro package `dotnet-sdk-8.0` | `winget install Microsoft.DotNet.SDK.8` |
| Bash, `ssh`, `md5sum`/`md5` | `deploy.sh`, `release.sh` | built in | built in | Git for Windows: `winget install Git.Git`, run the scripts from **Git Bash** |
| `zip` or bsdtar | packing release zips | built in (`tar`) | `sudo apt install zip` | `C:\Windows\System32\tar.exe`, used automatically |
| GitHub CLI `gh` | `release.sh --publish` only | `brew install gh` | [cli.github.com](https://cli.github.com/) | `winget install GitHub.cli` |

`deploy.sh` and `release.sh` take `dotnet` from `PATH`, then from `~/.dotnet`; `DOTNET=/path/to/dotnet` overrides it. After installing `gh`, sign in once with `gh auth login`.

**NuGet packages**, downloaded by `dotnet build` on the first build (needs internet): `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 (the .NET Framework 4.8 / 4.8.1 reference assemblies, so no Windows targeting pack is needed) and `BepInEx.AssemblyPublicizer.MSBuild` 0.4.2 (makes private game members accessible at compile time).

**The game's assemblies** (excluded from git, never publish them):

| Project | Folder | What to copy there |
|---|---|---|
| WotR | `GameInstall/Wrath_Data/Managed/` | all DLLs of `<WotR>/Wrath_Data/Managed`, plus `UnityModManager/` with `UnityModManager.dll` and `0Harmony.dll` |
| Kingmaker | `GameInstallKM/Kingmaker_Data/Managed/` | all DLLs of `<Kingmaker>/Kingmaker_Data/Managed`, plus `UnityModManager/` with `UnityModManager.dll` and `0Harmony.dll` |

For example, from a Steam Deck:

```bash
mkdir -p GameInstall/Wrath_Data GameInstallKM/Kingmaker_Data
scp -r "deck@steamdeck.local:/home/deck/.local/share/Steam/steamapps/common/Pathfinder Second Adventure/Wrath_Data/Managed" GameInstall/Wrath_Data/
scp -r "deck@steamdeck.local:/home/deck/.local/share/Steam/steamapps/common/Pathfinder Kingmaker/Kingmaker_Data/Managed" GameInstallKM/Kingmaker_Data/
```

`GamePath.props` in the repository root tells the WotR project where the game is. On Windows with WotR installed, the first build writes it by itself from WotR's `Player.log`; elsewhere create it (with an absolute path):

```xml
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
	<PropertyGroup>
		<WrathInstallDir>/absolute/path/to/wotr-pad-buffs/GameInstall</WrathInstallDir>
	</PropertyGroup>
</Project>
```

`GamePath.props` is not tracked by git: one copied from another machine (with the working copy or the `GameInstall*` folders) still points to that machine's path, and the build then fails on missing game assemblies. Fix `WrathInstallDir` after copying.

On Windows run `git config core.filemode false` once in the clone: Windows has no executable bit, so otherwise git shows every `.sh` file as modified. Copying `.git` from another machine brings back the old value.

The Kingmaker project uses `GameInstallKM/` unless `KingmakerInstallDir` is set in `GamePath.props`. After a successful WotR build the project also copies the mod into `$(WrathInstallDir)/Mods/` — harmless with `GameInstall/`.

### Commands

Build Release: the Debug build enables the debug keys Shift+I/B/R.

```bash
dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/
dotnet build BuffIt2TheLimit.Kingmaker/BuffIt2TheLimit.Kingmaker.csproj -c Release -p:SolutionDir=$(pwd)/
```

The Kingmaker project targets `net48`: Harmony 2.3.6 shipped with UMM for Kingmaker is built for 4.8.

`deploy.sh` builds Release and installs it on a Steam Deck over SSH (the game must be closed), then compares checksums:

```bash
DECK=deck@steamdeck.local ./deploy.sh              # WotR
DECK=deck@steamdeck.local ./deploy.sh --kingmaker  # Kingmaker
DECK=deck@steamdeck.local ./deploy.sh --bind-menu  # WotR, plus menu = F7, Long = F6, Important = F9 in every settings file
```

`WOTR_DIR` and `KINGMAKER_DIR` override the game folders on the deck. New strings go into all five `Config/*.json`; a key missing from `en_GB.json` crashes the game.

### Releasing

```bash
./release.sh wotr                  # build Release and pack dist/BuffIt2TheLimit-Pad-<version>-pad.<N>.zip
./release.sh kingmaker --publish   # pack dist/PadBuffsKingmaker-<version>.zip, tag kingmaker-v<version>, publish
```

WotR releases are numbered `v<upstream version>-pad.<N>`, where N is the next unreleased number; the Kingmaker version comes from `BuffIt2TheLimit.Kingmaker/Info.json` and must be bumped before publishing. `--publish` needs the [GitHub CLI](https://cli.github.com/), a clean tree and `gamepad` pushed; `--notes FILE` replaces the default release notes. `dist/` is not tracked by git.

## Pitfalls found along the way

- **L3+R3 is taken:** in WotR it opens the bug report window.
- **Steam Deck back button names** in a layout file: L4 = `button_back_left_upper`, L5 = `button_back_left`, R4 = `button_back_right_upper`, R5 = `button_back_right`. There is no `…_lower`; Steam silently skips such a block.
- **The buff list** is not built in gamepad mode until the spellbook is opened, so the menu calls `state.Recalculate(false)` when it opens.
- **Remaining time** comes from the public `Buff.TimeLeft` and `Buff.IsPermanent`.

## Maintaining the fork

- `master` mirrors [Gh05d/wrath-epic-buffing](https://github.com/Gh05d/wrath-epic-buffing) and gets no own commits.
- `gamepad` (the default branch) holds the fork's changes on top of it.

Bringing in new upstream versions, merging rather than rebasing so the published history stays intact:

```bash
git remote add upstream https://github.com/Gh05d/wrath-epic-buffing   # once
git fetch upstream
git switch master && git merge --ff-only upstream/master && git push origin master
git switch gamepad && git merge master && git push origin gamepad
```

GitHub's "Sync fork" button on `master` does the first step too. The roadmap (in Russian) is in [PLAN.md](PLAN.md).

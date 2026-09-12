# Utangard

Utangard gates Valheim's biomes on boss progress. Nothing stops you walking into the Swamp on
day two, but until your group has earned that biome your food burns away five times faster, you
cannot eat or drink, your wounds do not close, and you leave with a stamina penalty that follows
you out.

A biome counts as earned when every character on the group's roster was personally present when
that boss died, not when the boss has died in the world. Kill Moder yourself and the Plains
stays shut until the friend who was offline that night has killed it too. You can switch that
off and gate on the world's own defeat keys instead.

## Features

Everything here is a default and everything is configurable.

- Food burns 5x faster in a gated biome. Nothing is deleted, it just runs out fast.
- Eating and drinking are refused, with a message on screen. The item is never consumed or
  destroyed. Food is refused by `Food.BlockEating`; potions and meads are refused by
  `Buffs.BlockNewBuffs`, at the moment you drink them rather than after the effect is wasted.
- Health regeneration is set to zero. Food is Valheim's only passive healing, so damage taken
  in a gated biome is damage you carry home.
- Buffs already running burn 5x faster, and new ones are refused. Guardian powers are refused
  before the cooldown is spent, so yours is still there when you leave.
- Rested and Resting count as buffs, so a fire and a roof buy you nothing inside. This is the
  harshest rule in the mod and it has its own switch (`Buffs.BlockRested`).
- Harmful effects are never touched. Wet, Cold, Freezing, Burning, Poison and the rest run as
  normal; speeding those up would be a mercy rather than a penalty.
- Leaving a gated biome leaves you **Sapped**: 75% less stamina regeneration. One second inside
  banks one second of it, up to 30 seconds, and it only spends itself once you are out. It
  stacks with food and Rested rather than replacing them.
- The gate reaches 5 m past the edge of a gated biome, so stepping over the line to eat and
  stepping back does not work.
- Two HUD icons, a message on entering and leaving that names who the biome is still waiting on,
  and a message when a biome opens, wherever you are standing.
- A Utangard page in the compendium (the texts screen, beside Logs and Active Effects) listing
  every biome, whether it is open, who still owes it, and how long until the deadline opens it
  anyway.

Dungeons take the biome above them, so a Swamp crypt withers you exactly like the Swamp.

## How the group gate works

- **Credit comes from being at the kill.** When a boss dies, every player within 100 m of the
  body is credited. You do not need the killing blow and you do not need to be the host.
- **Only characters at the frontier count.** A character counts towards a boss once it has the
  boss before it in the table. Somebody who has killed nothing does not hold the Swamp shut for
  a group that cleared Eikthyr and is waiting on The Elder: they count for Eikthyr and nothing
  beyond it. They still get every biome the group has already earned.
- **Joining late does not undo anything.** Once the group clears a boss, that biome is open
  permanently. A friend arriving with a fresh character gates only what the group has not yet
  cleared.
- **A deadline opens the biome anyway.** Once the first person clears a boss, the rest of the
  group has a set number of days before it opens regardless: one day for Eikthyr, one more for
  each boss after. This is what stops one person who stops logging in from holding a biome shut.
- **The roster forgets people who stop playing.** A character stops counting for a boss after 14
  real days without logging in, so a friend who visited for one evening or an alt made once
  cannot hold the gate forever. No admin command and no list to maintain.
- **Existing worlds keep their progress.** A character is credited for a boss its own save file
  says it attended, as long as that boss has already died in this world, so installing on a
  long-running save does not re-lock everything.
- **An empty roster falls back to the world key.** Before anyone has published progress, which
  is the first spawn after installing, the gate answers from the world's own defeat keys.
- **The gate is one answer about the group.** If the roster has not all cleared Moder, the
  Plains withers you too, even if you landed the kill.

## Biome table

The defaults are the vanilla progression offset by one: the boss of the previous biome opens the
next.

| Biome | Opened by | Global key |
| --- | --- | --- |
| Meadows | *nothing* | *ungated* |
| Black Forest | Eikthyr | `defeated_eikthyr` |
| Swamp | The Elder | `defeated_gdking` |
| Mountain | Bonemass | `defeated_bonemass` |
| Plains | Moder | `defeated_dragon` |
| Mistlands | Yagluth | `defeated_goblinking` |
| Ashlands | The Queen | `defeated_queen` |
| Deep North | Fader | `defeated_fader` |
| Ocean | *nothing* | *ungated* |

This is a config table, not a hardcoded progression. Blank a row and that biome is never gated.
Point every row at one key and you have a single-boss gate. Point a row at a key some other mod
sets and it gates on that instead.

The Black Forest row is the one to look at first: gating it on Eikthyr walls off the copper run
most people do before touching him.

`defeated_queen` and `defeated_fader` are not in Valheim's `GlobalKeys` enum, they come from
prefab data, so they can only be read off a running game. Both defaults are confirmed correct.
A key name nothing sets fails closed and looks exactly like a working gate, so on spawn the mod
walks the world's creature prefabs, collects every key any of them sets on death (vanilla's and
any other mod's) and warns if a gate row names a key nothing here can set, listing the ones that
exist.

## Installation

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   **5.4.2350**. It is the only required dependency.
2. Install Utangard with a mod manager, or drop `Utangard.dll` into `BepInEx/plugins/`.
3. Launch once. The config file is written to `BepInEx/config/ezomic.valheim.utangard.cfg`.

Install it on every client and on the dedicated server. Each client enforces the gate on itself,
so a player without the plugin is not gated by it.

Single DLL, no asset bundle, net462. Built against Valheim 1.0.7, BepInEx 5.4.23.5 and Harmony
2.9. Version 1.3.0 does not run on pre-1.0 Valheim, and the versions before it do not run on
1.0.

## Multiplayer

[Longhouse Core](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse_Core/) is an optional soft
dependency. With it installed, the server rejects a client whose Utangard version or build id
does not match, and the host's rule settings are applied on connected clients in memory without
writing their config file. Without it Utangard is fully functional and single player needs
nothing else, but a player who simply does not install the mod is not gated at all, so the gate
becomes an agreement between players rather than a rule of the server. Utangard logs a warning
once if it finds the group gate running in a multiplayer session with no Core.

Settings that decide a rule are synced from the host: all of **Gate**, including the biome keys
and the border margin; the drains and blocks under **Food** and **Buffs**, including the healing
multiplier; and both **Sapped** values. Settings that decide wording stay yours: the two blocked
messages, all of **Presentation**, and all of **Diagnostics**.

Persistence:

- Progress is stored in the world's global keys and saved with the world, so it survives players
  logging out.
- Credit is per world, not per character. A character that cleared a solo world does not arrive
  on your server pre-credited: imported credit is only honoured for a boss this world has
  already seen die, so it can never open a biome on its own.
- Your character file is read, never written.
- Food timers and status effects belong to the owning client. Nothing here reaches into another
  player's character.

## Configuration

`BepInEx/config/ezomic.valheim.utangard.cfg`. Every entry has a comment in the file. BepInEx
writes the file on first run and the saved value beats any new default in code, so if a change
appears to do nothing, check the cfg first.

### Gate

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. Off leaves the game untouched. |
| `GateOnGroup` | `true` | Gate on whether every character on the roster has done the boss. Off gates on the world's own key, so one kill opens the biome for everybody. |
| `GateNeverRegresses` | `true` | Once the group clears a boss, that biome stays open forever. Off makes the gate strictly weakest-link at all times. |
| `RequirePreviousBoss` | `true` | A character counts towards a boss only once it has the boss before it in the table. Off, every character on the roster counts for every gate. |
| `RosterDays` | `14` | Real days a character keeps counting after it was last seen. |
| `RosterDaysPerBoss` | *(empty)* | Per-boss overrides, as comma-separated `key:days` pairs, e.g. `defeated_eikthyr:7, defeated_fader:60`. |
| `BackfillFromCharacter` | `true` | Credit a character for a boss its own save file says it attended, for a boss this world has already seen die. The migration path for existing worlds. |
| `CatchUpDays` | `0` | Fallback deadline in days for any boss not named below. `0` means none. |
| `CatchUpDaysPerBoss` | `defeated_eikthyr:1, defeated_gdking:2, defeated_bonemass:3, defeated_dragon:4, defeated_goblinking:5, defeated_queen:6, defeated_fader:7` | Days the group has to catch up once the first player clears a boss. The clock starts at the first credit recorded in this world and is never moved. |
| `CreditRadius` | `100` | Metres from a dying boss to be credited. |
| `BorderMargin` | `5` | Metres the gate reaches past the edge of a gated biome. `0` puts it exactly on the border. |
| `ExcludePlayerIds` | *(empty)* | Comma-separated character IDs that never count towards the gate. IDs, not names; the roster dump on spawn prints both. |
| `Key_Meadows` … `Key_Ocean` | see the table above | The global key that opens each biome. Blank means never gated. |

### Food

| Setting | Default | What it does |
| --- | --- | --- |
| `FoodDrainMultiplier` | `5` | How much faster food burns. `1` disables the drain and leaves only the refusal, which is a much gentler mod. |
| `BlockEating` | `true` | Refuse to eat food in a gated biome. |
| `EatBlockedMessage` | `The land will not feed you here` | Shown centre-screen when a bite is refused. |
| `HealthRegenMultiplier` | `0` | Health regeneration in a gated biome, as a fraction of normal. `1` leaves healing alone. |

### Buffs

| Setting | Default | What it does |
| --- | --- | --- |
| `BuffDrainMultiplier` | `5` | How much faster an already-running buff burns. |
| `BlockNewBuffs` | `true` | Refuse to apply any new buff, which also covers potions and meads at the point of drinking, and guardian powers. |
| `BlockRested` | `true` | Treat Rested and Resting as buffs. Off, a well-built camp becomes a real answer to the biome. |
| `AlsoBlock` | *(empty)* | Extra status effect names to treat as buffs, comma-separated. The mod finds potions and meads by walking ObjectDB for anything an item applies when consumed, and guardian powers by their `GP_` prefix, so this is for the odd one out. |
| `NeverBlock` | `Puke` | Names to leave alone even if the rules caught them. Wins over `AlsoBlock`. |
| `BuffBlockedMessage` | `The land turns your power aside` | Shown when a potion or a guardian power is refused. Effects that arrive without the player asking, such as equipment or weather, are refused silently. |

### Sapped

| Setting | Default | What it does |
| --- | --- | --- |
| `StaminaRegenMultiplier` | `0.25` | Stamina regeneration while Sapped, as a fraction of normal. |
| `MaxSeconds` | `30` | Ceiling on how much Sapped you can bank, and so how long you must stand in the biome to reach the full penalty. |

### Presentation

| Setting | Default | What it does |
| --- | --- | --- |
| `ShowStatusEffects` | `true` | Show the two effects on the HUD. The rules still apply when this is off. |
| `MarkIconFrom` | `Poison` | Vanilla status effect whose icon the in-biome marker borrows. |
| `SappedIconFrom` | `Encumbered` | Vanilla status effect whose icon Sapped borrows. |
| `EnterMessage` | `Something here refuses you` | Shown once on entering. Blank to say nothing. |
| `LeaveMessage` | `The land loosens its grip` | Shown once on leaving. Blank to say nothing. |
| `NameTheBlockers` | `true` | Name the characters the biome is still waiting on, and how long is left on the deadline. |
| `BlockedByPrefix` | `Still owed by:` | Prefix for that list. |
| `AnnounceOpenings` | `true` | Say so, wherever you are, when a biome opens. Covers openings nobody killed anything for, such as a deadline expiring. |
| `OpenedMessage` | `{biome} opens to you` | That message. `{biome}` becomes the biome's name, or both names when one boss opens two. |
| `ShowCompendiumPage` | `true` | Add the Utangard page to the compendium's text list. |
| `CompendiumTopic` | `Utangard` | What that page is called in the list. |

### Diagnostics

| Setting | Default | What it does |
| --- | --- | --- |
| `Verbose` | `false` | Log every gate transition and blocked effect as it happens. |
| `LogGlobalKeys` | `true` | Log the world's keys, the roster and the whole gate table on spawn. Worth leaving on: it is how you catch a wrong key name. |
| `LogBlockedEffects` | `false` | Log the full list of status effects the mod decided are buffs, and the ones it left alone. The list to consult before editing `AlsoBlock`. |
| `LogDefeatKeys` | `false` | Log every key a creature in this world sets on death, and what sets it. |

## Troubleshooting

**A biome will not open.** Open the compendium page, or read the spawn dump in
`BepInEx\LogOutput.log`. Both name the roster, who still owes each boss, and how long is left on
the catch-up deadline. The usual cause is a character on the roster that has not been at that
kill; `ExcludePlayerIds` or `RosterDays` are the way out if that character is not coming back.

**A biome is shut and nobody is named.** The log warns when a gate row names a key no creature
in this world sets, and lists the keys that do exist. That is either a typo in the table or a key
that comes from somewhere else, and it fails closed either way.

**A config change did nothing.** BepInEx saves the config on first run and the saved value wins.
Edit the cfg, not the default.

**The log says Utangard is running DEGRADED.** A game update has moved one of the methods the
mod patches. The line names which feature that cost; everything else is still in force.

**The log says Utangard is NOT withering anybody.** Neither of the two ways of recording a boss
kill survived patching, so a biome that is shut could never open again. The mod stops enforcing
anything until it is updated. Setting `Gate.GateOnGroup = false` gates on the world's own keys
instead, which needs none of the mod's patches to open.

**Upgrading from Wither.** The mod was called Wither before 1.1.0. Keys written under the old
name are still read, and are rewritten under the new one as people play, so no progress is lost.

## For mod authors

`Utangard.UtangardApi.GroupHasKey("defeated_bonemass")` answers whether the group has earned a
boss, which is not the same question as the world's raw `defeated_` key. Take a soft dependency
on `ezomic.valheim.utangard`. Yoke uses it so that stack sizes follow the group's progress rather
than a kill nobody else was present for.

## Status

Played on a local world and on a dedicated server: refused meals and potions keep their items,
both HUD icons render, food and buff timers burn at 5x, Sapped accumulates and follows you out, a
guardian power is refused without burning its cooldown, the border margin refuses a player
standing three metres outside a gated biome, healing is blocked, gates open and close at borders
in both directions, credit is granted at the kill and survives a world reload, a two-character
roster names both debtors, the catch-up deadline opens a biome, the compendium page names each
boss, the biome-opened announcement fires on the transition, and the defeat-key check verifies
all nine rows against the world's own creature prefabs. Running standalone with no Core has been
confirmed in game.

One thing is untested: attendee credit with more than one player at a boss kill. Solo you own
the boss and credit yourself either way. The loop is identical for one player or five; what is
unproven is whether other players' objects are instantiated on the owning client at fight range.

## Design notes

The long-form reasoning, and the technical notes on how Valheim records boss attendance, are in
[DESIGN.md](DESIGN.md).

## Bug reports

[The Discord](https://discord.gg/hJzAVaZ5wb) is the fastest route, and the right one if you are
not sure whether what you are seeing is a bug. Issues on
[the repo](https://github.com/Ezomic/valheim-utangard) work too and suit anything long.

Attach `BepInEx\LogOutput.log`, say whether you were on a server or in single player, and if the
gate is doing something you did not expect, turn on `Diagnostics.LogGlobalKeys` and include the
spawn dump: it carries the world's keys, the roster and the verdict on every biome.
`AppData\LocalLow\IronGate\Valheim\Player.log` is worth adding when a vanilla mechanic broke,
since exceptions thrown mid-frame land there rather than in the BepInEx log.

## Discord

[discord.gg/hJzAVaZ5wb](https://discord.gg/hJzAVaZ5wb) is used for mod information, updates,
support, bug reports and compatibility questions.

## Server

There is also a small EU server running the pack if you want somewhere to play: hard combat
difficulty, resources at 1x, everything else vanilla, no application and no activity
requirements. Connection details are in the Discord.

## Part of Longhouse

Utangard is part of [Longhouse](https://thunderstore.io/c/valheim/p/Ezomic/Longhouse/), the
Ezomic modpack, which pins exact versions of its member mods. You do not need the pack to use
Utangard, and it behaves the same on its own.

MIT licensed. By Robbin Thijssen (Thijssen Software).

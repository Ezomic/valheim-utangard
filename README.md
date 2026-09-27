# Utangard

Utangard gates Valheim's biomes on boss progress. Nothing stops you walking into the Swamp on
day two, but until your group has earned that biome your food burns away five times faster, you
cannot eat or drink, your wounds do not close, and you leave with a stamina penalty that follows
you out. Your own character can win some of that back by fighting and exploring there: eating
first, then healing (see [Earning a foothold](#earning-a-foothold)).

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
  Eating comes back once you have fought enough in that biome (see
  [Earning a foothold](#earning-a-foothold)).
- Health regeneration is set to zero. Food is Valheim's only passive healing, so damage taken
  in a gated biome is damage you carry home, until you have a full foothold there.
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
- A Utangard page in the compendium (the texts screen, beside Logs and Active Effects). A row of
  every biome runs across the top, green when open and red when locked. Pick one to see who it is
  still waiting on and how long until the deadline opens it anyway. For a locked biome it also
  shows your two foothold bars and the rules of the lock as your server has set them.

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

## Earning a foothold

Pidgey put the problem with the lock plainly while playing on Longhouse: a locked biome was a
wall, not a challenge, and the inability to eat was the problem. You could not stay long enough
to do anything there, so there was no reason to go. A character can now earn part of it back,
one biome at a time.

Every locked biome has two bars for each character.

- **Fighting** fills from kills of that biome's creatures. Each kind is worth a set number of
  points, 1 or 2 for the common ones up to 5 for the biggest threat, and the bar is full at
  150. One kind of creature can put in half of that at most, 75 points, so you cannot fill it
  on trolls alone. Helping with a kill counts the same as landing the last blow.
- **Discovery** fills from the part of that biome's map you uncovered yourself. It is full at
  1 km², the same in every biome. What a map table shares with you does not count. Each piece
  of the map counts once, so walking back and forth over the same ground earns nothing.

At 50% Fighting you can eat in that biome again. With both bars full your wounds heal there at
the normal rate. The rest of the lock stays as it is: meads, powers and Rested are still
refused, food and buffs still burn faster, and you still leave Sapped.

The bars are yours alone. They only lift the rules in the biome they were earned for, so a full
bar in the Swamp does nothing for you in the Mountains. Where two locked biomes meet you need the
unlock in both: within 5 m of the other one, its rules reach you too. They never open a biome for
the group either. That still takes the boss, or the catch-up deadline.

Only kills Utangard saw happen count, and only in the world they happened in. Utangard keeps
its own count in your character, one for each world. Everybody's bars started at zero with this
version, so kills from before it do not count. Kills made in another world never count here
either, and a singleplayer world is another world. Some kills never count:

- A tame animal, or one bred from tame parents. Lox, wolves and asksvin are on the lists, but
  slaughtering your own herd earns nothing.
- Your kill while you have devcommands on, or are in god mode, ghost mode or debug flight, or
  hold a weapon spawned with devcommands. The game ignores a player's devcommands on a dedicated server, and so
  does this.
- A creature spawned with devcommands, or wiped out with `killall` or `killenemies`. Those count
  for nobody, not even the players who had already hit them.
- A kill that lands after you have left. The kill is counted on the machine that had the
  creature and sent to everyone who hit it, so if you log out before it dies, you miss it.

One gap is left. If somebody in god mode helps you with a kill, the game only notices when
their own machine had the creature. When any other machine had it, their share is still refused
on their side, but yours counts, because no machine can see god mode on anybody else's
character.

The map lifts the fog in a wide circle around you, about 100 m, not just under your feet.
Walking along a border or sailing along a coast uncovers some of the biome on the other side, and
that counts too. The map does not remember where you stood, so you could fill the whole bar from
outside. That is why Discovery asks for a whole square kilometre: at half of that, five kilometres
of coastline did it, and now it takes about ten.

The Utangard page in the compendium shows both bars for whichever biome you pick, and it opens
on the first biome your group has not earned. Click another biome in the row to see it, or use
left and right on the d-pad with a controller. A refused meal tells you how far your Fighting bar
has got. `utangard foothold` in the console (F5) shows both bars for every biome, how many of
each creature Utangard has counted for you, and what each kind has added. What each creature is
worth is in the config, one line per biome, under **Foothold**.

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
becomes an agreement between players rather than a rule of the server. Footholds lose out too: a
creature that dies on the machine of a player without Utangard is counted for nobody. Utangard
logs a warning once if it finds the group gate running in a multiplayer session with no Core.

Settings that decide a rule are synced from the host: all of **Gate** apart from its two
messages, including the biome keys and the border margin; the drains and blocks under **Food**
and **Buffs**, including the healing multiplier; both **Sapped** values; and all of **Foothold**,
including the points lines. Settings that decide wording stay yours: all four blocked messages,
all of **Presentation**, and all of **Diagnostics**. That needs Core 1.1.0 or later. On an older
Core the host's wording applies too, and the log says so.

Persistence:

- Progress is stored in the world's global keys and saved with the world, so it survives players
  logging out.
- Credit is per world, not per character. A character that cleared a solo world does not arrive
  on your server pre-credited: imported credit is only honoured for a boss this world has
  already seen die, so it can never open a biome on its own.
- Utangard writes one thing to your character file: its foothold kill counts, one entry for each
  world you play. Everything else in the file is only read.
- Food timers and status effects belong to the owning client. Nothing here reaches into another
  player's character.
- A foothold is read from your own character on your own machine: the kills Utangard counted
  for you in this world, and the map you have explored here. A kill is counted on whichever
  machine had the creature, which tells each player who hit it. Nothing about a foothold is
  saved in the world.

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
| `BlockBossSummons` | `true` | An altar in a biome the group has not earned will not summon its boss, and the offering is not used up. It also covers the Queen's door, below. Each altar stands in the biome the boss before it opens, so it answers as soon as that biome does. |
| `BossBlockedMessage` | `The land will not answer an offering here` | Shown when an altar refuses. |
| `BossDoorKeys` | `DvergrKey` | Doors opened with these keys stay sealed while their biome is locked. The Sealbreaker opens the Queen's door, the one boss not summoned at an altar. The key is not used up, and an open door stays open. Empty turns it off. |
| `BossDoorBlockedMessage` | `The seal will not break here yet` | Shown when a boss door stays shut. |
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

### Foothold

| Setting | Default | What it does |
| --- | --- | --- |
| `FootholdEnabled` | `true` | Let a character earn eating and healing back in a locked biome. Off puts the lock back exactly as it was. |
| `FightingFullPoints` | `150` | Fighting points that make a full bar. Healing needs this bar full, and Discovery too. |
| `EatAtFightingPercent` | `50` | Where you may eat again, as a percent of a full Fighting bar. Food only; meads and potions stay refused. Above 100 means never. |
| `DiscoveryFullKm2` | `1` | How much of a biome's map, in km², you have to uncover yourself for a full Discovery bar. `0` means no walking is needed. |
| `MaxFromOneKindPercent` | `50` | The most one kind of creature can add to a Fighting bar, as a percent of a full bar. |
| `Points_Meadows` … `Points_Ocean` | see below | What each kill is worth there, as `Prefab:points` pairs. A creature that is not listed is worth nothing in that biome. |

The two percent settings are shares of `FightingFullPoints`, so if you raise the bar, eating
and the limit per kind stay at half of it.

The points lines, as they ship. The Meadows and the Ocean are empty because neither is gated by
default.

| Biome | Points |
| --- | --- |
| Black Forest | `Greydwarf:1, Skeleton:1, Greydwarf_Shaman:2, Greydwarf_Elite:3, Bjorn:4, Troll:5` |
| Swamp | `Draugr:1, Blob:1, Leech:1, Surtling:1, BlobElite:2, Wraith:2, Draugr_Elite:3, Writhan:3, Abomination:5` |
| Mountains | `Wolf:1, Ulv:1, Hatchling:2, Fenring_Cultist:2, Fenring:3, StoneGolem:5` |
| Plains | `Deathsquito:1, Goblin:1, BlobTar:1, GoblinShaman:2, Lox:3, GoblinBrute:4, Unbjorn:5` |
| Mistlands | `Seeker:1, Tick:1, Dverger:2, DvergerMageSupport:3, DvergerMageFire:3, DvergerMageIce:3, SeekerBrute:4, Gjall:5` |
| Ashlands | `Charred_Archer:1, Charred_Twitcher:1, Volture:1, BlobLava:1, Charred_Melee:2, Asksvin:2, Charred_Mage:3, BonemawSerpent:4, FallenValkyrie:5, Morgen:5, Morgen_NonSleeping:5, Charred_Melee_Dyrnwyn:5` |
| Deep North | `GoblinDeepNorth:2, Elaking:2, ElakingLantern:2, ElakingMole:2, DvergerDeepNorth:2, Moose:3, ShadowPerson:3, JotunWitch:4, JotunWarrior:5, JotunWarriorDualWield:5, Barka:5` |

Prey is left out on purpose, and so are Swamp skeletons. Some creatures share one name in the
game, like the frozen greydwarves of the Deep North and the ones at home, so a name listed in two
biomes only counts in the earlier one. The three dvergr mages are one name as well,
which is why they are all worth 3. `utangard creatures` checks every line against the game.

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
| `EatProgressLine` | `Fighting here {fighting}%. You can eat at {eat}%.` | Added under a refused meal. `{fighting}`, `{eat}` and `{biome}` are filled in. Blank to say nothing. |
| `AnnounceOpenings` | `true` | Say so, wherever you are, when a biome opens. Covers openings nobody killed anything for, such as a deadline expiring. |
| `OpenedMessage` | `{biome} opens to you` | That message. `{biome}` becomes the biome's name, or both names when one boss opens two. |
| `ShowCompendiumPage` | `true` | Add the Utangard page to the compendium's text list. |
| `CompendiumTopic` | `Utangard` | What that page is called in the list. |
| `ShowCompendiumPanel` | `true` | Draw that page as the panel with the biome row and the foothold bars. Off shows the plain text page, which also lists the roster. |

### Diagnostics

| Setting | Default | What it does |
| --- | --- | --- |
| `Verbose` | `false` | Log every gate transition and blocked effect as it happens. |
| `LogGlobalKeys` | `true` | Log the world's keys, the roster and the whole gate table on spawn. Worth leaving on: it is how you catch a wrong key name. |
| `LogBlockedEffects` | `false` | Log the full list of status effects the mod decided are buffs, and the ones it left alone. The list to consult before editing `AlsoBlock`. |
| `LogDefeatKeys` | `false` | Log every key a creature in this world sets on death, and what sets it. |

## Troubleshooting

**A biome will not open.** Open the compendium page, or read the spawn dump in
`BepInEx\LogOutput.log`. Both name who still owes each boss and how long is left on the catch-up
deadline, and the spawn dump lists the whole roster. The usual cause is a character on the
roster that has not been at that kill; `ExcludePlayerIds` or `RosterDays` are the way out if that
character is not coming back.

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

Footholds are new and have not been run in game yet: neither bar, the kill counting behind
Fighting, the two unlocks, `utangard foothold`, nor the compendium panel that shows them.

One more thing is untested: attendee credit with more than one player at a boss kill. Solo you own
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

## Bugs and ideas

Both go to the site. [longhouse.thijssensoftware.nl/bugs](https://longhouse.thijssensoftware.nl/bugs)
is for anything broken, and [longhouse.thijssensoftware.nl/ideas](https://longhouse.thijssensoftware.nl/ideas)
is for what a mod should do next. You can vote on other people's ideas there as well.

Signing in takes a Steam or Discord account. I work from that list, so the votes decide what
I pick up next.

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

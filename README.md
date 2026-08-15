# Wither

A biome you have not earned will not feed you.

While a biome's boss is still alive, standing in that biome burns your food and your buffs
down five times faster, refuses to let you eat or drink anything at all, and leaves you
**Sapped** — seventy-five percent less stamina regeneration — for one second per second you
spent there, up to half a minute, which keeps ticking after you leave.

## Why this and not a wall

Valheim already gates its biomes. It gates them with damage, and damage is a soft gate: it
can be out-geared, out-run, or simply out-healed, which is why the Plains stops being
frightening about ten minutes after it starts. The usual mod answer is the opposite extreme —
a hard boss gate that refuses to let you across the border at all. That fixes the pacing by
deleting the thing worth having, which is the walk into somewhere you should not be.

This sits between them. You can go anywhere, immediately, and nothing stops you at the edge.
The land just will not sustain you while you are there. So a run into the Plains for barley
before Moder is still possible, still the player's call, and now genuinely a raid: you go in
on whatever food you are already carrying, you watch it burn, and you come out slower than
you went in.

The three parts do different jobs, which is why they are three parts and not one number:

- **The drain** sets the clock. It is the part you feel while things are going well.
- **The refusal** makes the clock real. Without it the drain is beaten by a bigger pack, and
  the mod becomes an inventory tax rather than a time limit.
- **Sapped** makes leaving cost something. Without it the optimal play is to sprint in,
  grab, and sprint out with no consequence at all — and a penalty you can dodge by being
  quick is a penalty for slow players only.

## The gate is on the group, not the world

By default the biome opens when **every member of the roster** has personally done the boss —
not when the boss has died in this world. Kill Moder yourself and the Plains stays shut until
the friend who was offline that night has killed it too.

This asks for something Valheim seems not to record. It turns out it does. In
`Character.OnDeath`:

```csharp
if (!string.IsNullOrEmpty(m_defeatSetGlobalKey))
    Player.m_addUniqueKeyQueue.Add(m_defeatSetGlobalKey);

if ((bool)m_nview && !m_nview.IsOwner())
    return;                                    // ← the early-out comes AFTER
```

That push happens *before* the ownership early-return, so it runs on every client that had
the boss loaded when it died — precisely "everyone who was there". It lands in
`Player.m_uniques`, which `Player.Save` writes and `Player.Load` reads — so **existing
characters already carry their past attendance**.

**But that lands there later than it looks, and the first version of this mod got it wrong.**
`m_addUniqueKeyQueue` is a *static* list, drained only by `AddQueuedKeys`, which is called
from exactly two places — `Player.Start` and `SetLocalPlayer`. Both are spawn-time. So
`m_uniques` does not contain a boss you killed this session until you next spawn, and if you
quit to desktop before respawning, the queue dies with the process and the credit is lost
outright.

Found by playing rather than by reading: Eikthyr was killed, the world key was set, and no
credit was ever published. So Wither hooks `Character.OnDeath` itself and credits the local
character at the moment of the kill. `m_uniques` is still read on spawn, because it is the
only place credit earned *before* this mod lives — it is the backfill, not the live path.

The other half is visibility, since `m_uniques` is local and nothing replicates it. Each
client republishes its own record into the world's **global keys** —
`wither_p_<characterId>_<bosskey>`, plus one `wither_seen_<characterId>` heartbeat. Global
keys are broadcast to every client on connect and saved with the world, which is the point: a
gate that forgot people the moment they logged off would not be a group gate at all.

**The roster is self-pruning.** A character joins it the first time it spawns with the mod and
drops out after `RosterDays` (14) of absence. Two things fall out of that. Characters that
predate the mod are not on it, so installing on a long-running world does not instantly
wither everyone on behalf of an alt nobody has touched since spring. And leaving needs no
admin command — stop logging in and you stop counting.

`GateOnGroup = false` restores the original behaviour: one kill opens the biome for all.

### This makes the mod mandatory

A player without the plugin never publishes, so under a group gate they would hold every biome
shut for everyone. That is why Wither registers with Core at `Requirement.Everyone` — the
server refuses a client that does not have it, at a matching version.

Which exposed something: **Core carried `[BepInProcess("valheim.exe")]`**, so it never loaded
on a dedicated server, so `NetworkPatches`' gate only ever ran on a listen host and the
`IsServer()` branch — the only one that can refuse a connection — was unreachable. Every
dedicated server in this family was ungated. It also meant Delve, which declares Core a hard
dependency and has no `BepInProcess` of its own, could not load on a dedicated server at all.
Removing the attribute fixes both.

> **Deploy this deliberately.** Once Core reaches a dedicated server, `EnforceVersions`
> defaults to on and the server starts refusing anyone whose Ezomic mod set does not match.
> Update the server and every player together, or set `EnforceVersions = false` first.

## The gate is a table

`Key_<Biome>` in the config maps a biome to the global key that opens it. Defaults are the
vanilla progression offset by one: the boss of the previous biome opens the next.

| Biome | Needs |
| --- | --- |
| Meadows | — |
| Black Forest | `defeated_eikthyr` |
| Swamp | `defeated_gdking` (The Elder) |
| Mountain | `defeated_bonemass` |
| Plains | `defeated_dragon` (Moder) |
| Mistlands | `defeated_goblinking` (Yagluth) |
| Ashlands | `defeated_queen` |
| Deep North | `defeated_fader` |
| Ocean | — |

Blank a row and that biome is never gated. Point every row at one key and you have a
single-boss gate. Point a row at a key some other mod sets and it gates on that instead.

**The Black Forest row is the one to look at first.** Gating it on Eikthyr walls off the
copper and core run that most people do before touching him, which is the intended shape but
is also a real change to the opening hour.

**The Queen's and Fader's keys are not in the game's `GlobalKeys` enum** — they are set from
prefab data, which `ZoneSystem.GetGlobalKey` handles fine as plain strings but which means
they could not be verified against the code the way the first five could. `LogGlobalKeys` is
on by default and prints both the world's real key set and the whole gate table on spawn.
Check it once against a save where those bosses are down.

A key name that no world ever sets fails *closed* and looks exactly like a working gate, so a
typo there is silent. That log line is the only thing standing between you and a permanently
withered Ashlands.

## What counts as a buff

Found rather than listed, because a list of potion names goes stale the first time the game
ships a new potion:

1. Anything an item applies when you consume it — every potion and every mead, read straight
   off `ObjectDB`.
2. Anything named `GP_*` — the guardian powers.
3. `Rested` and `Resting`, if `BlockRested` is on.

Everything else passes through untouched, and that is the important half. Wet, Cold,
Freezing, Burning, Poison, Frost, Smoked, Tared and Spirit are how the game does weather and
damage. An allowlist of harmful effects was written first and thrown out: it can only ever be
as complete as the day it was written, and every name missing from it hands the player an
immunity in the biome that is supposed to be killing them.

`BlockRested` is the harshest single switch here. On, a forward base in the Plains gives you
shelter and warmth and no regeneration at all. Off, a well-built camp becomes a real answer
to the biome, which is a different and gentler game.

## Things that were nearly bugs

Recorded because each one took reading the game to find and would have looked like a design
choice from the outside:

- **`Player.ConsumeItem` removes the item whether or not `EatFood` succeeded.** Refusing food
  in `EatFood` — the obvious seam, and the one with the right name — would have destroyed
  every meal and every potion it refused. The block lives in `CanConsumeItem`, which is the
  gate that path actually respects and where vanilla puts its own refusal.
- **`SEMan` refreshes an already-running effect without going through `AddStatusEffect`.**
  `Internal_AddStatusEffect` calls `ResetTime` in place and returns. Sitting by a fire
  refreshes `Rested` through exactly that path, so patching only the public overload would
  have topped it back up faster than the drain could take it down and made `BlockRested` do
  nothing at all.
- **`StartGuardianPower` sets the cooldown before it applies the effect.** Blocking only the
  status effect would have burned a twenty-minute power on nothing.
- **`Puke` is applied by an item on consume**, so the "anything a consumable grants" rule swept
  it up as a buff — found on the first run in a real world, not by reading. Blocking a debuff
  would have made a gated biome the one place bad food cannot hurt you, and the drain would
  have made it wear off *faster* there than anywhere else. It ships in `NeverBlock`.
- **Every global key write also does `m_knownWorldKeys.IncrementOrSet(key + " " + value)`**
  into the saved player profile. A heartbeat carrying a fresh timestamp would have added an
  entry to that dictionary every beat, forever, in everyone's character file. The last-seen
  value is therefore a whole day number, and nothing is written unless it changed.
- **The gate is asked several times a frame** — by the tick, by both status effects, and by
  every consume and every status effect the game tries to apply. Building the roster walks
  every global key in the world, so it is cached for two seconds, and the list of names owed
  is built only on a transition rather than on every query.

## Configuration

Everything is in `BepInEx/config/ezomic.valheim.wither.cfg`, and BepInEx writes that file on
first run — after which the saved value beats any new default in code. Change a default here
and you must edit the cfg too, or nothing happens.

Headline knobs: `FoodDrainMultiplier` and `BuffDrainMultiplier` (5), `BlockEating` and
`BlockNewBuffs` (on), `BlockRested` (on), `StaminaRegenMultiplier` (0.25) and `MaxSeconds`
(30) for Sapped. `Enabled` turns the whole thing off without unloading it.

## Scope and honesty

**The gameplay is client-side; the gate is not.** Food timers and status effects belong to the
owning client and nothing here reaches into another player's character. But the group gate
reads and writes world state, and it only means anything if every client is publishing — hence
the server requirement above.

**What has actually been tested.** One session on a fresh world confirmed: the plugin loads,
the buff classification is right (7 guardian powers, 21 potions, Rested/Resting caught;
armour sets, trinkets, Wishbone, Demister and every harmful effect left alone), both HUD icons
resolve, the global key dump is correct, and the gate closes and opens cleanly crossing
Meadows ↔ Black Forest with no exceptions.

**What has not.** Everything visual, and the whole group gate:

- That a refused meal **keeps its item**. The code takes the seam that makes this safe, but it
  has not been watched happen.
- The two icons rendering, the enter/leave messages, and the 5× food drain by eye.
- That **Sapped accumulates and drains at the right rate** — it is charged by pushing an
  elapsed-time counter backwards, which is the least obvious thing in the mod.
- **The entire group gate.** Written after the only play session, so none of it has run:
  publishing, the roster window, the "still owed by" line, or the Core version gate.
- Whether `defeated_queen` and `defeated_fader` are the real key names. They are set from
  prefab data rather than named in the `GlobalKeys` enum, so they could not be verified from
  the code, and a wrong key fails *closed* and looks exactly like a live gate.
- **Dungeons.** Crypts and mines sit in their own zone with no heightmap, which reports as
  `Biome.None` and is therefore ungated — a swamp crypt is a refuge from the swamp. That
  follows from the game's layout rather than from a decision, and it may want revisiting once
  it has been felt.
- Whether the Black Forest row survives contact with an actual new character.

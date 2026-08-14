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

## Configuration

Everything is in `BepInEx/config/ezomic.valheim.wither.cfg`, and BepInEx writes that file on
first run — after which the saved value beats any new default in code. Change a default here
and you must edit the cfg too, or nothing happens.

Headline knobs: `FoodDrainMultiplier` and `BuffDrainMultiplier` (5), `BlockEating` and
`BlockNewBuffs` (on), `BlockRested` (on), `StaminaRegenMultiplier` (0.25) and `MaxSeconds`
(30) for Sapped. `Enabled` turns the whole thing off without unloading it.

## Scope and honesty

**Client-side.** Food timers and status effects belong to the owning client in Valheim, so
there is nothing for a dedicated server to enforce and nothing goes over the wire. A player
without the plugin plays vanilla. This is a rule for a group that all runs it, not a lock.

**Built and never played.** It compiles and the seams were read out of the game's own
assemblies rather than guessed, but no part of it has been seen working in a world yet.

What to check first, in rough order of how likely it is to be wrong:

- The global key dump on spawn, against a world with late bosses down.
- That **Sapped accumulates and drains at the right rate** — it is charged by pushing an
  elapsed-time counter backwards, which is the least obvious thing in the mod.
- That the two HUD icons appear at all. They borrow vanilla sprites, and the HUD skips any
  effect whose icon is null, so a bad donor name means an invisible effect rather than an
  error.
- **Dungeons.** Crypts and mines sit in their own zone with no heightmap, which reports as
  `Biome.None` and is therefore ungated — a swamp crypt is a refuge from the swamp. That
  follows from the game's layout rather than from a decision, and it may want revisiting once
  it has been felt.
- Whether the Black Forest row survives contact with an actual new character.

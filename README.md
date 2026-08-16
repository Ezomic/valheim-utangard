# Wither

A biome you have not earned will not feed you.

While a biome's boss is still alive, standing in that biome burns your food and your buffs
down five times faster, refuses to let you eat or drink anything at all, and leaves you
**Sapped** — seventy-five percent less stamina regeneration — for one second per second you
spent there, up to half a minute, which keeps ticking after you leave.

## Where this came from

A particular kind of evening. Somebody has not killed The Elder yet and the Swamp trip is
already being planned. Somebody else has been up a Mountain and come back with onion seeds, so
the whole server is eating onion soup in the Black Forest. Nobody cheated and nothing is broken:
Valheim's gates are made of damage, and damage is something a careful player with a decent
shield walks straight through hours early. But the food comes back with them, and so does the
gear and the map.

What that does is not "harder" or "easier" in the abstract. It makes the game **easy early and
empty later**. The opening hours get trivialised by food nobody should have yet, and the biomes
those things were taken from have nothing left to give when the group finally arrives at them
properly. Progression stops being a sequence of places you earn and turns into a shopping list
you can run in any order.

Everything below is aimed at that. The drain is what makes the early raid cost something; the
group gate is what stops one person's shopping trip from setting the difficulty for everybody
else.

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

### What this is for

Valheim groups come apart along progression. One person plays more, gets ahead, and the others
arrive to find the interesting content already cleared — so nobody wants to redo it, the person
ahead has no reason to go back, and what started as a group becomes several people playing
alone in the same world.

The gate is aimed squarely at that. It makes helping the people behind you **the way you move
forward yourself**, not a favour you do them. If your friend has not killed The Elder, the
Swamp is shut for you as well, so going back to fight him again is not charity — it is the next
thing on your own list. The pressure is real but it points somewhere useful: at organising the
fight, at bringing the person who missed it, at the group arriving somewhere together instead
of trickling in.

It is a coordination device, not a punishment. Nothing is taken away from anyone, and the
person who is ahead is not penalised for being ahead — they are simply given a reason to turn
around, which the base game never gives them.

Two rules exist to keep that from curdling into a hostage situation, and both are worth reading
as part of the same idea: **progress never regresses**, so a newcomer cannot revoke a biome the
group already earned, and **the catch-up deadline** means a single person who stops logging in
cannot hold a biome shut indefinitely. The gate should make you fetch your friend, not trap you
behind them.

This asks for something Valheim seems not to record. It turns out it does. In
`Character.OnDeath`:

```csharp
if (!string.IsNullOrEmpty(m_defeatSetGlobalKey))
    Player.m_addUniqueKeyQueue.Add(m_defeatSetGlobalKey);

if ((bool)m_nview && !m_nview.IsOwner())
    return;                                    // ← the early-out comes AFTER
```

That push sits *above* the ownership early-return, which reads as "every client that had the
boss loaded is credited — everyone who was there". **That reading is wrong, and it cost two
bugs.** The key does land in `Player.m_uniques`, which `Player.Save` writes and `Player.Load`
reads, so **existing characters do carry their past attendance** — but how it gets there, and
for whom, are both different from how they look.

**It lands later than it looks.** `m_addUniqueKeyQueue` is a *static* list, drained only by
`AddQueuedKeys`, called from exactly two places — `Player.Start` and `SetLocalPlayer`, both
spawn-time. So `m_uniques` does not contain a boss you killed this session until you next
spawn, and quitting to desktop before respawning loses the credit with the process. Found by
playing: Eikthyr died, the world key was set, nothing was recorded.

**And it lands for one player, not all of them.** That `!m_nview.IsOwner()` guard is
unreachable. `CheckDeath` is `OnDeath`'s only caller, and `CheckDeath` is called from exactly
one place — inside `if (zDO.IsOwner())` in `Character.CustomFixedUpdate`. `OnDeath` runs on the
owning client and nowhere else. Crediting "the local player" from it would credit precisely one
member of a group that killed a boss together, and the gate would then stay shut forever while
looking exactly like it was working.

So Wither hooks `Character.OnDeath` and the owning client credits **everyone within
`CreditRadius` of the corpse**, on the group's behalf. It can: global keys are world state,
writable for anyone, and `Player.GetPlayersInRange` sees every player instantiated on that
client — which, at a boss fight, is all of them. The radius defaults to a generous 100 m
because the two failure modes are not symmetric: crediting a bystander costs one person's sense
of having earned it, while missing a genuine participant holds the gate shut for the whole
group with no remedy short of killing the boss again.

`m_uniques` is still read on spawn, because it is the only place credit earned *before* this
mod lives — it is the backfill, not the live path.

The other half is visibility, since `m_uniques` is local and nothing replicates it. Each
client republishes its own record into the world's **global keys** —
`wither_p_<characterId>_<bosskey>`, plus one `wither_seen_<characterId>` heartbeat. Global
keys are broadcast to every client on connect and saved with the world, which is the point: a
gate that forgot people the moment they logged off would not be a group gate at all.

### Progress does not regress

Once the whole roster has cleared a boss, that biome is latched open permanently
(`wither_open_<bosskey>`) and the question is never asked again.

Without the latch the gate runs backwards, and unpleasantly: a friend joining with a fresh
character decides nobody has done Eikthyr and shuts the Black Forest *for the people who
killed him*, retroactively, for as long as that friend keeps logging in. Progress a group has
paid for should not be revocable by somebody else's arrival. What a newcomer still gates is
everything the group has **not** yet cleared — which is where the "bring your friend" pressure
belongs anyway.

The latch is only written when the roster is non-empty and every member has the boss. Latching
off the empty-roster fallback would let one client's loading screen open a biome forever.

### Credit is per world, not per character

`m_uniques` is stored per *character* and is world-agnostic — it means "this character was
present at an Eikthyr death", anywhere, ever. Taken at face value that is a hole straight
through the gate: clear a solo world, bring that character to the server, arrive pre-credited
for bosses nobody here has fought.

So the backfill from `m_uniques` is only trusted for a boss **this world has already seen
die**. If the boss is still alive here, imported credit is refused. If it is dead, the world
has demonstrably progressed past it, and crediting a character who was probably one of the
people who did it is both harmless and the only way an existing server gets backfilled at all —
nobody's past kills were recorded by a mod that did not exist yet.

Kills witnessed here are unaffected: the `Character.OnDeath` hook always credits, because it
saw it happen.

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

**What has been played, not merely built.** On a local world and on a real dedicated server:

- Refused meals and refused potions **keep their items** — the trap that would have destroyed
  them silently, and the one worth checking first.
- Both HUD icons render; the enter and leave messages appear; food and buff timers visibly
  burn at 5×.
- **Sapped** accumulates, counts down, follows you out of the biome, and cripples stamina
  regeneration while it lasts.
- A guardian power is refused **without burning its cooldown** — usable again the moment you
  leave.
- Gates close and open at biome borders, both directions, with no exceptions in a long session.
- Credit is granted at the kill, published, and **survives a world reload**. The latch fires
  and is read back. The `IsGateKey` guard keeps trolls and surtlings out of the key set.
- A **two-character roster** naming both debtors, and the catch-up **deadline opening a biome**
  for a group that had not all earned it.
- `BackfillFromCharacter = false` holding: a character carrying credit from another world got
  nothing here.
- The buff classification: 7 guardian powers, 21 potions, Rested and Resting caught; armour
  sets, trinkets, Wishbone, Demister, Warm and every harmful effect left alone.

**What has not been tested, and only this:**

- **Attendee credit with more than one player.** Solo you own the boss ZDO and credit yourself
  either way, and two characters taken in turns only credits whoever is logged in. The loop is
  identical for one player or five; what is unproven is whether other players' objects are
  instantiated on the owning client at fight range.
- Whether `defeated_queen` and `defeated_fader` are the real key names. They are set from
  prefab data rather than named in the `GlobalKeys` enum, so they could not be verified from
  the code, and a wrong key fails *closed* — indistinguishable from a working gate. Turn on
  `LogGlobalKeys` and read what your world actually has.

**Dungeons are gated, and inherit the biome above them.** Worth stating because the obvious
guess is the opposite. Crypt and mine interiors are generated at `y > 3000`, which sounds like
somewhere with no terrain and therefore no biome — but `Heightmap.FindBiome` resolves through
`IsPointInside`, and that compares **only X and Z**. The interior sits directly above its own
entrance, in a zone whose heightmap is loaded because you are standing in it, so it reports the
surface biome. A Swamp crypt withers you exactly like the Swamp. Convenient, and entirely
accidental.

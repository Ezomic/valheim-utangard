# Wither

> **A biome you have not earned will not feed you.**

Nothing stops you walking into the Swamp on day two. But while you are there, food burns down
five times faster, you cannot eat or drink at all, and you leave **Sapped**: three quarters of
your stamina regeneration gone, for up to half a minute. An early run for barley is still your
call. It is just a raid now rather than an errand.

A biome counts as earned when **every member of your group has personally been at that boss's
death**, not when the boss has died in the world. Kill Moder yourself and the Plains stays shut
until the friend who was offline that night has killed it too.

---

## How it works

### Biome pressure

While you are standing in a gated biome:

- **Food burns 5× faster.** A 1600-second meal becomes a 320-second one. Your existing food
  is not deleted, it just runs out fast.
- **You cannot eat or drink.** Meals, mead and potions are all refused, with
  *"The land will not feed you here"* on screen. **Nothing is consumed or destroyed**. The
  item stays in your inventory.
- **Running buffs burn 5× faster,** and new ones are refused. Guardian powers are refused
  **without burning the cooldown**, so you can use yours the moment you leave.
- **Rested and Resting count as buffs,** so a fire and a roof buy you nothing inside. This is
  the harshest single rule in the mod and it has its own switch.
- **Harmful effects are never touched.** Wet, Cold, Freezing, Burning, Poison and the rest run
  exactly as normal. Speeding up Poison would be a mercy, not a penalty.

And on the way out:

- **Sapped** gives you 75% less stamina regeneration. One second in the biome banks one second
  of it, up to **30 seconds**, and it only spends itself once you are somewhere the land
  tolerates you. A dash in and out still costs you; living there is a flat tax rather than an
  escalating one. It stacks with food and Rested rather than replacing them, so a sapped player
  with good food still regenerates faster than a sapped player without, just badly.

You get a HUD icon while you are inside, a second one for Sapped, and a message on the way in
and out that names who the biome is still waiting on.

**The gate is the same for everybody.** It is one answer about the group, not a per-player one.
If the roster has not all cleared Moder, the Plains withers *you* too, even if you personally
landed the kill. That is the point rather than a side effect. See below.

Two things worth knowing. **Dungeons inherit the biome above them**, so a Swamp crypt withers
you exactly like the Swamp. And **a player without the mod installed is not gated at all**.
This is a rule for a group that all runs it, not an anti-cheat. If you want that enforced, see
[Installation](#installation).

### Group progression

- **Everyone on the roster has to have done the boss personally.** The world key the game sets
  when a boss dies is not enough on its own.
- **Credit is earned by being there.** When a boss dies, everyone within **100 m** of the body
  is credited. You do not have to land the killing blow, and you do not have to be the host.
- **Joining late does not undo anything.** Once the group has cleared a boss, that biome is
  open **permanently**. A friend arriving with a fresh character gates only what the group has
  *not* yet cleared.
- **A deadline opens the biome anyway.** Once the first person clears a boss, the rest of the
  group has a set number of days before it opens regardless: one day for Eikthyr, and one more
  for each boss after. This is what stops one person who vanishes holding a biome shut, and it
  is usually what unblocks you.
- **The roster forgets people who stop playing.** A character stops counting for a boss after
  **14 days** without logging in. This is what decides who "everyone" means, so a friend who
  visited for one evening, or an alt made once, cannot hold the gate forever. No admin command
  and no list to maintain. With the default deadlines above this rarely decides anything on its
  own, but it is the only backstop if you clear the deadlines or point a biome at a boss that
  has none.
- **Existing worlds and characters are handled.** Installing on a long-running save does not
  re-lock everything: a character is credited for a boss its own file says it attended, as long
  as that boss has **already died in this world**.

---

## At a glance

Every value here is the shipped default and every one is configurable.

| Mechanic | Default |
| --- | --- |
| Food drain in a gated biome | **5×** |
| Buff drain in a gated biome | **5×** |
| Eating and drinking | **refused** |
| New buffs | **refused** |
| Rested / Resting | **treated as buffs** |
| Stamina regeneration while Sapped | **25% of normal** (a 75% penalty) |
| Sapped banked per second inside | **1 second** |
| Sapped maximum | **30 seconds** |
| Boss credit radius | **100 m** |
| Roster absence before you stop counting | **14 real days** |
| Catch-up deadline | **1 day for Eikthyr, +1 per boss after** |
| Gate basis | **the whole group**, not the world |
| Already-earned biomes | **never re-lock** |

---

## Biome progression

The defaults are the vanilla progression offset by one: the boss of the previous biome opens
the next.

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

It is a table, not a hardcoded progression, so the interesting variations are all edits to it.
Blank a row and that biome is never gated. Point every row at one key and you have a
single-boss gate. Point a row at a key some other mod sets and it gates on that instead.

**The Black Forest row is the one to look at first.** Gating it on Eikthyr walls off the copper
run most people do before touching him. That is the intended shape, and it is also a real
change to the opening hour.

**Two key names could not be verified from the game's code.** `defeated_queen` and
`defeated_fader` are set from prefab data rather than named in Valheim's `GlobalKeys` enum. A
wrong key fails *closed*, which looks exactly like a working gate, so `LogGlobalKeys` is on by
default and prints what your world actually records. Check it once against a save where those
bosses are down.

---

## Installation

Single DLL, no asset bundle. Built for **BepInEx 5.4.23.3** on **net462**.

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   **5.4.2333**. It is the only required dependency.
2. Drop `Wither.dll` into `BepInEx/plugins/`.
3. Launch once. The config file is written to
   `BepInEx/config/ezomic.valheim.wither.cfg`.

**Everyone should install it, including the server.** The gate is enforced by each client on
itself, so a player without the plugin is not gated by it.

[Longhouse Core](https://github.com/Ezomic/valheim-core) is **optional**. Install it and the
server refuses a client that does not have Wither at a matching version, and the host's gate
settings are pushed to every client so nobody can disagree about the rules. Without Core, Wither
is fully functional (solo you need nothing else at all), but the gate becomes an agreement
between players rather than a rule of the server. Wither says so loudly, once, in the log if it
finds the group gate running in multiplayer with no Core.

---

## Why Wither exists

A particular kind of evening. Somebody has not killed The Elder yet and the Swamp trip is
already being planned. Somebody else has been up a Mountain and come back with onion seeds, so
the whole server is eating onion soup in the Black Forest.

Nobody cheated. Valheim's gates are made of damage, and damage is a soft gate: it can be
out-geared, out-run or simply out-healed, which is why the Plains stops being frightening about
ten minutes after it starts. A careful player with a decent shield walks through any of them
hours early, and the food, the gear and the map all come back with them.

What that does is not "harder" or "easier" in the abstract. It makes the game **easy early and
empty later**. The opening hours are trivialised by food nobody should have yet, and the biomes
those things came from have nothing left to give when the group finally arrives properly.
Progression stops being a sequence of places you earn and turns into a shopping list you can
run in any order.

The usual mod answer is the opposite extreme: a hard boss gate that refuses to let you across
the border at all. That fixes the pacing by deleting the thing worth having, which is the walk
into somewhere you should not be.

Wither sits between them. You can go anywhere, immediately, and nothing stops you at the edge.
The land just will not sustain you while you are there.

### Why three penalties and not one number

They do different jobs, which is why they are three parts:

- **The drain** sets the clock. It is the part you feel while things are going well.
- **The refusal** makes the clock real. Without it the drain is simply beaten by a bigger food
  pack, and the mod becomes an inventory tax rather than a time limit.
- **Sapped** makes leaving cost something. Without it the optimal play is to sprint in, grab,
  and sprint out at no cost, and a penalty you can dodge by being quick is a penalty for slow
  players only.

### Why the gate is on the group

Valheim groups come apart along progression. One person plays more, gets ahead, and the others
arrive to find the interesting content already cleared, so nobody wants to redo it, the person
ahead has no reason to go back, and what started as a group becomes several people playing
alone in the same world.

The group gate is aimed squarely at that. It makes helping the people behind you **the way you
move forward yourself**, not a favour you do them. If your friend has not killed The Elder, the
Swamp is shut for you as well, so going back to fight him again is not charity. It is the next
thing on your own list.

**It is a coordination device, not a punishment.** Nothing is taken away from anyone, and the
person who is ahead is not penalised for being ahead. They are simply given a reason to turn
around, which the base game never gives them.

Two rules keep that from curdling into a hostage situation, and they are part of the same idea:
**progress never regresses**, so a newcomer cannot revoke a biome the group already earned, and
**the catch-up deadline** means one person who stops logging in cannot hold a biome shut
indefinitely. The gate should make you fetch your friend, not trap you behind them.

---

## Multiplayer and persistence

- **Progress belongs to the world**, and is saved with it. A gate that forgot people the moment
  they logged off would not be a group gate at all.
- **Credit is per world, not per character.** A character that cleared a solo world does not
  arrive on your server pre-credited. Imported credit is only honoured for a boss this world
  has already seen die, so it can never open a biome on its own.
- **The roster maintains itself.** A character joins it the first time it spawns with the mod
  and drops out after 14 days of absence. Characters that predate the mod are not on it, so
  installing on a long-running world does not wither everyone on behalf of an alt nobody has
  touched since spring.
- **Nothing about your character is modified.** Vanilla's own record of which bosses you have
  attended is read, never written.
- **The mod is client-side in what it does to you.** Food timers and status effects belong to
  the owning client; nothing here reaches into another player's character.
- **A player without the mod is not gated.** With Longhouse Core installed the server can
  refuse them instead; without it, they simply are not covered.

---

## Configuration

`BepInEx/config/ezomic.valheim.wither.cfg`. Every entry has a comment in the file explaining
the reasoning, not just the units.

> **BepInEx writes this file on first run, and the saved value beats any new default in code.**
> If a change appears to do nothing, check the cfg before reading anything else.

With Longhouse Core installed, every setting that decides **a rule** is synced from the host:
all of **Gate** including the biome keys, the drains and blocks under **Food** and **Buffs**,
and both **Sapped** values. Anything that decides **wording** stays yours: the two blocked
messages, all of **Presentation**, and all of **Diagnostics**. The host sets the rules of the
gate; it does not get to pick your phrasing or your log level.

### Gate

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. Off leaves the game completely untouched. |
| `GateOnGroup` | `true` | Gate on whether everyone has personally done the boss. Off restores "one kill opens it for all". |
| `GateNeverRegresses` | `true` | Once the group clears a boss, that biome stays open forever. Off makes the gate strictly weakest-link at all times. |
| `RosterDays` | `14` | Real days a character counts for after it was last seen. |
| `RosterDaysPerBoss` | *(empty)* | Per-boss overrides, as `key:days` pairs. E.g. `defeated_eikthyr:7, defeated_fader:60` |
| `BackfillFromCharacter` | `true` | Credit a character from its own file, for a boss this world has already seen die. The migration path for existing worlds. |
| `CatchUpDays` | `0` | Fallback deadline for any boss not named below. `0` means none. |
| `CatchUpDaysPerBoss` | `eikthyr:1 … fader:7` | Days the group has to catch up once the first player clears a boss. |
| `CreditRadius` | `100` | Metres from a dying boss to be credited. |
| `ExcludePlayerIds` | *(empty)* | Character IDs that never count towards the gate. IDs, not names. |
| `Key_<Biome>` | see table above | The global key that opens each biome. Blank means never gated. |

### Food

| Setting | Default | What it does |
| --- | --- | --- |
| `FoodDrainMultiplier` | `5` | How much faster food burns. `1` disables the drain and leaves only the refusal. |
| `BlockEating` | `true` | Refuse to eat or drink anything at all. |
| `EatBlockedMessage` | `The land will not feed you here` | Shown when a bite is refused. |

### Buffs

| Setting | Default | What it does |
| --- | --- | --- |
| `BuffDrainMultiplier` | `5` | How much faster a running buff burns. |
| `BlockNewBuffs` | `true` | Refuse to apply any new buff. |
| `BlockRested` | `true` | Treat Rested and Resting as buffs. Off, a well-built camp becomes a real answer to the biome. |
| `AlsoBlock` | *(empty)* | Extra status effect names to treat as buffs. |
| `NeverBlock` | `Puke` | Names to leave alone even if the rules caught them. Wins over `AlsoBlock`. |
| `BuffBlockedMessage` | `The land turns your power aside` | Shown when a potion or guardian power is refused. |

### Sapped

| Setting | Default | What it does |
| --- | --- | --- |
| `StaminaRegenMultiplier` | `0.25` | Stamina regeneration while Sapped, as a fraction of normal. |
| `MaxSeconds` | `30` | Ceiling on how much Sapped you can bank. Also how long you must stand there to reach full penalty. |

### Presentation

| Setting | Default | What it does |
| --- | --- | --- |
| `ShowStatusEffects` | `true` | Show the two effects on the HUD. |
| `WitherIconFrom` | `Poison` | Vanilla effect whose icon the in-biome marker borrows. |
| `SappedIconFrom` | `Encumbered` | Vanilla effect whose icon Sapped borrows. |
| `EnterMessage` | `Something here refuses you` | Shown once on entering. Blank to say nothing. |
| `LeaveMessage` | `The land loosens its grip` | Shown once on leaving. |
| `NameTheBlockers` | `true` | Name the characters the biome is still waiting on. |
| `BlockedByPrefix` | `Still owed by:` | Prefix for that list. |

### Diagnostics

| Setting | Default | What it does |
| --- | --- | --- |
| `Verbose` | `false` | Log every gate transition and blocked effect. |
| `LogGlobalKeys` | `true` | Log the world's keys and the whole gate table on spawn. **Leave this on**. It is how you catch a wrong key name. |
| `LogBlockedEffects` | `false` | Log the full list of effects the mod decided are buffs. |

### What counts as a buff

Found rather than listed, because a list of potion names goes stale the first time the game
ships a new potion:

1. Anything an item applies when you consume it: every potion and every mead, read straight
   off `ObjectDB`.
2. Anything named `GP_*`, which is the guardian powers.
3. `Rested` and `Resting`, if `BlockRested` is on.

Everything else passes through untouched, and that is the important half. An allowlist of
*harmful* effects was written first and thrown out: it can only ever be as complete as the day
it was written, and every name missing from it hands the player an immunity in the biome that is
supposed to be killing them.

---

## Technical notes

Everything below is for maintainers. You do not need any of it to play.

### Valheim records boss attendance, but not the way it looks

In `Character.OnDeath`:

```csharp
if (!string.IsNullOrEmpty(m_defeatSetGlobalKey))
    Player.m_addUniqueKeyQueue.Add(m_defeatSetGlobalKey);

if ((bool)m_nview && !m_nview.IsOwner())
    return;                                    // ← the early-out comes AFTER
```

That push sits *above* the ownership early-return, which reads as "every client that had the
boss loaded is credited". **That reading is wrong, and it cost two bugs.** The key does land in
`Player.m_uniques`, which `Player.Save` writes and `Player.Load` reads, so existing characters
do carry their past attendance. But how it gets there, and for whom, are both different from
how they look.

**It lands later than it looks.** `m_addUniqueKeyQueue` is a *static* list, drained only by
`AddQueuedKeys`, called from exactly two places, `Player.Start` and `SetLocalPlayer`, both
spawn-time. So `m_uniques` does not contain a boss you killed this session until you next spawn,
and quitting to desktop before respawning loses the credit with the process. Found by playing:
Eikthyr died, the world key was set, nothing was recorded.

**And it lands for one player, not all of them.** That `!m_nview.IsOwner()` guard is
unreachable. `CheckDeath` is `OnDeath`'s only caller, and `CheckDeath` is called from exactly
one place, inside `if (zDO.IsOwner())` in `Character.CustomFixedUpdate`. `OnDeath` runs on the
owning client and nowhere else. Crediting "the local player" from it would credit precisely one
member of a group that killed a boss together, and the gate would then stay shut forever while
looking exactly like it was working.

So Wither hooks `Character.OnDeath` and the owning client credits **everyone within
`CreditRadius` of the corpse**, on the group's behalf. It can: global keys are world state,
writable by anyone, and `Player.GetPlayersInRange` sees every player instantiated on that client,
which at a boss fight is all of them. The radius is generous because the two failure modes
are not symmetric: crediting a bystander costs one person's sense of having earned it, while
missing a genuine participant holds the gate shut for the whole group with no remedy short of
killing the boss again.

`m_uniques` is still read on spawn, because it is the only place credit earned *before* this
mod lives. It is the backfill, not the live path.

### The key layout

`m_uniques` is local and nothing replicates it, so each client republishes its own record into
the world's **global keys**, which are broadcast to every client on connect and saved with the
world.

| Key | Meaning |
| --- | --- |
| `wither_seen_<characterId>` | Heartbeat. Value is `<day>\|<name>`: the day last seen, plus the name for the "waiting on" message. |
| `wither_p_<characterId>_<bosskey>` | This character was present when that boss died. |
| `wither_open_<bosskey>` | The group has cleared this boss. Latched once, never removed. |
| `wither_first_<bosskey>` | Unix seconds when the catch-up clock started. Written once, never moved. |

Writes always check first. `RPC_SetGlobalKey` ends in `SendGlobalKeys(Everybody)`, so accepting
one key rebroadcasts the world's entire key list to every player. A publisher that did not
check would push a full broadcast on every call. The heartbeat therefore carries a whole **day
number** rather than a timestamp, for a second reason too: every global key write also does
`m_knownWorldKeys.IncrementOrSet(key + " " + value)` into the saved player profile, so a value
that changed every beat would grow that dictionary forever, in everyone's character file.

Days are real days, UTC, not world time. Valheim's clock only advances while somebody is
playing, which would make a 14-day window unmeasurable.

### Progress does not regress

Once the whole roster has cleared a boss, `wither_open_<bosskey>` is latched and the question is
never asked again. Without it the gate runs backwards: a friend joining with a fresh character
decides nobody has done Eikthyr and shuts the Black Forest *for the people who killed him*.

The latch is only written when the roster is non-empty and every counted member has the boss.
Latching off the empty-roster fallback would let one client's loading screen open a biome
forever. **A deadline that expires latches too**, or a biome opened by the deadline
would silently shut again if `CatchUpDays` were later lowered.

It is written from the publish tick rather than from the read path, because `GroupHasKey` is
asked several times a frame and a read should not write to the world.

### Credit is per world, not per character

`m_uniques` is world-agnostic. It means "this character was present at an Eikthyr death",
anywhere, ever. Taken at face value that is a hole straight through the gate: clear a solo
world, bring the character to the server, arrive pre-credited for bosses nobody here has fought.

So the backfill is only trusted for a boss **this world has already seen die**. If the boss is
still alive here, imported credit is refused. If it is dead, the world has demonstrably
progressed past it, and crediting a character who was probably one of the people who did it is
both harmless and the only way an existing server gets backfilled at all. Kills witnessed here
are unaffected. The `OnDeath` hook always credits, because it saw it happen.

### Things that were nearly bugs

Each took reading the game to find and would have looked like a design choice from the outside:

- **`Player.ConsumeItem` removes the item whether or not `EatFood` succeeded.** Refusing food
  in `EatFood`, the obvious seam and the one with the right name, would have destroyed every
  meal and potion it refused. The block lives in `CanConsumeItem`, which is the gate that path
  actually respects and where vanilla puts its own refusal.
- **`SEMan` refreshes a running effect without going through `AddStatusEffect`.**
  `Internal_AddStatusEffect` calls `ResetTime` in place and returns. Sitting by a fire refreshes
  `Rested` through exactly that path, so patching only the public overload would have topped it
  back up faster than the drain could take it down.
- **`StartGuardianPower` sets the cooldown before it applies the effect.** Blocking only the
  status effect would have burned a twenty-minute power on nothing.
- **`Puke` is applied by an item on consume**, so the "anything a consumable grants" rule swept
  it up as a buff, found on the first run in a real world. Blocking a debuff would have made a
  gated biome the one place bad food cannot hurt you, and the drain would have made it wear off
  *faster* there. It ships in `NeverBlock`.
- **The gate is asked several times a frame**: by the tick, by both status effects, and by
  every consume. Building the roster walks every global key in the world, so it is cached for
  two seconds, and the list of names owed is built only on a transition.
- **`SEMan.GetHUDStatusEffects` skips any effect whose icon is null**, so a custom effect with
  no icon works and is invisible. Both borrow a vanilla sprite.

### Dungeons, for free

Crypt and mine interiors are generated at `y > 3000`, which sounds like somewhere with no
terrain and therefore no biome. But `Heightmap.FindBiome` resolves through `IsPointInside`, and
that compares **only X and Z**. The interior sits directly above its own entrance, in a zone
whose heightmap is loaded because you are standing in it, so it reports the surface biome. A
Swamp crypt withers you exactly like the Swamp. Convenient, and entirely accidental.

### Where the drain rides

`Player.UpdateFood`, not `Update` or a MonoBehaviour of its own. It is where the food timers
live, it is called from `UpdateStats` which already refuses to run during the intro and
mid-teleport (two windows where starving someone would be a bug), and it arrives with the `dt`
the rest of the game is using, so there is no second clock to keep in step.

Only `m_time` is touched, never the health and stamina values derived from it. Vanilla
recomputes those once a second and removes anything expired, so pushing the timer past zero is
enough and the "your food is depleted" message still comes from the game.

Sapped charges *backwards*: Valheim tracks a status effect by elapsed time, so `m_time` counts
up and `IsDone` fires when it passes `m_ttl`. Charging means pushing `m_time` down, and the
effect is set up full so it arrives empty.

---

## Status

**Played, not merely built.** On a local world and on a real dedicated server: refused meals and
potions keep their items, both HUD icons render, food and buff timers visibly burn at 5×, Sapped
accumulates and follows you out, a guardian power is refused without burning its cooldown, gates
open and close at borders in both directions, credit is granted at the kill and survives a world
reload, the latch fires, a two-character roster names both debtors, and the catch-up deadline
opens a biome for a group that had not all earned it. Running standalone with no Core has been
confirmed in game.

**What has not been tested, and only this:**

- **Attendee credit with more than one player.** Solo you own the boss ZDO and credit yourself
  either way. The loop is identical for one player or five; what is unproven is whether other
  players' objects are instantiated on the owning client at fight range.
- Whether `defeated_queen` and `defeated_fader` are the real key names. See
  [Biome progression](#biome-progression).

---

MIT licensed. Part of a suite of mods written to be played with rather than published from;
[Longhouse Core](https://github.com/Ezomic/valheim-core) is the optional shared library.

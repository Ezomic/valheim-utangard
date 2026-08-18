# Utangard design notes

Why it works the way it does, and how it is built. None of this is needed to play; for that
see the [README](README.md).

## Why three penalties and not one number

They do different jobs, which is why they are three parts:

- **The drain** sets the clock. It is the part you feel while things are going well.
- **The refusal** makes the clock real. Without it the drain is simply beaten by a bigger food
  pack, and the mod becomes an inventory tax rather than a time limit.
- **Sapped** makes leaving cost something. Without it the optimal play is to sprint in, grab,
  and sprint out at no cost, and a penalty you can dodge by being quick is a penalty for slow
  players only.

## Why the gate is on the group

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

## What counts as a buff

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

So Utangard hooks `Character.OnDeath` and the owning client credits **everyone within
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
| `utangard_seen_<characterId>` | Heartbeat. Value is `<day>\|<name>`: the day last seen, plus the name for the "waiting on" message. |
| `utangard_p_<characterId>_<bosskey>` | This character was present when that boss died. |
| `utangard_open_<bosskey>` | The group has cleared this boss. Latched once, never removed. |
| `utangard_first_<bosskey>` | Unix seconds when the catch-up clock started. Written once, never moved. |

Writes always check first. `RPC_SetGlobalKey` ends in `SendGlobalKeys(Everybody)`, so accepting
one key rebroadcasts the world's entire key list to every player. A publisher that did not
check would push a full broadcast on every call. The heartbeat therefore carries a whole **day
number** rather than a timestamp, for a second reason too: every global key write also does
`m_knownWorldKeys.IncrementOrSet(key + " " + value)` into the saved player profile, so a value
that changed every beat would grow that dictionary forever, in everyone's character file.

Days are real days, UTC, not world time. Valheim's clock only advances while somebody is
playing, which would make a 14-day window unmeasurable.

### Progress does not regress

Once the whole roster has cleared a boss, `utangard_open_<bosskey>` is latched and the question is
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

### The border is a band

`Heightmap.FindBiome` at eight compass points, five metres out, and the gate closes if any of
them lands in a biome the group has not earned. The player's own biome is still the first
question asked, and the ring is only consulted when the ground underfoot is allowed - inside a
gated biome it can only ever agree, and it costs eight heightmap lookups to say so.

Vanilla's own answer, `Player.m_currentBiome`, is no use here: it is refreshed once a second
from the player's own position, and there is no per-point equivalent. `FindBiome` compares only
X and Z, so the dungeon case looks after itself exactly as the main gate does.

What is cached is the set of biomes within reach, and deliberately not the verdict on them. A
verdict goes stale the moment a boss dies or a deadline passes, and the player it would go
stale for is the one standing still at a border - which is precisely who is watching when a
biome opens. Which biomes are within five metres, by contrast, cannot change while the player
does not move, so the cache is keyed on position and re-taken every quarter of a metre.

Eight points and no interior ring, because a biome border is a smooth curve tens of metres
across at its sharpest: at five metres the arc between neighbouring samples is under four
metres, and a border that touches the ring at all crosses one of them.

### Why healing is an effect and not a patch

Everything else the mod does to a player is an edit to a timer or an answer, and lives in a
patch. Health regeneration is different in kind, because vanilla already asks for it:
`Player.UpdateFood` totals every meal's `m_foodRegen`, calls `SEMan.ModifyHealthRegen` on the
result, and heals by what comes back. So the marker effect overrides `ModifyHealthRegen` and
the multiplier composes with every other one rather than replacing them - the same argument as
Sapped's stamina figure.

The cost was that the marker stopped being optional. It was signage, and signage that nobody
can see does not need to exist, so it was skipped entirely when `ShowStatusEffects` was off.
Carrying a rule, that would have made a presentation toggle silently disable a mechanic. Both
effects are now always applied and hidden by handing them a null icon, which is what
`SEMan.GetHUDStatusEffects` already checks for.

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

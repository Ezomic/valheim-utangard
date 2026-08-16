# Changelog

Notable changes to Wither. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [1.1.0] — 2026-08-16

### Core is now optional

Wither installs and runs on its own. Core is a **soft** dependency: present, it is used exactly
as before; absent, the mod is fully functional without it.

Nothing about the gameplay needed Core. The drain, the refusal and Sapped are local patches,
and the group gate travels over vanilla global keys, which every client replicates already.
Singleplayer is unaffected in every respect.

What Core buys is **enforcement**, and that is the whole of what standalone gives up. Core is
what refuses a client that does not have Wither; without it, a player who skips the mod is not
gated at all and walks into the Ashlands on day one while everyone else waits on the roster.
The gate becomes an agreement between players rather than a rule of the server.

That is a real trade and it belongs to whoever runs the server, so the mod logs it rather than
refusing to run — and it says so **loudly**, once, on spawn, when it finds the group gate
enabled in a multiplayer session with no Core. That combination is the one that looks like it
is working and is not, and failing silently there is the worst of the options.

Mechanically: `[BepInDependency]` is now `SoftDependency`, every `Suite` call sits behind a
`Chainloader.PluginInfos` check inside a `[MethodImpl(MethodImplOptions.NoInlining)]` method,
and the project reference to Core is compile-time only. The no-inlining is load-bearing rather
than decorative — the JIT resolves the assemblies a method needs when it first compiles that
method, so a `Suite` call sitting directly in `Awake` would drag `Ezomic.Core` in *before* the
check could prevent it, and the missing-assembly exception would land during plugin load.

Core is no longer listed in `manifest.json`. Installing Wither from Thunderstore no longer
installs Core with it; suite users install Core themselves, exactly as before.

## [1.0.0] — 2026-08-16

First release. Played, not merely built.

### The group gate, finished

0.2.0 shipped the idea; this is the version where it holds up.

- **Credit is earned at the kill, by everyone present.** The owning client credits every
  player within `CreditRadius` (100 m) of the corpse. It had to be done that way:
  `Character.OnDeath` looks like it runs on every client that had the boss loaded — it pushes
  vanilla's key above an `IsOwner` early-return — but that guard is unreachable, because
  `CheckDeath` is its only caller and sits inside `if (zDO.IsOwner())`. Crediting "the local
  player" would have credited exactly one member of a group that killed a boss together, and
  the gate would then have jammed shut while looking like it worked.
- **Credit is per world.** A character that cleared a solo world no longer arrives
  pre-credited. `BackfillFromCharacter` still allows the migration case, and only for a boss
  this world has already seen die.
- **Progress never regresses.** Once the group clears a boss the biome latches open, so a
  newcomer gates only what has *not* been cleared rather than revoking what has.
- **A catch-up deadline**, defaulting to a ladder of one day for Eikthyr and one more per
  boss after. Without it a single person who stops logging in holds a biome shut for everyone
  until `RosterDays` finally drops them. A biome the deadline opens latches too.
- **Per-boss roster windows** via `RosterDaysPerBoss`, for when one boss deserves a shorter
  leash than another.
- **The blocker line names other people, never you**, and shows how long is left.

### Fixed

- A refused meal or potion is no longer destroyed. `Player.ConsumeItem` removes the item
  regardless of what `EatFood` returns, so the refusal had to move to `CanConsumeItem` — the
  gate that path actually respects.
- Refusing a guardian power no longer burns its cooldown; `StartGuardianPower` sets the
  cooldown before applying the effect.
- `Rested` can no longer be topped up past the drain. `SEMan` refreshes a running effect
  through `Internal_AddStatusEffect` without ever reaching the public overload.
- `Puke` is no longer treated as a buff. An item applies it on consume, so the potion rule
  swept up a debuff — which would have made a gated biome the one place bad food cannot hurt
  you.

### Played, not merely built

On a local world and on a real dedicated server: refused meals and potions keep their items,
both HUD icons render, food and buff timers burn at 5×, Sapped accumulates and follows you out
and cripples stamina regeneration, a guardian power is refused without burning its cooldown,
gates open and close at borders, credit is granted at the kill and survives a reload, the latch
fires, a two-character roster names both debtors, and the catch-up deadline opens a biome for a
group that had not all earned it. No exceptions in a long session.

### Known limits

- **Attendee credit has never run with more than one player.** Solo, you own the boss and
  credit yourself either way, and two characters taken in turns only credits whoever is logged
  in. The loop is the same for one player or five; what is unproven is whether other players'
  objects are instantiated on the owner's client at fight range.
- `defeated_queen` and `defeated_fader` are taken from prefab data rather than the game's
  `GlobalKeys` enum. A wrong key fails *closed*, which is indistinguishable from a working
  gate — `LogGlobalKeys` prints what your world actually has.

## [0.2.0] — 2026-08-15

Written and building. **Never run in game.**

### The line this sits on

> **A biome you have not earned will not feed you. It never stops you walking in.**

Valheim gates its biomes with damage, which is a soft gate — out-geared, out-run or
out-healed, which is why the Plains stops being frightening ten minutes after it starts. The
usual mod answer is a hard boss gate that refuses to let you across the border, which fixes
the pacing by deleting the thing worth having: the walk into somewhere you should not be.

Wither sits between them. You can go anywhere, immediately, and nothing stops you at the
edge. The land just will not sustain you while you are there.

### The three parts

Three parts rather than one number, because they do different jobs:

- **The drain.** Food and buffs burn down five times faster in an unearned biome. This sets
  the clock, and it is the part you feel while things are going well.
- **The refusal.** You cannot eat or drink anything at all while you are there. Without it
  the drain is simply beaten by a bigger pack, and the mod becomes an inventory tax rather
  than a time limit.
- **Sapped.** Seventy-five percent less stamina regeneration, one second per second spent in
  the biome up to thirty, and it keeps ticking after you leave. Without it the optimal play
  is to sprint in, grab and sprint out at no cost — and a penalty you can dodge by being
  quick is a penalty for slow players only.

### The gate is on the group, not the world

By default a biome opens when **every member of the roster** has personally killed the boss,
not when the boss has died in this world. Kill Moder yourself and the Plains stays shut until
the friend who was offline that night has killed it too.

This rides on `Character.OnDeath` pushing `m_defeatSetGlobalKey` into
`Player.m_addUniqueKeyQueue`, which is how the game records a boss kill against a *character*
rather than a world.

### Known limits

- **Never played.** None of the three parts has been felt in a session, and the numbers are
  therefore first guesses rather than tuned values.
- The per-character kill record is only refreshed when a player loads in, so a boss killed
  during the current session is not visible until then unless the kill itself is hooked.

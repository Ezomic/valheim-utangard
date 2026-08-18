# Changelog

Notable changes to Utangard. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [Unreleased]

### The border is a band, and wounds do not close

Two rules, both configurable, both on by default.

**`Gate.BorderMargin`, 5 metres.** The gate now reaches five metres past the edge of a gated
biome. On a line, every penalty in the mod is escapable by taking three steps out of the Swamp,
eating, and stepping back in - the drain, the refusal and the grudge all end at a boundary you
can see and stand behind. That makes it a rule about where you may chew rather than where you
may live, and it is worst exactly where it matters most, at the edge of a fight you are already
in. A band has to be genuinely cleared. Set it to 0 to put the gate back on the border.

It samples eight compass points at the margin, so it costs eight biome lookups. Those are
cached against the player's position and re-taken every quarter of a metre walked; what is
cached is *which* biomes are within reach and never the verdict on them, so a biome that opens
while somebody stands at its border opens for them where they stand.

**`Food.HealthRegenMultiplier`, 0.** Health regeneration in a gated biome, as a fraction of
normal. It sits in the Food section because food is the only passive healing Valheim has -
`Player.UpdateFood` adds up every meal's `m_foodRegen` every ten seconds and heals you by it -
so this multiplies exactly the healing the food you are not allowed to eat would have given.
The land that will not feed you does not mend you either.

It rides `StatusEffect.ModifyHealthRegen` on the marker effect rather than a patch, because
that is the seam vanilla already offers and it composes with every other multiplier instead of
overriding them. Which meant the marker had to stop being skipped when `ShowStatusEffects` was
off: it was pure signage then and is carrying a rule now, and turning off the HUD would
otherwise have quietly turned off the healing block.

Both are host-synced with Core, like every other setting that decides a rule.

Also: the deadline in the entry message is now read from the biome that is actually withering
you rather than the one underfoot. With a margin those part company, and a countdown for the
wrong boss is worse than no countdown.

### You can see the gate, and you are told when it opens

**A Utangard page in the compendium**, beside Logs and Active Effects: every biome, whether it
is open, who still owes it, and how long until the deadline opens it anyway. Until now that
report existed only as log lines on spawn, which is the wrong medium for the person who most
needs it - somebody mid-raid wondering why their food vanished is not going to read
`LogOutput.log`.

It is a postfix on `TextsDialog.UpdateTextsList`, so it is vanilla's list with vanilla's skin,
font, scrolling, gamepad handling and close behaviour, none of which this mod then owns. The
alternative was an IMGUI window: four patches (both `TakeInput` overloads,
`PlayerController.InInventoryEtc`, `GameCamera.UpdateMouseCapture`) and a keybind, to arrive at
something that looks like a different game.

The log and the page are one function now. They were about to be two copies of "is this biome
open, and if not who owes it", and the interesting part is not the wording but the three-way
distinction between open-because-latched, open-because-everyone-has-it and shut-with-an-empty-
roster. Two copies of that stay right for about a week.

**`Presentation.AnnounceOpenings`.** A message when a biome opens, wherever you are. The mod's
whole argument is that fetching the friend who is behind is worth doing, and the payoff for
doing it used to land silently - you found out by walking to the Mountain and not being
refused. It watches the answer rather than the kill, so a catch-up deadline expiring and a
roster member ageing out announce themselves too, and it needs no network code at all: global
keys are already broadcast to every client.

### The gate keys are checked against the game, not assumed

`defeated_queen` and `defeated_fader` are set from prefab data rather than named in the
`GlobalKeys` enum, so they were the two shipped defaults that could not be verified from the
game's code - and a wrong key fails *closed*, which looks exactly like a working gate.

`Character.m_defeatSetGlobalKey` is a public string on every creature prefab and `OnDeath`
hands it straight to `SetGlobalKey`, so walking ZNetScene's prefab list gives the complete list
of keys any death in this world can set, another mod's creatures included. On spawn Utangard
now warns about any gate row naming a key nothing here sets, and prints the ones that exist -
which is the answer to the question the warning provokes. `Diagnostics.LogDefeatKeys` prints
the whole map.

It checks and never corrects. A row pointed at another mod's key, or at a key a location sets,
is a supported thing to want.

**It has now been run**, and both names are right. The scan on a live world reported
`defeated_eikthyr`, `defeated_gdking`, `defeated_bonemass`, `defeated_dragon`,
`defeated_goblinking`, `defeated_queen`, `defeated_fader`, and also `defeated_hive` and
`defeated_serpent` for the two creatures that set a key without gating anything here.

### Played

All of the above except one path: the border margin refusing a player standing three metres
outside a gated biome, the healing block, the compendium page, and the announcement firing on
the transition. Still unplayed: the healing block with `ShowStatusEffects = false`, which is
the one path the marker's new job could be wrong on.

## [1.1.0] - 2026-08-17

### An API for other mods to ask what the group has earned

`UtangardApi.GroupHasKey(bossKey)` answers the one question this mod knows and nothing else
does: whether the group has *earned* a boss, rather than whether the world has merely seen it
die. Those two answers part company the moment somebody is offline for a kill.

It exists because Hoard scales stack sizes by world progression. Reading the raw `defeated_`
key there would hand out Plains-era stacks for a biome still fenced off here, which is two mods
disagreeing out loud about the same word in a way that reads as a bug in whichever one the
player happens to be looking at.

A facade rather than making `Progression` public: the roster, the latch and the deadline are
nobody else's business. Read-only by construction, so a consumer cannot open a biome by asking
about it.

### The README is half the length

The source-code archaeology moved to `DESIGN.md` - why `Character.OnDeath` credits one player
rather than all of them, what the global keys are called and why, and the handful of things
that were nearly bugs. None of it is needed to play, and it was sitting between a new reader
and the part that says what the mod does.

Nothing about the gameplay changed in this release.

## [1.0.0] - 2026-08-16

First release. Played, not merely built.

### Core is optional

Utangard installs and runs on its own. Core is a **soft** dependency: present, it is used exactly
as before; absent, the mod is fully functional without it.

Nothing about the gameplay needed Core. The drain, the refusal and Sapped are local patches,
and the group gate travels over vanilla global keys, which every client replicates already.
Singleplayer is unaffected in every respect.

What Core buys is **enforcement**, and that is the whole of what standalone gives up. Core is
what refuses a client that does not have Utangard; without it, a player who skips the mod is not
gated at all and walks into the Ashlands on day one while everyone else waits on the roster.
The gate becomes an agreement between players rather than a rule of the server.

That is a real trade and it belongs to whoever runs the server, so the mod logs it rather than
refusing to run, and it says so **loudly**, once, on spawn, when it finds the group gate
enabled in a multiplayer session with no Core. That combination is the one that looks like it
is working and is not, and failing silently there is the worst of the options.

Mechanically: `[BepInDependency]` is `SoftDependency`, every `Suite` call sits behind a
`Chainloader.PluginInfos` check inside a `[MethodImpl(MethodImplOptions.NoInlining)]` method,
and the project reference to Core is compile-time only. The no-inlining is load-bearing rather
than decorative. The JIT resolves the assemblies a method needs when it first compiles that
method, so a `Suite` call sitting directly in `Awake` would drag `Ezomic.Core` in *before* the
check could prevent it, and the missing-assembly exception would land during plugin load.

Core is not listed in `manifest.json`, so installing Utangard does not install Core with it.
Confirmed in game: Utangard loads alone, logs that it is running standalone, and the whole gate
works without Core present.

### The group gate, finished

0.2.0 shipped the idea; this is the version where it holds up.

- **Credit is earned at the kill, by everyone present.** The owning client credits every
  player within `CreditRadius` (100 m) of the corpse. It had to be done that way:
  `Character.OnDeath` looks like it runs on every client that had the boss loaded, since it
  pushes vanilla's key above an `IsOwner` early-return. But that guard is unreachable, because
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
  regardless of what `EatFood` returns, so the refusal had to move to `CanConsumeItem`, the
  gate that path actually respects.
- Refusing a guardian power no longer burns its cooldown; `StartGuardianPower` sets the
  cooldown before applying the effect.
- `Rested` can no longer be topped up past the drain. `SEMan` refreshes a running effect
  through `Internal_AddStatusEffect` without ever reaching the public overload.
- `Puke` is no longer treated as a buff. An item applies it on consume, so the potion rule
  swept up a debuff, which would have made a gated biome the one place bad food cannot hurt
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
  gate. `LogGlobalKeys` prints what your world actually has.

## [0.2.0] - 2026-08-15

Written and building. **Never run in game.**

### The line this sits on

> **A biome you have not earned will not feed you. It never stops you walking in.**

Valheim gates its biomes with damage, which is a soft gate: out-geared, out-run or
out-healed, which is why the Plains stops being frightening ten minutes after it starts. The
usual mod answer is a hard boss gate that refuses to let you across the border, which fixes
the pacing by deleting the thing worth having: the walk into somewhere you should not be.

Utangard sits between them. You can go anywhere, immediately, and nothing stops you at the
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
  is to sprint in, grab and sprint out at no cost, and a penalty you can dodge by being
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

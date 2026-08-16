# Changelog

Notable changes to Wither. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

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

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Utangard
{
    /// <summary>
    /// A character's own foothold in a biome the group has not earned yet. LHM-26.
    ///
    /// Why it exists. Pidgey's report from Longhouse was that a locked biome was a wall rather
    /// than a challenge, and he put the reason in one line: the inability to eat is the problem.
    /// Without food nobody could stay long enough to do anything there, so the rules that were
    /// meant to make a locked biome hard made it pointless instead. Robbin's answer, settled
    /// 2026-09-24: per character and per locked biome, two bars that earn back what the lock
    /// takes, "earned in the biome where the buff is active".
    ///
    ///   Fighting, from kills of that biome's creatures. Full at FightingFullPoints, 150 by
    ///   default since 2026-09-27 ("for fighting make the max points needed 150"; it was 100).
    ///   Eating comes back at half of it, and one kind of creature may put in half at most.
    ///   Discovery, 0 to 100%, from that biome's map uncovered on your own feet (see Discovery).
    ///   Healing comes back at normal speed when both are full.
    ///
    /// Everything else about a locked biome stays: meads, powers and Rested are still refused,
    /// food and buffs still burn faster, and you still leave Sapped. The group gate is not
    /// touched. A full foothold changes what the land does to you, never whether it is open.
    ///
    /// <b>Only in that biome.</b> The bars are asked about the biomes whose rules are being
    /// applied right now - the locked biome underfoot, and every one whose border margin you are
    /// standing in, which is BiomeGate.RulingBiomes - so a foothold in the Swamp buys nothing in
    /// the Mountains. Where two locked biomes meet, an unlock needs both: otherwise a foothold
    /// one metre over a line would be a better place to eat than an open biome, which the margin
    /// makes you stand five metres clear of.
    ///
    /// <b>Each client decides for itself.</b> Both bars read the local character's own data on
    /// the local client: the kill tally Utangard keeps in the character (KillTally), and the
    /// explored map in the local Minimap. The two things they unlock, the eating refusal and the
    /// healing multiplier, already run only on the client that owns the character. The one thing
    /// that crosses the network is a kill: it is seen on whichever machine owned the creature,
    /// and that machine tells each player who helped. The same caveat as the rest of the mod
    /// applies: this is a rule for a group running the same plugins, not an anti-cheat.
    ///
    /// <b>A failed read keeps the lock as it was.</b> Every question here answers "not earned"
    /// when it cannot answer at all, so a game update that moves the kill tally or the map costs
    /// the relief, never the mod and never a player's life to an exception. That is the opposite
    /// direction from Seams.PenaltyIsEscapable on purpose: there, failing closed would make a
    /// biome shut for good; here, failing closed only means the lock behaves as it did before
    /// LHM-26.
    /// </summary>
    internal static class Foothold
    {
        /// <summary>
        /// Fighting points that make a full bar: FightingFullPoints, never below 1, so a percent
        /// can always be taken of it. It was a constant 100 until Robbin raised it to 150 on
        /// 2026-09-27, which is when eating and the per-kind limit became percents of it (see
        /// UtangardConfig.EatAtFightingPercent).
        /// </summary>
        internal static int FullBar()
        {
            return Math.Max(1, UtangardConfig.FightingFullPoints.Value);
        }

        /// <summary>
        /// A bar's points as the percent every screen shows: the panel, the console and the line
        /// under a refused meal. Rounded down, so nothing says 50% a point short of eating.
        /// </summary>
        internal static int Percent(int points, int full)
        {
            if (full <= 0) return 100;
            return (int)Math.Min(100L, Math.Max(0L, (long)points * 100L / full));
        }

        /// <summary>
        /// Where eating comes back, in points: the fewest points whose percent, rounded down as
        /// Percent rounds it, reaches EatAtFightingPercent. So "Fighting here 49%" is always a
        /// refusal and "50%" never is. Above 100% it is more than a full bar, which is never.
        /// </summary>
        internal static int EatAtPoints(int full)
        {
            long points = ((long)full * EatAtPercent() + 99L) / 100L;
            return (int)Math.Min(int.MaxValue, points);
        }

        /// <summary>
        /// The most one kind may put in, in points: MaxFromOneKindPercent of a full bar, rounded
        /// down, so "at most half" of an odd bar stays at most half.
        /// </summary>
        internal static int KindCap(int full)
        {
            long points = (long)full * Math.Max(0, UtangardConfig.MaxFromOneKindPercent.Value) / 100L;
            return (int)Math.Min(int.MaxValue, points);
        }

        /// <summary>EatAtFightingPercent, never below 0.</summary>
        internal static int EatAtPercent()
        {
            return Math.Max(0, UtangardConfig.EatAtFightingPercent.Value);
        }

        /// <summary>
        /// What a kill is worth, per biome, as they were first written.
        ///
        /// These are now only the config defaults: the live tables are the Foothold.Points_ lines,
        /// read through UtangardConfig.PointsLineFor. They were plain strings from the start so they
        /// could become config lines with no change of shape, and that is what happened.
        ///
        /// Hand-set by Robbin on 2026-09-24, biome by biome, after a first draft that paid each kill
        /// its creature's health: that made the bar a sum of hit points, and he wanted the numbers to
        /// say how much a creature matters rather than how long it takes to fall. So each biome's
        /// common creature is worth 1 and its big threat 5, a full bar is the same size whatever the
        /// biome (100 then, 150 since 2026-09-27), eating comes back at half of it, and no one kind
        /// of creature may supply more than half of it.
        ///
        /// A creature missing from a biome's line is worth nothing there, and that is how prey and
        /// the harmless are kept out - deer in the Black Forest, bats in the Mountains, hares and
        /// seed-sized seeker brood in the Mistlands, seals in the Deep North. Swamp skeletons are left
        /// out on purpose too. Listing by name rather than by spawn table is also what lets the cave
        /// and camp creatures count: ulvs and cultists in a frost cave, surtlings at a fire geyser,
        /// growths in a tar pit, a charred warlock in a fortress. None of those are in any spawn list.
        ///
        /// Keyed by prefab name here because that is what a person can read and type. A kill is
        /// filed under the creature's display token, m_name, in the game's records and in
        /// Utangard's own tally alike, so the name is resolved to a token at runtime; two prefabs
        /// that share one token cannot be scored apart, which is what `utangard creatures` checks.
        ///
        /// <b>A shared token counts only in the earliest biome that lists it.</b> Measured with
        /// `utangard creatures` on 2026-09-24: eight tokens are shared between prefabs. The frozen
        /// greydwarf, frozen skeleton and frozen shaman of the Deep North are filed under the Black
        /// Forest ones' names, Swamp skeletons under the Black Forest skeleton's, and the Ashlands
        /// dvergr under the Mistlands rogue's - so as first written, a player could have filled the
        /// Deep North bar by killing greydwarves at home. Robbin's rule, the same day: the earliest
        /// biome in progression order keeps the name and every later biome loses it. What leaks after
        /// that only ever leaks into a biome that is already open by the time the later creature can
        /// be reached, because the gate opens biomes in order. Those entries are gone from the lines
        /// below, and Resolve() enforces the rule anyway, so a later edit cannot bring a leak back.
        ///
        /// The three Mistlands dvergr mages are one token, $enemy_dvergr_mage, so they cannot be
        /// scored apart either: support, fire and ice are all 3, Robbin's call.
        /// </summary>
        internal static readonly KeyValuePair<Heightmap.Biome, string>[] Defaults =
        {
            Pair(Heightmap.Biome.BlackForest,
                "Greydwarf:1, Skeleton:1, Greydwarf_Shaman:2, Greydwarf_Elite:3, Bjorn:4, Troll:5"),
            Pair(Heightmap.Biome.Swamp,
                "Draugr:1, Blob:1, Leech:1, Surtling:1, BlobElite:2, Wraith:2, Draugr_Elite:3, Writhan:3, Abomination:5"),
            Pair(Heightmap.Biome.Mountain,
                "Wolf:1, Ulv:1, Hatchling:2, Fenring_Cultist:2, Fenring:3, StoneGolem:5"),
            Pair(Heightmap.Biome.Plains,
                "Deathsquito:1, Goblin:1, BlobTar:1, GoblinShaman:2, Lox:3, GoblinBrute:4, Unbjorn:5"),
            Pair(Heightmap.Biome.Mistlands,
                "Seeker:1, Tick:1, Dverger:2, DvergerMageSupport:3, DvergerMageFire:3, DvergerMageIce:3, SeekerBrute:4, Gjall:5"),
            Pair(Heightmap.Biome.AshLands,
                "Charred_Archer:1, Charred_Twitcher:1, Volture:1, BlobLava:1, Charred_Melee:2, Asksvin:2, "
                + "Charred_Mage:3, BonemawSerpent:4, FallenValkyrie:5, Morgen:5, Morgen_NonSleeping:5, Charred_Melee_Dyrnwyn:5"),
            Pair(Heightmap.Biome.DeepNorth,
                "GoblinDeepNorth:2, Elaking:2, ElakingLantern:2, ElakingMole:2, DvergerDeepNorth:2, Moose:3, "
                + "ShadowPerson:3, JotunWitch:4, JotunWarrior:5, JotunWarriorDualWield:5, Barka:5"),
        };

        private static KeyValuePair<Heightmap.Biome, string> Pair(Heightmap.Biome biome, string line)
        {
            return new KeyValuePair<Heightmap.Biome, string>(biome, line);
        }

        /// <summary>The line a biome starts with, or empty for one Robbin gave no numbers.</summary>
        internal static string DefaultLine(Heightmap.Biome biome)
        {
            foreach (var pair in Defaults)
                if (pair.Key == biome) return pair.Value;

            return "";
        }

        /// <summary>One table entry once its prefab has been resolved against the running game.</summary>
        internal sealed class Entry
        {
            internal Heightmap.Biome Biome;
            internal string Prefab;
            internal string Token;
            internal int Points;

            /// <summary>Empty when it counts; otherwise why it does not.</summary>
            internal string Dropped = "";
        }

        /// <summary>
        /// Every table entry resolved to the token the kill tally files it under, with the shared
        /// token rule applied: the earliest biome in progression order (the order of
        /// UtangardConfig.GateableBiomes) keeps a token, and a later biome that lists it again is
        /// marked dropped. Within one biome the first entry for a token keeps it, since two points
        /// values for one token cannot both be true.
        ///
        /// Reads the live config lines, so what `utangard creatures` checks is what the bar pays.
        /// </summary>
        internal static List<Entry> Resolve(ZNetScene scene)
        {
            var result = new List<Entry>();
            var claimed = new Dictionary<string, Entry>();

            foreach (var biome in UtangardConfig.GateableBiomes)
            {
                foreach (var e in Parse(UtangardConfig.PointsLineFor(biome)))
                {
                    var entry = new Entry { Biome = biome, Prefab = e.Key, Points = e.Value };
                    result.Add(entry);

                    var go = scene != null ? scene.GetPrefab(e.Key) : null;
                    Character character;
                    if (go == null || !go.TryGetComponent(out character))
                    {
                        entry.Dropped = "no such creature";
                        continue;
                    }

                    entry.Token = character.m_name ?? "";

                    Entry owner;
                    if (claimed.TryGetValue(entry.Token, out owner))
                    {
                        // Two looks of one creature in one biome at one value - both morgen, both
                        // elaking, the three mages - are aliases, not a conflict: the tally cannot
                        // tell them apart and does not need to. They are folded into one kind when
                        // the table is built, so the tally is only ever counted once for them.
                        if (owner.Biome == entry.Biome && owner.Points == entry.Points) continue;

                        entry.Dropped = owner.Biome == entry.Biome
                            ? "shares " + entry.Token + " with " + owner.Prefab + ", which sets it at " + owner.Points
                            : "shares " + entry.Token + " with " + owner.Prefab + ", which counts in " + owner.Biome;
                        continue;
                    }

                    claimed[entry.Token] = entry;
                }
            }

            return result;
        }

        /// <summary>
        /// "Prefab:points, Prefab:points" into an ordered list. A malformed entry is skipped rather
        /// than thrown, so one typo in a config line costs one creature and not the biome.
        /// </summary>
        internal static List<KeyValuePair<string, int>> Parse(string line)
        {
            var result = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(line)) return result;

            foreach (var raw in line.Split(','))
            {
                var entry = raw.Trim();
                var colon = entry.LastIndexOf(':');
                if (colon <= 0) continue;

                int points;
                if (!int.TryParse(entry.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out points)) continue;
                if (points <= 0) continue;

                result.Add(new KeyValuePair<string, int>(entry.Substring(0, colon).Trim(), points));
            }

            return result;
        }

        // ------------------------------------------------------------ the table, cached ---

        /// <summary>One kind of creature as the bar sees it: one kill-tally token in one biome.</summary>
        private sealed class KindDef
        {
            internal string Token;
            internal int Points;
            internal readonly List<string> Prefabs = new List<string>(1);
        }

        private static Dictionary<Heightmap.Biome, List<KindDef>> _table;

        /// <summary>Every token in _table, in any biome. What KillTally keeps a count of.</summary>
        private static HashSet<string> _paid;

        private static ZNetScene _tableScene;
        private static int _tableVersion = -1;
        private static int _version;
        private static float _tableBuiltAt;
        private static bool _tableHadMisses;

        /// <summary>
        /// Rebuild the table on the next read. Called when a points line changes, whether a person
        /// edited it or Core handed over the host's.
        /// </summary>
        internal static void Invalidate()
        {
            _version++;
        }

        /// <summary>
        /// The resolved tables, per biome, folded into kinds. Null when there is no scene to
        /// resolve names against, which every caller reads as "nothing earned".
        ///
        /// Cached per ZNetScene, because the scene is rebuilt with every world and a table resolved
        /// against the last one would be answering about prefabs that no longer exist. A table that
        /// could not find some of its creatures is tried again every half minute, in case they were
        /// registered after it was first asked; resolving is sixty dictionary lookups, so that costs
        /// nothing, and a name that is simply wrong then costs nothing either.
        /// </summary>
        private static Dictionary<Heightmap.Biome, List<KindDef>> Table()
        {
            var scene = ZNetScene.instance;
            if (scene == null) return null;

            bool stale = _table == null
                || !ReferenceEquals(scene, _tableScene)
                || _tableVersion != _version
                || (_tableHadMisses && UnityEngine.Time.realtimeSinceStartup - _tableBuiltAt > 30f);

            if (!stale) return _table;

            var table = new Dictionary<Heightmap.Biome, List<KindDef>>();
            var paid = new HashSet<string>(StringComparer.Ordinal);
            var misses = false;

            foreach (var entry in Resolve(scene))
            {
                if (!string.IsNullOrEmpty(entry.Dropped))
                {
                    if (entry.Dropped == "no such creature") misses = true;
                    continue;
                }

                List<KindDef> kinds;
                if (!table.TryGetValue(entry.Biome, out kinds))
                    table[entry.Biome] = kinds = new List<KindDef>();

                // Aliases arrive here as separate undropped entries with one token, so fold them.
                // Counting each would pay the three dvergr mages' single tally three times over.
                KindDef kind = kinds.Find(k => k.Token == entry.Token);
                if (kind == null)
                {
                    kind = new KindDef { Token = entry.Token, Points = entry.Points };
                    kinds.Add(kind);
                }

                kind.Prefabs.Add(entry.Prefab);
                paid.Add(entry.Token);
            }

            _table = table;
            _paid = paid;
            _tableScene = scene;
            _tableVersion = _version;
            _tableBuiltAt = UnityEngine.Time.realtimeSinceStartup;
            _tableHadMisses = misses;
            return table;
        }

        /// <summary>
        /// Whether some biome's points line pays for a kill filed under this token. KillTally asks
        /// before it counts anything, so the tally holds only kinds a foothold can use and stays a
        /// few dozen numbers long.
        ///
        /// The Fighting bar used to read the game's lifetime tally, PlayerProfile.m_enemyStats,
        /// here. It counted kills from every world the character had played, from before the mod,
        /// from devcommands and of tamed animals, and Robbin's answer on 2026-09-27 was "only kills
        /// the mod saw happening". KillTally is that: see its class comment.
        /// </summary>
        internal static bool Pays(string token)
        {
            if (string.IsNullOrEmpty(token) || Table() == null) return false;

            var paid = _paid;
            return paid != null && paid.Contains(token);
        }

        // ------------------------------------------------------------------ the read API --

        /// <summary>One kind's share of a Fighting bar.</summary>
        internal sealed class Kind
        {
            /// <summary>The kill-tally token, e.g. $enemy_troll.</summary>
            public string Token;

            /// <summary>The token in the player's language, e.g. Troll.</summary>
            public string Name;

            /// <summary>The config names that resolved to this token. More than one for aliases.</summary>
            public List<string> Prefabs;

            /// <summary>Points per kill.</summary>
            public int Points;

            /// <summary>
            /// Kills Utangard counted for this character in this world, assists included.
            /// </summary>
            public int Kills;

            /// <summary>What this kind puts into the bar, after the cap.</summary>
            public int Contribution;

            /// <summary>Kills times points, before the cap.</summary>
            public int Earned;

            /// <summary>True when the cap took something off.</summary>
            public bool Capped;
        }

        /// <summary>
        /// One biome's foothold for the local character, in the shape the compendium panel and the
        /// console both draw. A snapshot: read it again to refresh, it is cheap.
        /// </summary>
        internal sealed class Standing
        {
            public Heightmap.Biome Biome;

            /// <summary>FootholdEnabled and the mod itself are on. Off, nothing is unlocked.</summary>
            public bool Enabled;

            /// <summary>The gate table names a key for this biome.</summary>
            public bool Gated;

            /// <summary>Gated, and the group has not earned it. The bars only mean something here.</summary>
            public bool Locked;

            /// <summary>
            /// This biome's rules are being applied to you right now. More than one biome can say
            /// so at once where two locked biomes meet.
            /// </summary>
            public bool Here;

            /// <summary>Fighting points, 0 to Full.</summary>
            public int Fighting;

            /// <summary>Fighting points that make a full bar (FightingFullPoints).</summary>
            public int Full;

            /// <summary>Where eating comes back, in percent of a full bar (EatAtFightingPercent).</summary>
            public int EatAtPercent;

            /// <summary>The same, in points.</summary>
            public int EatAt;

            /// <summary>The most one kind may put in, in points (MaxFromOneKindPercent of Full).</summary>
            public int KindCap;

            /// <summary>Every kind the biome's line pays for, killed or not, in the line's order.</summary>
            public List<Kind> Kinds = new List<Kind>();

            /// <summary>
            /// False when the kill tally could not be read, or kills are not being counted this
            /// session; Fighting is then 0.
            /// </summary>
            public bool FightingAvailable;

            /// <summary>Map pixels of this biome the character uncovered on foot.</summary>
            public int Discovered;

            /// <summary>Pixels that make a full Discovery bar, or 0 when the map cannot be read.</summary>
            public int DiscoveryFull;

            /// <summary>Discovery, 0 to 100.</summary>
            public float DiscoveryPercent;

            /// <summary>False when the minimap could not be read at all. The bar is then empty.</summary>
            public bool DiscoveryAvailable;

            /// <summary>
            /// The background count of this world's map is still running, so Discovered is a lower
            /// bound that will only grow. Worth a word on the panel, or a full bar reads as stuck.
            /// </summary>
            public bool DiscoveryCounting;

            /// <summary>Fighting has reached EatAt (and the foothold is on).</summary>
            public bool CanEat;

            /// <summary>Both bars are full (and the foothold is on).</summary>
            public bool CanHeal;

            /// <summary>
            /// The Fighting bar in percent, rounded down, which is the number every screen shows and
            /// the one EatAtPercent is compared with.
            /// </summary>
            public int FightingPercent
            {
                get { return Percent(Fighting, Full); }
            }
        }

        private static bool _saidFailure;

        /// <summary>
        /// The local character's foothold in one biome. Never throws: a read that fails comes back
        /// with that bar empty and unavailable, which is the lock as it was.
        /// </summary>
        internal static Standing Read(Heightmap.Biome biome)
        {
            int full = FullBar();
            var s = new Standing
            {
                Biome = biome,
                Enabled = On(),
                Full = full,
                EatAtPercent = EatAtPercent(),
                EatAt = EatAtPoints(full),
                KindCap = KindCap(full),
            };

            try
            {
                var key = UtangardConfig.RequiredKeyFor(biome);
                s.Gated = key != null;

                var zone = ZoneSystem.instance;
                s.Locked = s.Gated && zone != null && !BiomeGate.Earned(zone, key);

                s.Here = BiomeGate.RulingBiomes(Player.m_localPlayer, Ruling) && Ruling.Contains(biome);

                FillFighting(s);
                FillDiscovery(s);

                s.CanEat = s.Enabled && s.FightingAvailable && s.Fighting >= s.EatAt;
                s.CanHeal = s.Enabled && s.FightingAvailable && s.DiscoveryAvailable
                    && s.Fighting >= s.Full && s.Discovered >= s.DiscoveryFull;
            }
            catch (Exception e)
            {
                SayFailure(e);
                s.CanEat = false;
                s.CanHeal = false;
            }

            return s;
        }

        /// <summary>Every gateable biome, in progression order.</summary>
        internal static List<Standing> ReadAll()
        {
            var all = new List<Standing>();
            foreach (var biome in UtangardConfig.GateableBiomes) all.Add(Read(biome));
            return all;
        }

        /// <summary>
        /// The first locked biome in progression order - where the character's group is stuck, and
        /// the one the panel opens on. Biome.None when nothing is locked.
        /// </summary>
        internal static Heightmap.Biome Frontier()
        {
            var zone = ZoneSystem.instance;
            if (zone == null) return Heightmap.Biome.None;

            foreach (var biome in UtangardConfig.GateableBiomes)
            {
                var key = UtangardConfig.RequiredKeyFor(biome);
                if (key != null && !BiomeGate.Earned(zone, key)) return biome;
            }

            return Heightmap.Biome.None;
        }

        private static void FillFighting(Standing s)
        {
            var tally = KillTally.Here();
            var table = Table();
            s.FightingAvailable = tally != null && table != null;
            if (!s.FightingAvailable) return;

            List<KindDef> kinds;
            if (!table.TryGetValue(s.Biome, out kinds)) return;

            var loc = Localization.instance;
            long sum = 0;

            foreach (var def in kinds)
            {
                int kills;
                tally.TryGetValue(def.Token, out kills);
                if (kills < 0) kills = 0;

                var earned = Earned(kills, def.Points);
                var capped = Math.Min(earned, s.KindCap);

                string name = loc != null ? loc.Localize(def.Token) : null;
                if (string.IsNullOrEmpty(name) || name == def.Token) name = def.Prefabs[0];

                s.Kinds.Add(new Kind
                {
                    Token = def.Token,
                    Name = name,
                    Prefabs = new List<string>(def.Prefabs),
                    Points = def.Points,
                    Kills = kills,
                    Earned = earned,
                    Contribution = capped,
                    Capped = capped < earned,
                });

                sum += capped;
            }

            s.Fighting = (int)Math.Min(s.Full, sum);
        }

        /// <summary>Kills times points, held at int.MaxValue rather than wrapping.</summary>
        private static int Earned(int kills, int points)
        {
            return (int)Math.Min(int.MaxValue, (long)kills * points);
        }

        private static void FillDiscovery(Standing s)
        {
            s.DiscoveryAvailable = Discovery.Available();
            if (!s.DiscoveryAvailable) return;

            s.Discovered = Discovery.Pixels(s.Biome);
            s.DiscoveryFull = Discovery.FullPixels();
            s.DiscoveryCounting = Discovery.Counting();

            // A full count of zero means the config asked for no walking at all.
            s.DiscoveryPercent = s.DiscoveryFull <= 0
                ? 100f
                : Math.Min(100f, s.Discovered * 100f / s.DiscoveryFull);
        }

        // --------------------------------------------------------------- the unlocks -----

        /// <summary>
        /// Whether the eating refusal is lifted for this player where they stand: the foothold is
        /// on, and their Fighting bar has reached EatAtFightingPercent in every locked biome whose
        /// rules reach them here. One biome short is a refusal, which is what keeps a foothold
        /// from being an eating spot on the edge of a biome you have not earned (see RulingBiomes).
        ///
        /// Only the local player can answer yes, because only its kill tally is on this machine,
        /// and that is also the only player the refusal ever runs for.
        /// </summary>
        internal static bool EatingAllowed(Player player)
        {
            if (!On()) return false;

            try
            {
                if (!BiomeGate.RulingBiomes(player, Ruling)) return false;

                int eatAt = EatAtPoints(FullBar());
                foreach (var biome in Ruling)
                {
                    int fighting;
                    if (!FightingIn(biome, out fighting) || fighting < eatAt) return false;
                }

                return true;
            }
            catch (Exception e)
            {
                SayFailure(e);
                return false;
            }
        }

        /// <summary>
        /// Whether healing is back to normal for this player where they stand: both bars full in
        /// every locked biome whose rules reach them here.
        /// </summary>
        internal static bool HealingAllowed(Player player)
        {
            if (!On()) return false;

            try
            {
                if (!BiomeGate.RulingBiomes(player, Ruling)) return false;
                if (!Discovery.Available()) return false;

                int fullBar = FullBar();
                int fullMap = Discovery.FullPixels();
                foreach (var biome in Ruling)
                {
                    int fighting;
                    if (!FightingIn(biome, out fighting) || fighting < fullBar) return false;
                    if (Discovery.Pixels(biome) < fullMap) return false;
                }

                return true;
            }
            catch (Exception e)
            {
                SayFailure(e);
                return false;
            }
        }

        /// <summary>
        /// Scratch list for RulingBiomes. Every caller is on the main thread and finishes with it
        /// before returning, so one list serves them all and a bite allocates nothing.
        /// </summary>
        private static readonly List<Heightmap.Biome> Ruling = new List<Heightmap.Biome>(3);

        /// <summary>
        /// The line added under a refused meal, saying how far the Fighting bar has got here, or
        /// null for none. Without it the bars are invisible until somebody opens the compendium,
        /// and the moment a bite is refused is exactly when a player wants to know what would
        /// change that.
        /// </summary>
        internal static string EatingHint(Player player)
        {
            if (!On()) return null;

            try
            {
                string line = UtangardConfig.EatProgressLine.Value;
                if (string.IsNullOrEmpty(line)) return null;

                // Past a full bar the threshold cannot be reached, so there is nothing to aim at.
                if (EatAtPercent() > 100) return null;

                if (!BiomeGate.RulingBiomes(player, Ruling)) return null;

                int full = FullBar();
                int eatAt = EatAtPoints(full);

                // The biome doing the refusing: the first one short of the mark. Where two locked
                // biomes meet, naming the one already past it would tell a player they may eat
                // while the bite is being refused.
                var biome = Heightmap.Biome.None;
                int fighting = 0;
                foreach (var b in Ruling)
                {
                    int f;
                    if (!FightingIn(b, out f)) return null;
                    if (f >= eatAt) continue;

                    biome = b;
                    fighting = f;
                    break;
                }

                if (biome == Heightmap.Biome.None) return null;

                // Both in percent, the bar rounded down as everywhere else, so the line can never
                // say 50% over a bite that is being refused at a 50% mark. Points would not do
                // since the bar stopped being 100: "Fighting here 60. You can eat at 75." reads as
                // a percent and is not one.
                //
                // Replace rather than string.Format, so a stray brace in someone's wording cannot
                // throw in the middle of a refusal.
                return line
                    .Replace("{fighting}", Percent(fighting, full).ToString(CultureInfo.InvariantCulture))
                    .Replace("{eat}", EatAtPercent().ToString(CultureInfo.InvariantCulture))
                    .Replace("{biome}", GateReport.BiomeName(biome));
            }
            catch (Exception e)
            {
                SayFailure(e);
                return null;
            }
        }

        /// <summary>
        /// The Fighting bar for one biome, in points, without building the whole Standing. False
        /// when the tally or the table cannot be read, which callers treat as not earned.
        /// </summary>
        private static bool FightingIn(Heightmap.Biome biome, out int fighting)
        {
            fighting = 0;

            var tally = KillTally.Here();
            var table = Table();
            if (tally == null || table == null) return false;

            List<KindDef> kinds;
            if (!table.TryGetValue(biome, out kinds)) return true;

            int full = FullBar();
            int cap = KindCap(full);
            long sum = 0;
            foreach (var def in kinds)
            {
                int kills;
                tally.TryGetValue(def.Token, out kills);
                if (kills <= 0) continue;

                sum += Math.Min(Earned(kills, def.Points), cap);
            }

            fighting = (int)Math.Min(full, sum);
            return true;
        }

        private static bool On()
        {
            return UtangardConfig.Enabled.Value && UtangardConfig.FootholdEnabled.Value;
        }

        /// <summary>
        /// Once per session. These run on every refused bite and every regen tick, and one line is
        /// all a bug report needs.
        /// </summary>
        private static void SayFailure(Exception e)
        {
            if (_saidFailure) return;
            _saidFailure = true;

            UtangardPlugin.Log.LogError(
                "Utangard could not read a foothold, so eating and healing stay locked in a locked "
                + "biome as they were before footholds existed. " + e);
        }
    }
}

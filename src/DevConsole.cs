using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// `utangard`, the console command. Three verbs: `foothold`, the local character's two bars
    /// in every biome as the unlocks read them; and `biomes` and `creatures`, the raw material
    /// the foothold tables were written from.
    ///
    /// The design Robbin settled on 2026-09-24 is that a character earns back eating and health
    /// regeneration in a biome that is still gated, one biome at a time, by fighting there and
    /// by exploring it - counted from what the game already records per character rather than
    /// from anything Utangard stores. Numbers for that cannot be picked from a desk, because
    /// both halves of the answer are data that exists only in a running world: which creatures
    /// the spawn tables put in each biome, and how big each biome is on this world's map. This
    /// prints both, beside what the local character has actually done, so the thresholds are
    /// chosen from real lists rather than guessed.
    ///
    /// Read-only, so registered isCheat: false. It needs no devcommands and marks nothing.
    ///
    /// <b>What each half reads, and what it cannot see.</b>
    ///
    ///   Kills come from PlayerProfile's per-creature tally, keyed by the creature's display
    ///   token ($enemy_wolf). The game writes it in Game.RPC_RegisterKill for every player who
    ///   damaged the creature, not only the one who landed the blow. It is lifetime and per
    ///   character, which on Longhouse is the same thing as per world, since the server only
    ///   admits characters made for it.
    ///
    ///   Creatures per biome come from the world's spawn tables, the same source the shared
    ///   BiomeIndex reads. Those are the overworld spawns only: a draugr placed by a crypt's own
    ///   spawner, or a camp's fulings, are not in them. A row gated behind a world key or a
    ///   weather is marked, because a creature you cannot meet yet is not one a threshold
    ///   should count.
    ///
    ///   Exploration comes from the minimap's own explored bits - m_explored, the fog this
    ///   character lifted by walking, not m_exploredOthers, which a cartography table hands
    ///   out - checked against WorldGenerator.GetBiome at each map pixel. The game does not keep
    ///   exploration per biome; this works it out from what it does keep.
    /// </summary>
    internal static class DevConsole
    {
        /// <summary>
        /// Process-wide: Terminal's command table is a private static nothing clears, so a
        /// second registration is a duplicate that outlives the world.
        /// </summary>
        private static bool _registered;

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        internal static class Hook
        {
            private static void Postfix()
            {
                Register();
            }
        }

        private static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("utangard",
                "utangard foothold | biomes | creatures - your foothold in each locked biome, and the raw numbers behind it",
                OnCommand, isCheat: false);
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            var term = args.Context;
            if (term == null) return;

            var what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (what == "foothold") { FootholdReport(term); return; }
            if (what == "biomes") { Biomes(term); return; }
            if (what == "creatures") { Creatures(term); return; }

            term.AddString("utangard foothold - per biome: your Fighting and Discovery bars, what each kind of creature put in, and what is unlocked");
            term.AddString("utangard biomes - per biome: its creatures and your kills of each, and how much of it you have explored");
            term.AddString("utangard creatures - every creature in the foothold tables, checked against the game");
        }

        /// <summary>
        /// `utangard foothold`: the local character's two bars in every biome, the way the unlocks
        /// read them. Read through Foothold.Read, the same call the compendium panel makes, so the
        /// console and the panel cannot disagree about a number.
        ///
        /// Every kind is listed, killed or not, with the cap shown where it bit: "why is my bar
        /// stuck at 50" is nearly always one kind at its cap, and that is only visible beside the
        /// kinds that have not been touched yet.
        /// </summary>
        private static void FootholdReport(Terminal term)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Say(term, "utangard foothold: no character in a world yet.");
                return;
            }

            var inv = CultureInfo.InvariantCulture;
            var full = Discovery.FullPixels();
            var pixelKm2 = Discovery.PixelKm2();

            Say(term, "utangard foothold: " + player.GetPlayerName() + ". "
                      + (UtangardConfig.FootholdEnabled.Value ? "" : "FootholdEnabled is OFF, so nothing below unlocks anything. ")
                      + "Eating at " + UtangardConfig.EatAtFighting.Value + " fighting, healing at "
                      + Foothold.FullBar + " fighting and full discovery. No kind above "
                      + UtangardConfig.MaxFromOneKind.Value + ".");

            if (!Discovery.Available())
                Say(term, "utangard foothold: the explored map cannot be read this session, so every Discovery bar is empty.");
            else
                Say(term, "utangard foothold: full discovery is "
                          + UtangardConfig.DiscoveryFullKm2.Value.ToString("0.##", inv) + " km2 = " + full + " map pixels of "
                          + (pixelKm2 * 1000000f).ToString("0", inv) + " m2. "
                          + (Discovery.Counting()
                              ? "Still counting this world's map (" + (Discovery.Progress() * 100f).ToString("0", inv)
                                + "%), so discovery can only go up from here."
                              : "Map counted. " + Discovery.Describe()));

            foreach (var s in Foothold.ReadAll())
            {
                if (!s.Gated && s.Kinds.Count == 0) continue;

                var state = !s.Gated ? "ungated" : s.Locked ? "LOCKED" : "open";
                if (s.Here) state += ", its rules apply to you now";

                var unlocked = !s.Locked
                    ? "nothing to unlock"
                    : (s.CanEat ? "eating allowed" : "eating at " + s.EatAt + "%")
                      + ", " + (s.CanHeal ? "healing allowed" : "healing needs both full");

                var fighting = s.FightingAvailable
                    ? s.Fighting + "/" + Foothold.FullBar + " (" + s.FightingPercent.ToString("0", inv) + "%)"
                    : "unreadable";

                var discovery = s.DiscoveryAvailable
                    ? s.Discovered + "/" + s.DiscoveryFull + " px (" + s.DiscoveryPercent.ToString("0", inv) + "%"
                      + (s.DiscoveryCounting ? ", still counting" : "") + ")"
                    : "unreadable";

                Say(term, s.Biome + " (" + state + "): fighting " + fighting + ", discovery " + discovery + " - " + unlocked + ".");

                if (s.Kinds.Count == 0)
                {
                    Say(term, "    no creatures on this biome's points line, so its Fighting bar cannot fill.");
                    continue;
                }

                var parts = new List<string>();
                foreach (var k in s.Kinds)
                {
                    var part = k.Name + " " + k.Points + "pt x" + k.Kills + " = " + k.Contribution;
                    if (k.Capped) part += " (capped at " + s.KindCap + ", " + k.Earned + " earned)";
                    parts.Add(part);
                }

                Say(term, "    " + string.Join(", ", parts.ToArray()));
            }
        }

        private static readonly Heightmap.Biome[] Order =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        private sealed class Creature
        {
            internal string Prefab;
            internal string Token;
            internal float Health;
            internal bool Unconditional;
            internal readonly List<string> Rows = new List<string>();
        }

        private static void Biomes(Terminal term)
        {
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile == null || Player.m_localPlayer == null)
            {
                Say(term, "utangard biomes: no character in a world yet.");
                return;
            }

            var kills = profile.m_playerStats[0].m_enemyStats[0];
            var creatures = SpawnTables();
            Dictionary<Heightmap.Biome, int> area, seen;
            float pixelKm2;
            var mapNote = Exploration(out area, out seen, out pixelKm2);

            Say(term, "utangard biomes: " + profile.GetName() + ". " + mapNote);

            foreach (var biome in Order)
            {
                area.TryGetValue(biome, out var total);
                seen.TryGetValue(biome, out var mine);

                var totalKm2 = total * pixelKm2;
                var mineKm2 = mine * pixelKm2;
                var share = total > 0 ? 100f * mine / total : 0f;

                creatures.TryGetValue(biome, out var list);
                var rows = list != null ? list.Values.OrderBy(c => c.Prefab, StringComparer.Ordinal).ToList() : new List<Creature>();

                var kinds = 0;
                var killed = 0;
                var sum = 0f;
                var parts = new List<string>();
                foreach (var c in rows)
                {
                    kills.TryGetValue(c.Token, out var n);
                    if (c.Unconditional) kinds++;
                    if (n > 0f) { killed++; sum += n; }
                    parts.Add(c.Prefab + (c.Unconditional ? "" : "*") + " " + c.Health.ToString("0", CultureInfo.InvariantCulture)
                              + "hp x" + n.ToString("0", CultureInfo.InvariantCulture));
                }

                Say(term, biome + ": " + totalKm2.ToString("0.0", CultureInfo.InvariantCulture) + " km2 on this map, you explored "
                          + mineKm2.ToString("0.00", CultureInfo.InvariantCulture) + " km2 ("
                          + share.ToString("0.0", CultureInfo.InvariantCulture) + "%). Creatures: "
                          + kinds + " kinds you can always meet, you have killed " + killed + " kinds, "
                          + sum.ToString("0", CultureInfo.InvariantCulture) + " kills.");
                if (parts.Count > 0) Say(term, "    " + string.Join(", ", parts.ToArray()));
            }

            Say(term, "utangard biomes: * = only spawns behind a world key, a weather or a persistent event. "
                      + "Spawn tables only - creatures a dungeon or a camp places itself are not in them. "
                      + "Each entry is health, then your kills. Kills include assists.");

            // A creature in most biomes' tables is either a real wanderer or a row gated by
            // something this does not read. Print its raw rows so which one is decidable.
            var seenIn = new Dictionary<string, Creature>();
            var count = new Dictionary<string, int>();
            foreach (var pair in creatures)
                foreach (var c in pair.Value.Values)
                {
                    seenIn[c.Prefab] = c;
                    count.TryGetValue(c.Prefab, out var k);
                    count[c.Prefab] = k + 1;
                }

            foreach (var pair in count.Where(p => p.Value >= 5).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Say(term, "  in " + pair.Value + " biomes: " + pair.Key + ", rows:");
                foreach (var row in seenIn[pair.Key].Rows) Say(term, "    " + row);
            }
        }

        /// <summary>
        /// Every creature each biome's spawn rows can produce, bosses left out. A creature with
        /// even one unconditional row in a biome counts as always met there; one that only
        /// arrives behind a world key or a weather is marked, since a threshold that counted it
        /// could be impossible to meet on the day.
        /// </summary>
        private static Dictionary<Heightmap.Biome, Dictionary<string, Creature>> SpawnTables()
        {
            var result = new Dictionary<Heightmap.Biome, Dictionary<string, Creature>>();

            var spawn = UnityEngine.Object.FindObjectOfType<SpawnSystem>();
            if (spawn == null || spawn.m_spawnLists == null) return result;

            foreach (var list in spawn.m_spawnLists)
            {
                if (list == null || list.m_spawners == null) continue;

                foreach (var row in list.m_spawners)
                {
                    if (row == null || !row.m_enabled || row.m_devDisabled || row.m_prefab == null) continue;

                    Character character;
                    if (!row.m_prefab.TryGetComponent(out character)) continue;
                    if (character.IsBoss()) continue;

                    // Three gates, not two. m_requiredPersistentEvent was missed at first, and it
                    // is what put Elaking and the Jotun in every biome's list, Meadows included.
                    var conditional = !string.IsNullOrEmpty(row.m_requiredGlobalKey)
                                      || !string.IsNullOrEmpty(row.m_requiredPersistentEvent)
                                      || (row.m_requiredEnvironments != null && row.m_requiredEnvironments.Count > 0);

                    var describe = "'" + row.m_name + "' biomes=" + row.m_biome + " area=" + row.m_biomeArea
                                   + " key='" + row.m_requiredGlobalKey + "' event='" + row.m_requiredPersistentEvent
                                   + "' env=" + (row.m_requiredEnvironments != null ? row.m_requiredEnvironments.Count : 0)
                                   + " chance=" + row.m_spawnChance.ToString("0.#", CultureInfo.InvariantCulture)
                                   + " max=" + row.m_maxSpawned
                                   + " day=" + row.m_spawnAtDay + " night=" + row.m_spawnAtNight;

                    foreach (var biome in Order)
                    {
                        if ((row.m_biome & biome) == 0) continue;

                        if (!result.TryGetValue(biome, out var inBiome))
                            result[biome] = inBiome = new Dictionary<string, Creature>();

                        if (!inBiome.TryGetValue(row.m_prefab.name, out var c))
                            inBiome[row.m_prefab.name] = c = new Creature
                            {
                                Prefab = row.m_prefab.name, Token = character.m_name, Health = character.m_health,
                            };

                        if (!conditional) c.Unconditional = true;
                        if (!c.Rows.Contains(describe)) c.Rows.Add(describe);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Map pixels per biome, and how many of them this character uncovered on foot.
        /// Sampled every other pixel on a large map, which is plenty for a size and keeps the
        /// command under a couple of seconds.
        /// </summary>
        private static string Exploration(out Dictionary<Heightmap.Biome, int> area,
                                          out Dictionary<Heightmap.Biome, int> seen,
                                          out float pixelKm2)
        {
            area = new Dictionary<Heightmap.Biome, int>();
            seen = new Dictionary<Heightmap.Biome, int>();
            pixelKm2 = 0f;

            var map = Minimap.instance;
            var world = WorldGenerator.instance;
            if (map == null || world == null) return "No minimap or world generator, so no exploration.";

            BitArray explored = null;
            try
            {
                explored = Traverse.Create(map).Field("m_explored").GetValue<BitArray>();
            }
            catch (Exception)
            {
                explored = null;
            }

            var size = map.m_textureSize;
            var metres = map.m_pixelSize;
            var stride = size >= 1024 ? 2 : 1;
            var half = size / 2;
            const float Edge = 10000f;

            for (var y = 0; y < size; y += stride)
            {
                for (var x = 0; x < size; x += stride)
                {
                    var wx = (x - half) * metres;
                    var wz = (y - half) * metres;
                    if (wx * wx + wz * wz > Edge * Edge) continue;

                    var biome = world.GetBiome(wx, wz);
                    area.TryGetValue(biome, out var a);
                    area[biome] = a + 1;

                    if (explored != null && explored[y * size + x])
                    {
                        seen.TryGetValue(biome, out var s);
                        seen[biome] = s + 1;
                    }
                }
            }

            pixelKm2 = stride * stride * metres * metres / 1000000f;

            return "Map " + size + "x" + size + " at " + metres.ToString("0.#", CultureInfo.InvariantCulture)
                   + " m a pixel, explore radius " + map.m_exploreRadius.ToString("0", CultureInfo.InvariantCulture) + " m"
                   + (explored == null ? ", and the explored bits could not be read." : ".");
        }

        /// <summary>
        /// Prefabs deliberately left out of the foothold tables, checked alongside them so the
        /// prey reading can be compared with Robbin's calls. Two spellings where the game files
        /// carry both and only one of them is the creature.
        /// </summary>
        private static readonly string[] LeftOut =
        {
            "Deer", "Bat", "Hare", "SeekerBrood", "Seal", "Seal_Pup", "Seal_pup", "Moose_calf",
            "Asksvin_hatchling", "AsksvinHatchling", "Skeleton_Swamps",
        };

        /// <summary>
        /// Every creature in Foothold's tables, resolved against the running game: whether the
        /// prefab exists, the token the kill tally will file it under, its health, its faction,
        /// and whether its brain is a monster's or an animal's. Answers three questions before
        /// any of LHM-26 is built on the tables:
        ///
        ///   which of the new creatures the game treats as prey - an AnimalAI, or the AnimalsVeg
        ///   faction - since prey is meant to be worth nothing, as deer are;
        ///
        ///   which prefabs share a token, since the tally cannot tell them apart and they cannot
        ///   then be given different points, or be counted in one biome and not another;
        ///
        ///   the health of the camp and town creatures the spawn tables never showed.
        /// </summary>
        private static void Creatures(Terminal term)
        {
            var scene = ZNetScene.instance;
            if (scene == null) { Say(term, "utangard creatures: no world loaded yet."); return; }

            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            var kills = profile != null ? profile.m_playerStats[0].m_enemyStats[0] : null;

            var byToken = new Dictionary<string, List<string>>();

            // The live Points_ lines, not the defaults in code, so this checks what the bar pays.
            foreach (var biome in UtangardConfig.GateableBiomes)
            {
                var entries = Foothold.Parse(UtangardConfig.PointsLineFor(biome));
                if (entries.Count == 0) continue;

                Say(term, "utangard creatures: " + biome + ", " + entries.Count + " kinds, full bar " + Foothold.FullBar
                          + ", no kind above " + UtangardConfig.MaxFromOneKind.Value + ".");

                foreach (var e in entries)
                    Say(term, "    " + e.Value + " pt  " + Describe(scene, e.Key, kills, byToken, biome + " " + e.Key + "=" + e.Value));
            }

            Say(term, "utangard creatures: left out on purpose -");
            foreach (var name in LeftOut)
                Say(term, "    -     " + Describe(scene, name, kills, byToken, "left out " + name));

            var shared = 0;
            foreach (var pair in byToken)
            {
                if (pair.Value.Count < 2) continue;
                shared++;
                Say(term, "utangard creatures: SHARED TOKEN " + pair.Key + " - " + string.Join("; ", pair.Value.ToArray()));
            }

            Say(term, shared == 0
                ? "utangard creatures: every creature has a token of its own."
                : "utangard creatures: " + shared + " token(s) shared - those creatures cannot be scored apart.");

            // The rule applied, so what the tables actually pay is visible beside what they say.
            var dropped = 0;
            foreach (var entry in Foothold.Resolve(scene))
            {
                if (string.IsNullOrEmpty(entry.Dropped)) continue;
                dropped++;
                Say(term, "utangard creatures: DROPPED " + entry.Biome + " " + entry.Prefab + " - " + entry.Dropped);
            }

            Say(term, dropped == 0
                ? "utangard creatures: after the shared-name rule, every entry in the tables counts."
                : "utangard creatures: after the shared-name rule, " + dropped + " entr(ies) do not count.");
        }

        private static string Describe(ZNetScene scene, string prefabName, Dictionary<string, float> kills,
                                       Dictionary<string, List<string>> byToken, string label)
        {
            var go = scene.GetPrefab(prefabName);
            if (go == null) return prefabName + " - NOT FOUND in ZNetScene";

            Character character;
            if (!go.TryGetComponent(out character)) return prefabName + " - not a creature";

            string brain = "no AI";
            if (go.GetComponent<MonsterAI>() != null) brain = "monster";
            else if (go.GetComponent<AnimalAI>() != null) brain = "ANIMAL";

            var tame = go.GetComponent<Tameable>() != null ? ", tameable" : "";
            var prey = brain == "ANIMAL" || character.m_faction == Character.Faction.AnimalsVeg ? "  <- prey" : "";

            var token = character.m_name ?? "";
            if (!byToken.TryGetValue(token, out var list)) byToken[token] = list = new List<string>();
            list.Add(label);

            var n = 0f;
            if (kills != null) kills.TryGetValue(token, out n);

            return prefabName + " " + token + " " + character.m_health.ToString("0", CultureInfo.InvariantCulture) + "hp "
                   + character.m_faction + " " + brain + tame + ", your kills " + n.ToString("0", CultureInfo.InvariantCulture) + prey;
        }

        /// <summary>To the console and the log, so a run leaves the numbers on disk.</summary>
        private static void Say(Terminal term, string line)
        {
            term.AddString(line);
            UtangardPlugin.Log.LogInfo(line);
        }
    }
}

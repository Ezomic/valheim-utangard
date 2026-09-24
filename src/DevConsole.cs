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
    /// `utangard`, the console command. One verb for now: `biomes`, the raw material for
    /// per-character reliefs in a gated biome.
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
                "utangard biomes - per biome: its creatures and your kills of each, and how much of it you have explored",
                OnCommand, isCheat: false);
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            var term = args.Context;
            if (term == null) return;

            var what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (what == "biomes") { Biomes(term); return; }

            term.AddString("utangard biomes - per biome: its creatures and your kills of each, and how much of it you have explored");
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
            internal bool Unconditional;
            internal string Gate = "";
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
                    parts.Add(c.Prefab + (c.Unconditional ? "" : "*") + " " + n.ToString("0", CultureInfo.InvariantCulture));
                }

                Say(term, biome + ": " + totalKm2.ToString("0.0", CultureInfo.InvariantCulture) + " km2 on this map, you explored "
                          + mineKm2.ToString("0.00", CultureInfo.InvariantCulture) + " km2 ("
                          + share.ToString("0.0", CultureInfo.InvariantCulture) + "%). Creatures: "
                          + kinds + " kinds you can always meet, you have killed " + killed + " kinds, "
                          + sum.ToString("0", CultureInfo.InvariantCulture) + " kills.");
                if (parts.Count > 0) Say(term, "    " + string.Join(", ", parts.ToArray()));
            }

            Say(term, "utangard biomes: * = only spawns behind a world key or a weather. Spawn tables only - "
                      + "creatures a dungeon or a camp places itself are not in them. Kills include assists.");
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

                    var conditional = !string.IsNullOrEmpty(row.m_requiredGlobalKey)
                                      || (row.m_requiredEnvironments != null && row.m_requiredEnvironments.Count > 0);

                    foreach (var biome in Order)
                    {
                        if ((row.m_biome & biome) == 0) continue;

                        if (!result.TryGetValue(biome, out var inBiome))
                            result[biome] = inBiome = new Dictionary<string, Creature>();

                        if (!inBiome.TryGetValue(row.m_prefab.name, out var c))
                            inBiome[row.m_prefab.name] = c = new Creature { Prefab = row.m_prefab.name, Token = character.m_name };

                        if (!conditional) c.Unconditional = true;
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

        /// <summary>To the console and the log, so a run leaves the numbers on disk.</summary>
        private static void Say(Terminal term, string line)
        {
            term.AddString(line);
            UtangardPlugin.Log.LogInfo(line);
        }
    }
}

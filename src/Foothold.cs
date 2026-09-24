using System;
using System.Collections.Generic;
using System.Globalization;

namespace Utangard
{
    /// <summary>
    /// What a kill is worth towards a character's foothold in a biome it has not earned yet.
    /// LHM-26.
    ///
    /// Hand-set by Robbin on 2026-09-24, biome by biome, after a first draft that paid each kill
    /// its creature's health: that made the bar a sum of hit points, and he wanted the numbers to
    /// say how much a creature matters rather than how long it takes to fall. So each biome's
    /// common creature is worth 1 and its big threat 5, a full bar is 100 whatever the biome,
    /// eating comes back at 50, and no one kind of creature may supply more than half of it.
    ///
    /// A creature missing from a biome's line is worth nothing there, and that is how prey and
    /// the harmless are kept out - deer in the Black Forest, bats in the Mountains, hares and
    /// seed-sized seeker brood in the Mistlands, seals in the Deep North. Swamp skeletons are left
    /// out on purpose too. Listing by name rather than by spawn table is also what lets the cave
    /// and camp creatures count: ulvs and cultists in a frost cave, surtlings at a fire geyser,
    /// growths in a tar pit, a charred warlock in a fortress. None of those are in any spawn list.
    ///
    /// Keyed by prefab name here because that is what a person can read and type. The game's kill
    /// tally is keyed by the creature's display token, so the name is resolved to a token at
    /// runtime; two prefabs that share one token cannot be scored apart, which is what
    /// `utangard creatures` checks.
    ///
    /// Plain strings, one per biome, so they can become config lines with no change of shape.
    /// </summary>
    internal static class Foothold
    {
        internal const int FullBar = 100;

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
                "Seeker:1, Tick:1, Dverger:2, DvergerMageSupport:2, DvergerMageFire:3, DvergerMageIce:3, SeekerBrute:4, Gjall:5"),
            Pair(Heightmap.Biome.AshLands,
                "Charred_Archer:1, Charred_Twitcher:1, Volture:1, BlobLava:1, Charred_Melee:2, Asksvin:2, DvergerAshlands:2, "
                + "Charred_Mage:3, BonemawSerpent:4, FallenValkyrie:5, Morgen:5, Morgen_NonSleeping:5, Charred_Melee_Dyrnwyn:5"),
            Pair(Heightmap.Biome.DeepNorth,
                "Greydwarf_Frozen:1, Skeleton_DeepNorth:1, Greydwarf_Shaman_Frozen:2, GoblinDeepNorth:2, Elaking:2, "
                + "ElakingLantern:2, ElakingMole:2, DvergerDeepNorth:2, Moose:3, ShadowPerson:3, JotunWitch:4, "
                + "JotunWarrior:5, JotunWarriorDualWield:5, Barka:5"),
        };

        private static KeyValuePair<Heightmap.Biome, string> Pair(Heightmap.Biome biome, string line)
        {
            return new KeyValuePair<Heightmap.Biome, string>(biome, line);
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
    }
}

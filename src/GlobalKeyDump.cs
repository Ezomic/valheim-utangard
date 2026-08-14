using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace Wither
{
    /// <summary>
    /// Prints what the world actually knows, once per spawn.
    ///
    /// This exists because of the mod's one silent failure mode: a key name in the Gate
    /// table that no world ever sets. GetGlobalKey returns false for it forever, so the
    /// biome is gated permanently and looks exactly like a working gate against a boss you
    /// have not killed. Two of the shipped defaults - the Queen's and Fader's - are not in
    /// the game's GlobalKeys enum and were taken from prefab data, so they are precisely the
    /// ones worth checking against a real save.
    /// </summary>
    internal static class GlobalKeyDump
    {
        private static readonly AccessTools.FieldRef<ZoneSystem, HashSet<string>> KeysOf =
            AccessTools.FieldRefAccess<ZoneSystem, HashSet<string>>("m_globalKeys");

        private static readonly Heightmap.Biome[] Gateable =
        {
            Heightmap.Biome.Meadows,
            Heightmap.Biome.BlackForest,
            Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains,
            Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands,
            Heightmap.Biome.DeepNorth,
            Heightmap.Biome.Ocean
        };

        public static void Log()
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null)
            {
                WitherPlugin.Log.LogWarning("No ZoneSystem yet - nothing to report.");
                return;
            }

            HashSet<string> keys = KeysOf(zone);
            var sorted = new List<string>(keys ?? new HashSet<string>());
            sorted.Sort();

            var line = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) line.Append(", ");
                line.Append(sorted[i]);
            }

            WitherPlugin.Log.LogInfo("World global keys (" + sorted.Count + "): "
                + (sorted.Count == 0 ? "(none)" : line.ToString()));

            WitherPlugin.Log.LogInfo("Gate table:");
            foreach (Heightmap.Biome biome in Gateable)
            {
                string key = WitherConfig.RequiredKeyFor(biome);
                if (key == null)
                {
                    WitherPlugin.Log.LogInfo("  " + biome + ": ungated");
                    continue;
                }

                // "not set" is the interesting case and it is ambiguous by nature: either the
                // boss is alive, or the key name is wrong. Say so rather than reporting a
                // typo as a closed gate.
                WitherPlugin.Log.LogInfo("  " + biome + ": needs '" + key + "' - "
                    + (zone.GetGlobalKey(key) ? "set, biome is open" : "NOT set, biome withers"));
            }
        }
    }
}

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

        private static readonly Heightmap.Biome[] Gateable = WitherConfig.GateableBiomes;

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

            LogRoster();

            WitherPlugin.Log.LogInfo("Gate table:");
            foreach (Heightmap.Biome biome in Gateable)
            {
                string key = WitherConfig.RequiredKeyFor(biome);
                if (key == null)
                {
                    WitherPlugin.Log.LogInfo("  " + biome + ": ungated");
                    continue;
                }

                if (!WitherConfig.GateOnGroup.Value)
                {
                    // "not set" is the interesting case and it is ambiguous by nature: either
                    // the boss is alive, or the key name is wrong. Say so rather than
                    // reporting a typo as a closed gate.
                    WitherPlugin.Log.LogInfo("  " + biome + ": needs '" + key + "' - "
                        + (zone.GetGlobalKey(key)
                            ? "set, biome is open"
                            : "NOT set, biome withers"));
                    continue;
                }

                // The verdict comes from GroupHasKey and the names from BlockersFor, and they
                // are not interchangeable: with an empty roster BlockersFor has nobody to
                // report and returns null, while GroupHasKey falls back to the world key and
                // may well be shut. Reading "no names" as "open" would print the opposite of
                // the truth in exactly the situation that needs diagnosing.
                bool open = Progression.GroupHasKey(key);
                string blockedBy = open ? null : Progression.BlockersFor(key);

                // Say when it is open because it was latched, not because the current roster
                // all have it. Otherwise a biome that stays open while someone on the roster
                // plainly has not done the boss reads as a bug.
                string why = open
                    ? (Progression.IsLatchedOpen(key)
                        ? "cleared by the group, open for good"
                        : "whole group has it, biome is open")
                    : "biome withers" + (blockedBy == null
                        ? " (roster empty; falling back to the world key)"
                        : ", still owed by " + blockedBy);

                WitherPlugin.Log.LogInfo("  " + biome + ": needs '" + key + "' - " + why);
            }
        }

        /// <summary>
        /// Who is on the roster and how stale each of them is.
        ///
        /// The single most useful line in the log once the group gate is on, because every
        /// confusing outcome it can produce - a gate that will not open, or one that opened
        /// without somebody - is a question about this list and nothing else.
        /// </summary>
        private static void LogRoster()
        {
            if (!WitherConfig.GateOnGroup.Value)
            {
                WitherPlugin.Log.LogInfo("Gating on world keys; the group roster is not used.");
                return;
            }

            List<Progression.RosterEntry> roster = Progression.Roster();
            if (roster.Count == 0)
            {
                WitherPlugin.Log.LogInfo(
                    "Roster is empty - falling back to world keys until somebody publishes. "
                    + "Expect this only on the first spawn after installing.");
                return;
            }

            WitherPlugin.Log.LogInfo("Roster (" + roster.Count + " within "
                + WitherConfig.RosterDays.Value + " days):");

            foreach (Progression.RosterEntry member in roster)
                WitherPlugin.Log.LogInfo("  " + member.Name + " (id " + member.Id + ")");
        }
    }
}

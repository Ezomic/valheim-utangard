using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace Utangard
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

        public static void Log()
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null)
            {
                UtangardPlugin.Log.LogWarning("No ZoneSystem yet - nothing to report.");
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

            UtangardPlugin.Log.LogInfo("World global keys (" + sorted.Count + "): "
                + (sorted.Count == 0 ? "(none)" : line.ToString()));

            LogRoster();

            UtangardPlugin.Log.LogInfo("Gate table:");
            foreach (GateReport.Row row in GateReport.Rows())
            {
                // The same rows the compendium page renders. The log used to compute its own
                // verdicts, which meant two pieces of code deciding what "open" means about
                // one world - and the interesting cases here are the ambiguous ones, where a
                // difference between the two would read as a bug in whichever one the player
                // happened to be looking at.
                UtangardPlugin.Log.LogInfo("  " + row.Biome
                    + (row.Key == null ? ": " : ": needs '" + row.Key + "' - ")
                    + GateReport.Verdict(row));
            }

            DefeatKeys.Report();
        }

        /// <summary>
        /// A deadline in words. Days once there is more than one left, hours below that -
        /// "0.3 days" is not a thing anyone reads as urgency.
        /// </summary>
        internal static string Describe(long seconds)
        {
            if (seconds >= 172800L) return (seconds / 86400L) + " days";
            if (seconds >= 7200L) return (seconds / 3600L) + " hours";
            if (seconds >= 120L) return (seconds / 60L) + " minutes";
            return seconds + " seconds";
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
            if (!UtangardConfig.GateOnGroup.Value)
            {
                UtangardPlugin.Log.LogInfo("Gating on world keys; the group roster is not used.");
                return;
            }

            List<Progression.RosterEntry> roster = Progression.Roster();
            if (roster.Count == 0)
            {
                UtangardPlugin.Log.LogInfo(
                    "Roster is empty - falling back to world keys until somebody publishes. "
                    + "Expect this only on the first spawn after installing.");
                return;
            }

            UtangardPlugin.Log.LogInfo("Roster (" + roster.Count + " within "
                + UtangardConfig.RosterDays.Value + " days):");

            foreach (Progression.RosterEntry member in roster)
                UtangardPlugin.Log.LogInfo("  " + member.Name + " (id " + member.Id + ")");
        }
    }
}

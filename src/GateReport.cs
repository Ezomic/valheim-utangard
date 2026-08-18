using System.Collections.Generic;
using System.Text;

namespace Utangard
{
    /// <summary>
    /// The state of the gate, in words, once - for the log on spawn and for the compendium
    /// page, which are the same nine questions asked by two different readers.
    ///
    /// They were one function that logged, and the page was going to be a second copy of it
    /// with colours. Two copies of "is this biome open, and if not who owes it" is the sort
    /// of duplication that stays right for a week: the interesting part is not the wording
    /// but the three-way distinction between open-because-latched, open-because-everyone-has-
    /// it, and shut-with-an-empty-roster, and getting that subtly different in two places
    /// would make the log and the panel disagree about the same world.
    /// </summary>
    internal static class GateReport
    {
        /// <summary>One biome's answer, computed once and rendered by whoever asked.</summary>
        internal struct Row
        {
            /// <summary>The biome this row is about.</summary>
            public Heightmap.Biome Biome;

            /// <summary>The key it demands, or null when the row is blank and it is ungated.</summary>
            public string Key;

            /// <summary>Whether the gate is currently open here.</summary>
            public bool Open;

            /// <summary>Open because it was latched, rather than because the roster all have it.</summary>
            public bool Latched;

            /// <summary>Who still owes it, or null when nobody does or nobody is known.</summary>
            public string BlockedBy;

            /// <summary>Seconds left on the catch-up deadline, or -1 when no clock runs.</summary>
            public long SecondsLeft;

            /// <summary>
            /// Shut with nothing on the roster, which is the fallback to the world key rather
            /// than a verdict about people. Reading "no names owed" as "open" prints the exact
            /// opposite of the truth in the one situation that needs diagnosing, so it is
            /// carried as its own fact.
            /// </summary>
            public bool RosterEmpty;
        }

        /// <summary>Every gateable biome, in progression order, with its verdict.</summary>
        public static List<Row> Rows()
        {
            var rows = new List<Row>();

            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return rows;

            foreach (Heightmap.Biome biome in UtangardConfig.GateableBiomes)
            {
                var row = new Row { Biome = biome, SecondsLeft = -1L };
                row.Key = UtangardConfig.RequiredKeyFor(biome);

                if (row.Key == null)
                {
                    row.Open = true;
                    rows.Add(row);
                    continue;
                }

                if (!UtangardConfig.GateOnGroup.Value)
                {
                    row.Open = zone.GetGlobalKey(row.Key);
                    rows.Add(row);
                    continue;
                }

                row.Open = Progression.GroupHasKey(row.Key);
                row.Latched = row.Open && Progression.IsLatchedOpen(row.Key);
                row.BlockedBy = row.Open ? null : Progression.BlockersFor(row.Key);
                row.RosterEmpty = !row.Open && row.BlockedBy == null;
                row.SecondsLeft = Progression.SecondsLeft(row.Key);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// The verdict as a sentence, without the biome's name in front of it. Plain text -
        /// the log has no use for markup and the page adds its own around this.
        /// </summary>
        public static string Verdict(Row row)
        {
            if (row.Key == null) return "ungated";

            if (!UtangardConfig.GateOnGroup.Value)
                return row.Open ? "the world has it, biome is open" : "the world has not; biome withers";

            if (row.Open)
                return row.Latched
                    ? "cleared by the group, open for good"
                    : "whole group has it, biome is open";

            if (row.RosterEmpty)
                return "biome withers (roster empty; falling back to the world key)";

            string clock = row.SecondsLeft < 0L
                ? ""
                : " - " + GlobalKeyDump.Describe(row.SecondsLeft) + " left to catch up";

            return "biome withers, still owed by " + row.BlockedBy + clock;
        }

        /// <summary>
        /// The whole thing as one compendium page.
        ///
        /// Vanilla's own text pages are the model: a coloured heading per block and plain
        /// prose under it, built with the same &lt;color&gt; tags TextsDialog.AddActiveEffects
        /// uses, so the page cannot look like it came from somewhere else.
        ///
        /// It says what the mod is doing before it says what is shut, because the reader who
        /// most needs this page is the one who does not yet know why their food vanished.
        /// </summary>
        public static string Page()
        {
            var text = new StringBuilder(512);

            if (!UtangardConfig.Enabled.Value)
                return "Utangard is switched off. Every biome behaves as vanilla.";

            text.Append("A biome your group has not earned will not feed you. "
                + "Food burns faster there, nothing you eat or drink takes hold, "
                + "and you leave Sapped.\n\n");

            List<Row> rows = Rows();
            if (rows.Count == 0)
                return text.Append("No world loaded yet.").ToString();

            foreach (Row row in rows)
            {
                text.Append("<color=orange>").Append(BiomeName(row.Biome)).Append("</color>  ");

                if (row.Key == null)
                {
                    text.Append("ungated\n");
                    continue;
                }

                text.Append(row.Open
                    ? "<color=#7ec27e>open</color>"
                    : "<color=#c27e7e>withers you</color>");

                if (row.Open)
                {
                    text.Append(row.Latched ? " - cleared by the group\n" : "\n");
                    continue;
                }

                text.Append("\n  needs ").Append(row.Key).Append('\n');

                if (row.RosterEmpty)
                {
                    text.Append("  nobody has published progress here yet\n");
                }
                else if (!string.IsNullOrEmpty(row.BlockedBy))
                {
                    text.Append("  still owed by ").Append(row.BlockedBy).Append('\n');

                    // The deadline belongs next to the names for the same reason it is in the
                    // entry message: "who" and "how long" are one question to somebody
                    // deciding whether to go and fetch a friend.
                    if (row.SecondsLeft >= 0L)
                        text.Append("  opens anyway in ")
                            .Append(GlobalKeyDump.Describe(row.SecondsLeft))
                            .Append('\n');
                }
            }

            AppendRoster(text);

            return text.ToString();
        }

        /// <summary>
        /// Who "everyone" currently means.
        ///
        /// On the page rather than only in the log because every confusing thing the group
        /// gate can do - a gate that will not open, or one that opened without somebody - is
        /// a question about this list, and a player cannot read a log file mid-raid.
        /// </summary>
        private static void AppendRoster(StringBuilder text)
        {
            if (!UtangardConfig.GateOnGroup.Value)
            {
                text.Append("\n<color=orange>Gate</color>\n")
                    .Append("Gating on the world's own keys. One kill opens a biome for "
                        + "everybody, and the roster is not used.\n");
                return;
            }

            List<Progression.RosterEntry> roster = Progression.Roster();

            text.Append("\n<color=orange>The group</color>\n");

            if (roster.Count == 0)
            {
                text.Append("Nobody has published progress in this world yet. Expect this "
                    + "only on the first spawn after installing.\n");
                return;
            }

            text.Append(roster.Count).Append(" seen in the last ")
                .Append((int)UtangardConfig.RosterDays.Value).Append(" days: ");

            for (int i = 0; i < roster.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(roster[i].Name);
            }

            text.Append("\nA character stops counting once it has not played for that long, "
                + "which is how the gate forgets somebody who has stopped playing.\n");
        }

        /// <summary>
        /// The biome's name as the game writes it.
        ///
        /// $biome_swamp and friends are vanilla's own tokens - Player.AddKnownBiome builds
        /// them the same way - so the page reads in the player's language for free and
        /// matches the name the game used when they discovered the place.
        /// </summary>
        public static string BiomeName(Heightmap.Biome biome)
        {
            string token = "$biome_" + biome.ToString().ToLowerInvariant();

            Localization loc = Localization.instance;
            if (loc == null) return biome.ToString();

            string name = loc.Localize(token);

            // An unresolved token comes back as the raw word rather than as anything a player
            // would want to read, so fall back to the enum name, which at least is English.
            return string.IsNullOrEmpty(name) || name == token ? biome.ToString() : name;
        }
    }
}

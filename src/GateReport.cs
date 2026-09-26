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

            // Said before anything else, because the rest of this page describes gates that
            // are not currently being enforced and a reader has no other way to know that.
            // The rows below stay honest about what is shut - the mod has simply stopped
            // acting on it, rather than pretending the world has changed.
            if (!Seams.PenaltyIsEscapable())
                text.Append("<color=#c27e7e>Utangard is not enforcing anything right now.</color>\n"
                    + "A game update has moved the code that records boss kills, so a biome "
                    + "shut here could never open again. Nothing below is withering you until "
                    + "the mod is updated. The gate is described anyway, because it is still "
                    + "what the world thinks.\n\n");

            text.Append("A biome your group has not earned will not feed you. "
                + "Food burns faster there, nothing you eat or drink takes hold, "
                + "and you leave Sapped.");

            // Said here so the page stays true now that eating can be earned back, one biome and
            // one character at a time. The bars themselves are `utangard foothold`.
            if (UtangardConfig.FootholdEnabled.Value)
                text.Append(" Fighting there earns eating back, and fighting and exploring "
                    + "together earn healing back, for you and only in that biome.");

            text.Append("\n\n");

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

                // The boss's name, not the global key. 'needs defeated_gdking' is a
                // sentence about the implementation; the player is waiting on The Elder. The
                // key is still what the log prints, because there the string that can be
                // wrong is the whole point.
                text.Append("\n  needs ").Append(BossName(row.Key)).Append('\n');

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
        /// The name of whatever sets this key, falling back to the key itself.
        ///
        /// The fallback is not a formality: a row may point at another mod's key, or at one a
        /// location sets rather than a creature, and printing nothing there would leave the
        /// panel saying a biome is shut for no stated reason.
        /// </summary>
        internal static string BossName(string key)
        {
            string name = DefeatKeys.NameFor(key);
            return string.IsNullOrEmpty(name) ? key : name;
        }

        /// <summary>The row for one biome, or a blank ungated one when there is no world.</summary>
        internal static Row RowFor(Heightmap.Biome biome)
        {
            foreach (Row row in Rows())
                if (row.Biome == biome) return row;

            return new Row { Biome = biome, Open = true, SecondsLeft = -1L };
        }

        /// <summary>
        /// One sentence on who a biome is waiting on and until when, or that it is open, for the
        /// line under a biome's title in the compendium panel (LHM-26, panel C). Built from the
        /// same Row the page and the log read, so the three cannot disagree.
        ///
        /// "You" goes first when the local character owes it too, because that is the part of
        /// the sentence a player can do something about tonight. Names come out of the world's
        /// global keys, which the game lowercases, so they read in lower case; underscores are
        /// turned back into the spaces Progression swapped out.
        /// </summary>
        internal static string WaitingLine(Row row)
        {
            if (row.Key == null) return "This biome is never locked.";

            string boss = BossName(row.Key);

            if (!UtangardConfig.GateOnGroup.Value)
                return row.Open
                    ? "Open. " + boss + " has died in this world."
                    : "Waiting on " + boss + ". One kill opens it for everybody.";

            if (row.Open)
                return row.Latched
                    ? "Open for good. The group has cleared " + boss + "."
                    : "Open. Everyone has " + boss + ".";

            if (row.RosterEmpty)
                return "Waiting on " + boss + ". Nobody has published progress here yet.";

            var names = new List<string>();

            // Two reads of one list, with and without the local character. When they differ the
            // local character is on it, and saying "you" beats hoping they recognise their own
            // name in lower case.
            string everyone = Progression.BlockersFor(row.Key);
            string others = Progression.BlockersFor(row.Key, excludeSelf: true);
            if (everyone != others) names.Add("you");

            if (!string.IsNullOrEmpty(others))
                foreach (string name in others.Split(new[] { ", " }, System.StringSplitOptions.RemoveEmptyEntries))
                    names.Add(name.Replace('_', ' '));

            var text = new StringBuilder("Waiting on ").Append(boss);
            if (names.Count > 0) text.Append(" from ").Append(JoinAnd(names));
            text.Append('.');

            if (row.SecondsLeft >= 0L)
                text.Append(" Opens anyway in ").Append(Span(row.SecondsLeft)).Append('.');

            return text.ToString();
        }

        /// <summary>
        /// What a locked biome does to you, built from the rules actually in force, for the last
        /// line of the compendium panel. So on Longhouse it says food burns 3x faster and wounds
        /// heal at a fifth, rather than the defaults. Each rule that is switched off drops out.
        ///
        /// Eating is left out on purpose: the Fighting box above it says whether you may eat.
        /// </summary>
        internal static string RulesLine()
        {
            var parts = new List<string>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            float food = UtangardConfig.FoodDrainMultiplier.Value;
            if (food > 1f) parts.Add("Food burns " + food.ToString("0.#", inv) + "x faster");

            if (UtangardConfig.BlockNewBuffs.Value)
                parts.Add(UtangardConfig.BlockRested.Value ? "no meads, powers or Rested" : "no meads or powers");

            float regen = UnityEngine.Mathf.Clamp01(UtangardConfig.HealthRegenMultiplier.Value);
            if (regen < 1f) parts.Add(regen <= 0f ? "wounds do not heal" : "wounds heal at " + Fraction(regen));

            if (UtangardConfig.SappedStaminaRegen.Value < 1f && UtangardConfig.SappedMaxSeconds.Value > 0f)
                parts.Add("you leave Sapped");

            if (parts.Count == 0) return "";

            // Capitalised whichever rule ends up first.
            string line = string.Join(" · ", parts.ToArray());
            return char.ToUpperInvariant(line[0]) + line.Substring(1);
        }

        /// <summary>"a fifth" for 0.2, and the like; a percentage when there is no plain word.</summary>
        private static string Fraction(float value)
        {
            if (UnityEngine.Mathf.Approximately(value, 0.5f)) return "half speed";
            if (UnityEngine.Mathf.Approximately(value, 0.25f)) return "a quarter";
            if (UnityEngine.Mathf.Approximately(value, 0.2f)) return "a fifth";
            if (UnityEngine.Mathf.Approximately(value, 0.1f)) return "a tenth";
            if (UnityEngine.Mathf.Abs(value - 1f / 3f) < 0.005f) return "a third";

            return (value * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% of the normal rate";
        }

        /// <summary>"1 day 4 hours", "5 hours 10 minutes", "12 minutes".</summary>
        internal static string Span(long seconds)
        {
            long days = seconds / 86400L;
            long hours = seconds % 86400L / 3600L;
            long minutes = seconds % 3600L / 60L;

            if (days > 0L) return Count(days, "day") + (hours > 0L ? " " + Count(hours, "hour") : "");
            if (hours > 0L) return Count(hours, "hour") + (minutes > 0L ? " " + Count(minutes, "minute") : "");
            if (minutes > 0L) return Count(minutes, "minute");
            return "less than a minute";
        }

        private static string Count(long n, string unit)
        {
            return n + " " + unit + (n == 1L ? "" : "s");
        }

        private static string JoinAnd(List<string> items)
        {
            if (items.Count == 1) return items[0];

            return string.Join(", ", items.GetRange(0, items.Count - 1).ToArray())
                + " and " + items[items.Count - 1];
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

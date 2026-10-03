using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// Every active player's two bars per biome, published so any client can read them. LHM-61.
    ///
    /// <b>Why anything is published.</b> A character's bars are private to the machine that earns
    /// them: the kill tally lives in the character file and Discovery is read from the character's
    /// own map, neither of which another client can see. The compendium's player tabs need both,
    /// so each client writes its own character's numbers into the world, where every client reads
    /// them back. Percentages only, never a position: the map count is reduced to one number per
    /// biome before it leaves the machine.
    ///
    /// <b>One key per character, not one per biome and bar.</b> Every global key the server
    /// accepts is followed by SendGlobalKeys(Everybody), the whole key list sent to every player,
    /// and every key a client receives is also written into that player's saved profile as
    /// "key value" (m_knownWorldKeys). So the cost is the size of the whole list, paid per write,
    /// and one profile entry per distinct value ever written. Sixteen keys per character would
    /// put 160 keys of about 30 bytes into every broadcast for ten players, 4.8 KB each time;
    /// one packed key of about 80 bytes is 0.8 KB for the same ten. See the README for the sums.
    ///
    /// <b>The value.</b> `utangard_f_&lt;character id&gt;` is `1.&lt;minutes&gt;.&lt;slots&gt;`: a version, the
    /// time of the write in unix minutes (for "updated 2 minutes ago", and since the key is only
    /// written when a bar moved, it is also when one last did), and one slot per entry of
    /// UtangardConfig.GateableBiomes, in that order. A slot is six characters, Fighting then
    /// Discovery, each a three digit percent. `---` is not published (the biome is open, so the
    /// bars mean nothing) and `???` is a bar the publisher's game could not read. Decimal and
    /// padded so a key read in the world dump says what it holds.
    ///
    /// <b>Written only when it changes, and not more than once in MinWriteSeconds.</b> Whole
    /// percents, as the ticket asked, would still be a write every few dozen metres of walking on
    /// new ground, each one a broadcast to every player and a new string in every profile. The
    /// floor turns that into at most two a minute per player, and the last number is never lost,
    /// only late: the next pass after the floor writes it. The values are also rounded down to
    /// UtangardConfig.PublishStep (5 by default, a recommendation Robbin has not chosen), which
    /// is what cuts the number of distinct strings, and so the profile growth, to a fifth.
    ///
    /// <b>Changed means changed by this client, not changed by anyone.</b> The comparison is with
    /// the world's value, so two live writers for one character id (a copied character file, or a
    /// hostile client) would each see the other's value as a change and rewrite every 30 seconds
    /// for ever, each pass adding a string to every client's three dictionaries. So the last
    /// slots this client wrote are remembered: a world that disagrees with them has been
    /// overwritten by someone else, and that is answered at most once in ContestedWriteSeconds. A
    /// missing key is not a dispute and is rewritten at the normal floor. The time in the value is
    /// rounded to MinutesBucket for the same reason: a rewrite of identical bars inside a bucket
    /// is the identical string, which the server's own exact-match check then drops.
    ///
    /// <b>Only locked biomes are published.</b> The bars only mean something where the lock is,
    /// and the Fighting tally keeps counting in an open biome, so publishing those would write on
    /// every kill in the Meadows for a number no page shows.
    ///
    /// <b>Not held to be true.</b> Like the rest of the mod this is a rule for a group running the
    /// same plugins: any client can write any key, and nothing proves a number is honest. It only
    /// feeds a page that tells players how far their friends have got.
    /// </summary>
    internal static class PlayerBars
    {
        private const string KeyPrefix = "utangard_f_";

        private const string Version = "1";

        private const string NotPublished = "---";
        private const string Unreadable = "???";

        /// <summary>Characters in one slot: three for Fighting, three for Discovery.</summary>
        private const int SlotLength = 6;

        /// <summary>The fewest real seconds between two writes by one client.</summary>
        internal const float MinWriteSeconds = 30f;

        /// <summary>The fewest between two writes of bars this client already wrote once, see Publish.</summary>
        internal const float ContestedWriteSeconds = 300f;

        /// <summary>The write time in the value is rounded down to this many minutes.</summary>
        private const long MinutesBucket = 10L;

        /// <summary>How far ahead of this clock a published time may be before it is not believed.</summary>
        private const long FutureMinutes = 24L * 60L;

        /// <summary>A slot that was not published: the biome was open, or the key is missing.</summary>
        internal const int None = -1;

        /// <summary>A slot whose bar the publisher could not read.</summary>
        internal const int Unknown = -2;

        /// <summary>The most tabs the panel can draw. See CompendiumPanel.</summary>
        internal const int MostTabs = 9;

        private static float _wroteAt = float.NegativeInfinity;
        private static Player _publisher;
        private static ZoneSystem _zone;

        /// <summary>The slots string this client last wrote into this world, or null.</summary>
        private static string _lastSlots;

        // ---------------------------------------------------------------- the numbers ---

        /// <summary>One character's bars, a slot per biome in GateableBiomes order.</summary>
        internal sealed class Bars
        {
            /// <summary>Unix minutes of the write, or 0 for the local character's live read.</summary>
            public long Minutes;

            /// <summary>Percent 0 to 100, or None or Unknown.</summary>
            public int[] Fighting;

            public int[] Discovery;
        }

        private static int Slots()
        {
            return UtangardConfig.GateableBiomes.Length;
        }

        internal static int SlotOf(Heightmap.Biome biome)
        {
            return Array.IndexOf(UtangardConfig.GateableBiomes, biome);
        }

        private static bool Locked(ZoneSystem zone, Heightmap.Biome biome)
        {
            string key = UtangardConfig.RequiredKeyFor(biome);
            return key != null && !BiomeGate.Earned(zone, key);
        }

        /// <summary>
        /// The local character's bars right now, read the way Foothold.Read reads them. Locked
        /// biomes only; an open one is None.
        /// </summary>
        internal static Bars Own()
        {
            int n = Slots();
            var bars = new Bars { Fighting = new int[n], Discovery = new int[n] };

            ZoneSystem zone = ZoneSystem.instance;
            bool counting = Discovery.Available() && Discovery.Counting();
            for (int i = 0; i < n; i++)
            {
                Heightmap.Biome biome = UtangardConfig.GateableBiomes[i];
                if (zone == null || !Locked(zone, biome))
                {
                    bars.Fighting[i] = None;
                    bars.Discovery[i] = None;
                    continue;
                }

                int fighting = Foothold.FightingPercentIn(biome);

                // A count still running, or one that belongs to another world's map, has no number
                // yet. Unknown, never the 0 that Discovery.Pixels answers with.
                int discovery = counting ? -1 : Foothold.DiscoveryPercentIn(biome);
                bars.Fighting[i] = fighting < 0 ? Unknown : fighting;
                bars.Discovery[i] = discovery < 0 ? Unknown : discovery;
            }

            return bars;
        }

        // ---------------------------------------------------------------- publishing ---

        /// <summary>
        /// Write the local character's bars into the world if they differ from what the world
        /// holds. Called from the tick every few seconds; a pass that finds nothing changed costs
        /// a handful of lookups and one string compare, and sends nothing.
        /// </summary>
        internal static void Publish(Player player)
        {
            if (!UtangardConfig.Enabled.Value || !UtangardConfig.FootholdEnabled.Value) return;

            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || player == null) return;

            long id = player.GetPlayerID();
            if (id == 0L) return;

            if (!ReferenceEquals(_publisher, player) || !ReferenceEquals(_zone, zone))
            {
                _publisher = player;
                _zone = zone;
                _wroteAt = float.NegativeInfinity;
                _lastSlots = null;
            }

            string key = KeyPrefix + id;
            string current;
            bool held = zone.GetGlobalKey(key, out current);

            // The map count of a world runs for a few seconds after it loads, and until it is done
            // every Discovery number is a floor still rising, or another world's map read as 0.
            // Where the world already holds a value for this character it stays as it is until the
            // count is done. Where it holds none, a value goes in with Discovery as unknown, so a
            // tab is not left saying "older build" through the count.
            if (held && Discovery.Available() && Discovery.Counting()) return;

            string slots = Pack(Own());
            if (held && SlotsOf(current) == slots) return;

            bool contested = held && _lastSlots == slots;
            float floor = contested ? ContestedWriteSeconds : MinWriteSeconds;
            if (Time.realtimeSinceStartup - _wroteAt < floor) return;

            _wroteAt = Time.realtimeSinceStartup;
            _lastSlots = slots;

            long minutes = Progression.Now() / 60L;
            minutes -= minutes % MinutesBucket;
            zone.SetGlobalKey(key + " " + Version + "." + minutes.ToString(CultureInfo.InvariantCulture)
                + "." + slots);

            if (UtangardConfig.Verbose.Value)
                UtangardPlugin.Log.LogInfo("Published " + player.GetPlayerName() + "'s foothold bars: " + slots);
        }

        private static string Pack(Bars bars)
        {
            var text = new StringBuilder(Slots() * SlotLength);
            for (int i = 0; i < Slots(); i++)
                text.Append(Slot(bars.Fighting[i])).Append(Slot(bars.Discovery[i]));

            return text.ToString();
        }

        private static string Slot(int percent)
        {
            if (percent == None) return NotPublished;
            if (percent < 0) return Unreadable;

            // Rounded down to the step, but a full bar stays 100: at a step of 30 the top of the
            // scale would otherwise read 90 for ever.
            int step = Math.Max(1, Math.Min(100, UtangardConfig.PublishStep.Value));
            int shown = percent >= 100 ? 100 : percent - percent % step;
            return shown.ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>The slots of a stored value, or null when it is not one this build wrote.</summary>
        private static string SlotsOf(string value)
        {
            int minutes = value == null ? -1 : value.IndexOf('.');
            int slots = minutes < 0 ? -1 : value.IndexOf('.', minutes + 1);
            return slots < 0 ? null : value.Substring(slots + 1);
        }

        // ------------------------------------------------------------------- reading ---

        /// <summary>
        /// Another character's published bars, or null when the world holds none for it or what
        /// it holds is not a shape this build knows, which reads as "unknown", never as zero.
        /// </summary>
        internal static Bars Read(long id)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return null;

            string value;
            if (!zone.GetGlobalKey(KeyPrefix + id, out value)) return null;

            return Parse(value);
        }

        private static Bars Parse(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(Version + ".", StringComparison.Ordinal)) return null;

            int first = value.IndexOf('.');
            int second = value.IndexOf('.', first + 1);
            if (second < 0) return null;

            // No sign, no spaces, and a time that is neither before 1970 nor a day past this clock.
            // Anyone can write any key, and a number out of range is treated as no data rather than
            // trusted into the "updated N minutes ago" sum.
            long minutes;
            if (!long.TryParse(value.Substring(first + 1, second - first - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out minutes)) return null;
            if (minutes < 0L || minutes > Progression.Now() / 60L + FutureMinutes) return null;

            string slots = value.Substring(second + 1);
            int n = Slots();
            if (slots.Length != n * SlotLength) return null;

            var bars = new Bars { Minutes = minutes, Fighting = new int[n], Discovery = new int[n] };
            for (int i = 0; i < n; i++)
            {
                if (!Unpack(slots.Substring(i * SlotLength, 3), out bars.Fighting[i])) return null;
                if (!Unpack(slots.Substring(i * SlotLength + 3, 3), out bars.Discovery[i])) return null;
            }

            return bars;
        }

        private static bool Unpack(string slot, out int percent)
        {
            percent = None;
            if (slot == NotPublished) return true;

            if (slot == Unreadable)
            {
                percent = Unknown;
                return true;
            }

            return int.TryParse(slot, NumberStyles.None, CultureInfo.InvariantCulture, out percent)
                && percent >= 0 && percent <= 100;
        }

        // ---------------------------------------------------------------- the players ---

        /// <summary>One tab: a character the compendium can show.</summary>
        internal sealed class Member
        {
            public long Id;

            /// <summary>As a person would write it. Never carries markup.</summary>
            public string Name;

            /// <summary>The name as the heartbeat key stores it, for matching the online list.</summary>
            public string NameKey;

            public bool You;
            public bool Online;

            /// <summary>Whole days since the character was last seen playing, 0 for today.</summary>
            public long DaysAgo;

            /// <summary>
            /// Their bars. Null for a character that has played with Utangard but never published,
            /// which is an older build, and which the page shows as unknown.
            /// </summary>
            public Bars Bars;
        }

        /// <summary>
        /// The days a character stays listed: the longest window of any boss the gate waits on,
        /// since a character the gate still counts for one boss is one the page should show. With
        /// no gate row at all, RosterDays.
        /// </summary>
        internal static long WindowDays()
        {
            float most = 0f;
            bool any = false;
            foreach (string key in UtangardConfig.AllGateKeys())
            {
                any = true;
                most = Math.Max(most, UtangardConfig.RosterDaysFor(key));
            }

            return Math.Max(1L, (long)(any ? most : UtangardConfig.RosterDays.Value));
        }

        /// <summary>
        /// The tabs, in the order they are drawn: you first, then who is online, then the rest by
        /// how recently they were seen, and anyone with no data last.
        ///
        /// Everyone the heartbeat has seen within WindowDays, which is the window the group gate
        /// uses for the boss with the longest one (RosterDaysFor), so a tab is dropped by the same
        /// rule that stops a player holding a biome shut. Online is read from the server's player
        /// list by name, since the heartbeat key carries the character's name and nothing public
        /// maps a name to an id.
        ///
        /// Two characters with one name cannot be told apart by that list. Rather than show both
        /// online when one is, the list's count for the name says how many of them are, and the
        /// seats go to the ones seen most recently, then to the one whose bars were written last.
        /// Your own character is taken out of that count first, since it is in the list too.
        /// </summary>
        internal static List<Member> Members()
        {
            var members = new List<Member>();

            Player me = Player.m_localPlayer;
            long myId = me != null ? me.GetPlayerID() : 0L;

            Dictionary<string, int> online = OnlineNames(me);
            long today = Progression.Today();
            long window = WindowDays();
            var byName = new Dictionary<string, List<Member>>(StringComparer.Ordinal);

            foreach (Progression.RosterEntry entry in Progression.Roster())
            {
                if (entry.Id == myId || entry.Id == 0L) continue;

                long ago = Math.Max(0L, today - entry.LastSeenDay);
                if (ago > window) continue;

                var member = new Member
                {
                    Id = entry.Id,
                    Name = Plain(GateReport.DisplayName(entry.Name)),
                    NameKey = Normal(entry.Name),
                    DaysAgo = ago,
                    Bars = Read(entry.Id),
                };

                members.Add(member);

                List<Member> same;
                if (!byName.TryGetValue(member.NameKey, out same))
                    byName[member.NameKey] = same = new List<Member>();
                same.Add(member);
            }

            foreach (KeyValuePair<string, List<Member>> pair in byName)
            {
                int seats;
                if (!online.TryGetValue(pair.Key, out seats) || seats <= 0) continue;

                pair.Value.Sort(MoreRecent);
                for (int i = 0; i < pair.Value.Count && i < seats; i++) pair.Value[i].Online = true;
            }

            members.Sort(Order);

            if (me != null && myId != 0L)
            {
                members.Insert(0, new Member
                {
                    Id = myId,
                    Name = Plain(me.GetPlayerName()),
                    You = true,
                    Online = true,
                    Bars = Own(),
                });
            }

            return members;
        }

        private static int MoreRecent(Member a, Member b)
        {
            int days = a.DaysAgo.CompareTo(b.DaysAgo);
            if (days != 0) return days;

            return (b.Bars != null ? b.Bars.Minutes : 0L).CompareTo(a.Bars != null ? a.Bars.Minutes : 0L);
        }

        private static int Order(Member a, Member b)
        {
            int rank = Rank(a).CompareTo(Rank(b));
            if (rank != 0) return rank;

            int days = a.DaysAgo.CompareTo(b.DaysAgo);
            if (days != 0) return days;

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static int Rank(Member member)
        {
            if (member.Online) return 0;
            return member.Bars != null ? 1 : 2;
        }

        /// <summary>How many connected players carry each name, not counting this client's own.</summary>
        private static Dictionary<string, int> OnlineNames(Player me)
        {
            var names = new Dictionary<string, int>(StringComparer.Ordinal);

            ZNet net = ZNet.instance;
            if (net == null) return names;

            foreach (ZNet.PlayerInfo info in net.GetPlayerList())
            {
                if (string.IsNullOrEmpty(info.m_name)) continue;

                string name = Normal(info.m_name);
                int count;
                names.TryGetValue(name, out count);
                names[name] = count + 1;
            }

            if (me != null)
            {
                string mine = Normal(me.GetPlayerName());
                int count;
                if (names.TryGetValue(mine, out count)) names[mine] = count - 1;
            }

            return names;
        }

        /// <summary>
        /// A name the way the heartbeat key stores it. Progression.Sanitise is what writes that
        /// key, so reusing it is what keeps a name with a space or the separator in it matching
        /// the online list; the key is lowercased by the game, hence the ToLowerInvariant.
        /// </summary>
        private static string Normal(string name)
        {
            return Progression.Sanitise(name).ToLowerInvariant();
        }

        /// <summary>A name with the two characters TextMeshPro would read as markup taken out.</summary>
        internal static string Plain(string name)
        {
            return string.IsNullOrEmpty(name) ? "unnamed" : name.Replace("<", "").Replace(">", "");
        }

        /// <summary>
        /// A tab's second line: "You, online", "Online", "Seen today", "Seen 3 days ago".
        /// </summary>
        internal static string Status(Member member)
        {
            if (member.You)
                return Discovery.Available() && Discovery.Counting() ? "You, reading your map..." : "You, online";
            if (member.Online) return "Online";
            if (member.DaysAgo <= 0L) return "Seen today";

            return "Seen " + member.DaysAgo + (member.DaysAgo == 1L ? " day ago" : " days ago");
        }

        // ----------------------------------------------------------------- the console ---

        /// <summary>
        /// One line per tab for `utangard players`: who, how they were last seen, and the bars of
        /// every locked biome they have published, in the page's own percents. A scenario reads the
        /// line, and so can a person asking why a tab says it has no data.
        /// </summary>
        internal static string Describe(Member member)
        {
            var text = new StringBuilder();
            text.Append(member.Name).Append(" (")
                .Append(member.You ? "you" : member.Online ? "online" : "seen " + member.DaysAgo + "d ago")
                .Append("): ");

            if (member.Bars == null)
                return text.Append("older build, no data").ToString();

            text.Append("data");

            for (int i = 0; i < Slots(); i++)
            {
                int fighting = member.Bars.Fighting[i];
                int discovery = member.Bars.Discovery[i];
                if (fighting == None && discovery == None) continue;

                text.Append(", ").Append(UtangardConfig.GateableBiomes[i])
                    .Append(" fighting ").Append(Shown(fighting))
                    .Append(" discovery ").Append(Shown(discovery));
            }

            return text.ToString();
        }

        private static string Shown(int percent)
        {
            return percent < 0 ? "?" : percent.ToString(CultureInfo.InvariantCulture) + "%";
        }
    }
}

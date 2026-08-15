using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Wither
{
    /// <summary>
    /// Who in the group has actually done which boss.
    ///
    /// The world's global key answers "has this boss died in this world", which is not the
    /// question. A player who joined after the fight, or who was asleep at home while three
    /// others killed it, gets the same credit as the people who were there. This gates on the
    /// weakest link instead: the biome opens when *everyone* has done it.
    ///
    /// Almost all of this is vanilla's work. In Character.OnDeath:
    ///
    /// <code>
    ///     if (!string.IsNullOrEmpty(m_defeatSetGlobalKey))
    ///         Player.m_addUniqueKeyQueue.Add(m_defeatSetGlobalKey);
    ///     if ((bool)m_nview &amp;&amp; !m_nview.IsOwner()) return;
    /// </code>
    ///
    /// That push happens *before* the ownership early-return, so it runs on every client that
    /// had the boss loaded when it died - which is exactly "everyone who was there". It lands
    /// in Player.m_uniques, which Player.Save writes and Player.Load reads back. So the game
    /// has been recording per-character boss attendance all along, and characters that predate
    /// this mod are already filled in. Nothing had to be invented; it only had to be found.
    ///
    /// What is missing is visibility. m_uniques is local and nothing replicates it, so this
    /// class republishes each character's own record into the world's global keys, which are
    /// broadcast to every client on connect and saved with the world. That last part is why
    /// global keys and not a ZDO: a gate that forgets people the moment they log off is not
    /// the mechanic that was asked for.
    /// </summary>
    internal static class Progression
    {
        /// <summary>
        /// "this character has played here", with the day it was last seen as the value.
        /// One key per character, rewritten in place rather than accumulating.
        /// </summary>
        private const string SeenPrefix = "wither_seen_";

        /// <summary>"this character was present when that boss died".</summary>
        private const string DonePrefix = "wither_p_";

        /// <summary>
        /// "the group has already cleared this boss, and that does not come undone".
        ///
        /// Latched once, never removed. Without it the gate regresses: a friend arriving with
        /// a fresh character would find every biome the group had earned, decide nobody has
        /// done the boss, and shut all of them - retroactively, for the people who did the
        /// work. Progress a group has paid for should not be revocable by someone else's
        /// arrival, so the moment the whole roster has a boss it is written down and the
        /// question is never asked again.
        /// </summary>
        private const string OpenPrefix = "wither_open_";

        /// <summary>
        /// Separates the last-seen day from the character name inside the seen key's value.
        /// The name is carried so the "waiting on" message can say who, and it rides along in
        /// the same key because one key per character is the whole point of the layout.
        /// </summary>
        private const char ValueSeparator = '|';

        // m_uniques is private, and HaveUniqueKey is an exact match. Reading the set directly
        // allows a case-insensitive compare, so a config key typed as Defeated_Eikthyr still
        // finds the defeated_eikthyr the game recorded.
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> UniquesOf =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_uniques");

        /// <summary>
        /// Whole days since the epoch, in UTC.
        ///
        /// Real days, not world time - "seen in the last 14 days" means what a person means by
        /// it, and Valheim's own clock advances only while somebody is playing, which would
        /// make the window unmeasurable. Day resolution rather than seconds is deliberate and
        /// load-bearing: every global key write also does
        /// m_knownWorldKeys.IncrementOrSet(key + " " + value) into the saved player profile, so
        /// a value that changes every heartbeat would grow that dictionary forever. Changing
        /// once a day keeps it to one entry per player per day, and clients disagreeing about
        /// the clock by a few hours cannot matter at this resolution.
        /// </summary>
        private static long Today()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .TotalDays;
        }

        /// <summary>
        /// Publish anything about the local character the world does not already know.
        ///
        /// Checks before it writes, and that check is load-bearing rather than tidy. The
        /// server's RPC_SetGlobalKey ends in SendGlobalKeys(ZRoutedRpc.Everybody) - accepting
        /// one key rebroadcasts the world's entire key list to every connected player. Its
        /// only guard is an exact string match against the flattened "key value", so a
        /// publisher that did not check first would push a full broadcast to the whole server
        /// on every call. Writing only on genuine change keeps that to once per player per
        /// day, plus once per boss credit.
        ///
        /// Cheap enough to call often, which matters because there is no single reliable
        /// moment to call it once: a character can arrive with credit from any past session.
        /// </summary>
        public static void PublishLocal(Player player)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || player == null) return;

            long id = player.GetPlayerID();
            if (id == 0L) return;   // profile not attached yet; nothing stable to key on

            string seenKey = SeenPrefix + id;
            string seenValue = Today() + ValueSeparator.ToString() + Sanitise(player.GetPlayerName());

            string current;
            if (!zone.GetGlobalKey(seenKey, out current) || current != seenValue.ToLower())
                zone.SetGlobalKey(seenKey + " " + seenValue);

            HashSet<string> uniques = UniquesOf(player);
            if (uniques == null) return;

            foreach (string bossKey in WitherConfig.AllGateKeys())
            {
                LatchIfGroupCleared(zone, bossKey);

                if (!HasLocally(uniques, bossKey)) continue;

                // The backfill is only trusted for a boss this world has actually seen die.
                //
                // m_uniques is stored per character and is world-agnostic - it means "this
                // character was present at an Eikthyr death", anywhere, ever. Taken at face
                // value that is a hole straight through the gate: kill everything on a solo
                // world, bring that character to the server, and arrive pre-credited for
                // bosses nobody here has fought.
                //
                // Requiring the world's own defeat key first closes it exactly. If the boss
                // has never died here, imported credit is refused. If it has, the world has
                // demonstrably progressed past it and crediting a character who was probably
                // one of the people who did it is both harmless and the only way an existing
                // server gets backfilled at all - nobody's past kills were recorded by a mod
                // that did not exist yet.
                if (!zone.GetGlobalKey(bossKey)) continue;

                string doneKey = DoneKey(id, bossKey);
                if (zone.GetGlobalKey(doneKey)) continue;

                zone.SetGlobalKey(doneKey);

                // A publish is exactly the event that can open a gate, so the cached roster
                // answer is stale the moment it lands.
                InvalidateRoster();

                WitherPlugin.Log.LogInfo(
                    "Published " + player.GetPlayerName() + "'s credit for " + bossKey + ".");
            }
        }

        /// <summary>
        /// Credit the local character for a boss that just died, immediately.
        ///
        /// This exists because reading m_uniques is not enough on its own, which cost a
        /// session to discover. Vanilla's OnDeath pushes the defeat key onto the *static*
        /// Player.m_addUniqueKeyQueue, and that queue is only drained by AddQueuedKeys, which
        /// is called from exactly two places - Player.Start and SetLocalPlayer - both of which
        /// are spawn-time. So m_uniques does not contain a boss you killed this session until
        /// you next spawn, and if you quit to desktop before respawning the queue dies with
        /// the process and the credit is lost outright.
        ///
        /// Observed rather than reasoned: Eikthyr was killed, the world key was set, and no
        /// credit was ever published. Hooking the death directly is the only way to catch it
        /// at the moment it is true.
        ///
        /// m_uniques is still read by PublishLocal, and still worth reading - it is where
        /// credit earned before this mod existed lives, and it is the only backfill there is.
        /// </summary>
        public static void CreditLocal(string bossKey)
        {
            if (string.IsNullOrEmpty(bossKey)) return;

            // Trolls, surtlings and bats carry a defeat key too. Publishing those would put a
            // key in the world for every player and every creature type, for a gate that will
            // never ask about them. If the table later starts asking, PublishLocal picks it
            // up off m_uniques on the next spawn.
            if (!WitherConfig.IsGateKey(bossKey)) return;

            Player player = Player.m_localPlayer;
            ZoneSystem zone = ZoneSystem.instance;
            if (player == null || zone == null) return;

            long id = player.GetPlayerID();
            if (id == 0L) return;

            string doneKey = DoneKey(id, bossKey);
            if (zone.GetGlobalKey(doneKey)) return;

            zone.SetGlobalKey(doneKey);
            InvalidateRoster();

            WitherPlugin.Log.LogInfo(
                "Credited " + player.GetPlayerName() + " for " + bossKey + " at the kill.");
        }

        /// <summary>
        /// Whether the whole group has done this boss.
        ///
        /// Allocation-free and safe to ask several times a frame, which it is: the gate is
        /// consulted by the tick, by both status effects, and by every consume and every
        /// status effect the game tries to apply. The first version of this built the list of
        /// missing names on every call, which meant a string allocation per frame for as long
        /// as a player stood in a gated biome - the exact case the mod is designed to make
        /// last a long time. Names are now built only by BlockersFor, on transitions.
        ///
        /// An empty roster falls back to the world key rather than passing. "Every member has
        /// it" is vacuously true of nobody, and a fresh world would swing every gate open in
        /// the window between spawning and the first publish - the one moment the answer
        /// matters most, and the one the naive form gets exactly backwards.
        /// </summary>
        public static bool GroupHasKey(string bossKey)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return false;

            // Cleared once is cleared for good. Checked first because it is one lookup and
            // because it is the answer whenever it is present.
            if (WitherConfig.GateNeverRegresses.Value
                && zone.GetGlobalKey(OpenKey(bossKey))) return true;

            List<RosterEntry> roster = Roster();
            if (roster.Count == 0) return zone.GetGlobalKey(bossKey);

            for (int i = 0; i < roster.Count; i++)
                if (!zone.GetGlobalKey(DoneKey(roster[i].Id, bossKey))) return false;

            return true;
        }

        /// <summary>
        /// Write the latch the first time the whole roster has a boss.
        ///
        /// Called from the publish tick rather than from GroupHasKey, because GroupHasKey is
        /// a hot read asked several times a frame and a read path should not be writing to
        /// the world. A few seconds of lag costs nothing: until the latch lands, the
        /// all-have-it check returns the same answer anyway.
        ///
        /// Deliberately not latched off the empty-roster fallback. An empty roster is a
        /// startup condition, not a statement about the group, and latching from it would
        /// let one client's loading screen permanently open a biome.
        /// </summary>
        private static void LatchIfGroupCleared(ZoneSystem zone, string bossKey)
        {
            if (!WitherConfig.GateNeverRegresses.Value) return;

            string openKey = OpenKey(bossKey);
            if (zone.GetGlobalKey(openKey)) return;

            List<RosterEntry> roster = Roster();
            if (roster.Count == 0) return;

            for (int i = 0; i < roster.Count; i++)
                if (!zone.GetGlobalKey(DoneKey(roster[i].Id, bossKey))) return;

            zone.SetGlobalKey(openKey);
            InvalidateRoster();

            WitherPlugin.Log.LogInfo(
                "The group has cleared " + bossKey + "; that biome is open for good.");
        }

        private static string OpenKey(string bossKey)
        {
            return OpenPrefix + bossKey.ToLowerInvariant();
        }

        /// <summary>Whether this boss is open because it was latched, for diagnostics.</summary>
        public static bool IsLatchedOpen(string bossKey)
        {
            ZoneSystem zone = ZoneSystem.instance;
            return zone != null && zone.GetGlobalKey(OpenKey(bossKey));
        }

        /// <summary>
        /// The names still owed on this boss, comma separated, or null if nobody is.
        /// Allocates; call it on a transition or for a log line, never per frame.
        /// </summary>
        public static string BlockersFor(string bossKey)
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return null;

            StringBuilder missing = null;
            foreach (RosterEntry member in Roster())
            {
                if (zone.GetGlobalKey(DoneKey(member.Id, bossKey))) continue;

                if (missing == null) missing = new StringBuilder();
                else missing.Append(", ");

                missing.Append(member.Name);
            }

            return missing == null ? null : missing.ToString();
        }

        private static List<RosterEntry> _roster;
        private static float _rosterBuiltAt = float.NegativeInfinity;

        /// <summary>How long a built roster is reused for, in real seconds.</summary>
        private const float RosterCacheSeconds = 2f;

        /// <summary>
        /// Everyone who has played here recently enough to count.
        ///
        /// A character enters the roster the first time it spawns with this mod installed and
        /// drops out after RosterDays of absence. Two useful properties fall out of that.
        /// Characters that predate the mod are not in it, so adding the mod to a long-running
        /// world does not instantly wither everyone on behalf of an alt nobody has touched
        /// since spring. And leaving is automatic, so the escape hatch is "do not log in",
        /// which needs no admin command.
        ///
        /// Cached for a couple of seconds because building it walks every global key in the
        /// world and allocates, and the gate asks for it many times a frame. Two seconds is
        /// far below any rate at which the answer can change - a roster changes when somebody
        /// logs in or kills a boss - and far above a frame.
        /// </summary>
        public static List<RosterEntry> Roster()
        {
            if (_roster != null && Time.realtimeSinceStartup - _rosterBuiltAt < RosterCacheSeconds)
                return _roster;

            List<RosterEntry> roster = BuildRoster();
            _roster = roster;
            _rosterBuiltAt = Time.realtimeSinceStartup;
            return roster;
        }

        /// <summary>Drop the cache, for when the answer has certainly just changed.</summary>
        public static void InvalidateRoster()
        {
            _rosterBuiltAt = float.NegativeInfinity;
        }

        private static List<RosterEntry> BuildRoster()
        {
            var roster = new List<RosterEntry>();

            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return roster;

            long today = Today();
            long window = Math.Max(1L, (long)WitherConfig.RosterDays.Value);
            HashSet<string> excluded = WitherConfig.ExcludedPlayerIds;

            // m_globalKeysValues is public, and is the only place the values live -
            // m_globalKeys holds "key value" as one flattened string, which would have to be
            // re-split to be useful.
            foreach (KeyValuePair<string, string> pair in zone.m_globalKeysValues)
            {
                if (!pair.Key.StartsWith(SeenPrefix, StringComparison.Ordinal)) continue;

                string idText = pair.Key.Substring(SeenPrefix.Length);
                if (excluded.Contains(idText)) continue;

                long id;
                if (!long.TryParse(idText, out id)) continue;

                long lastSeen;
                string name;
                if (!ParseSeenValue(pair.Value, out lastSeen, out name)) continue;

                // Future dates survive the window check rather than being discarded. A client
                // with a clock set forward writes one, and dropping that player out of the
                // roster would quietly open the gate for everybody.
                if (today - lastSeen > window) continue;

                roster.Add(new RosterEntry
                {
                    Id = id,
                    Name = string.IsNullOrEmpty(name) ? idText : name,
                    LastSeenDay = lastSeen
                });
            }

            return roster;
        }

        private static bool ParseSeenValue(string value, out long day, out string name)
        {
            day = 0L;
            name = null;
            if (string.IsNullOrEmpty(value)) return false;

            int split = value.IndexOf(ValueSeparator);
            string dayText = split < 0 ? value : value.Substring(0, split);
            if (split >= 0) name = value.Substring(split + 1);

            return long.TryParse(dayText, out day);
        }

        private static bool HasLocally(HashSet<string> uniques, string bossKey)
        {
            foreach (string unique in uniques)
                if (string.Equals(unique, bossKey, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        /// <summary>
        /// Global keys are lowercased and split on their first space, so the name has to
        /// survive both. Spaces become underscores; the rest is cosmetic anyway, since it only
        /// ever appears in a "waiting on" line.
        /// </summary>
        private static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            return name.Replace(' ', '_').Replace(ValueSeparator, '_');
        }

        private static string DoneKey(long id, string bossKey)
        {
            return DonePrefix + id + "_" + bossKey.ToLowerInvariant();
        }

        internal struct RosterEntry
        {
            public long Id;
            public string Name;
            public long LastSeenDay;
        }
    }
}

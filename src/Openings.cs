using System;
using System.Collections.Generic;
using System.Text;

namespace Utangard
{
    /// <summary>
    /// Says so, wherever you are, the moment a biome opens.
    ///
    /// Until now the only way to learn that the group had finally cleared Bonemass was to
    /// walk to the Mountain and not be refused - which is backwards for a mod whose whole
    /// argument is that fetching the friend who is behind should be worth doing. The payoff
    /// for going and getting somebody landed silently, and often to nobody, because the
    /// person who benefits most is usually the one at home who was never at the fight.
    ///
    /// No network code, and deliberately none. Every input to the gate is a global key, and
    /// global keys are broadcast to every client whenever one is written, so each client can
    /// watch its own copy and announce to itself. An RPC would be a second channel saying
    /// what the first already said, with its own ways of being missed.
    ///
    /// It also covers openings that were nobody's kill: a catch-up deadline expiring, or a
    /// member of the roster ageing out, both open a biome without anything dying. Watching
    /// the *answer* rather than hooking the credit is what gets those for free.
    /// </summary>
    internal static class Openings
    {
        /// <summary>
        /// What each gate key answered last time we looked.
        ///
        /// Absent means "not yet seen", which is seeded silently. Announcing on the first
        /// look would greet every login with a wall of messages about biomes the group
        /// earned weeks ago.
        /// </summary>
        private static readonly Dictionary<string, bool> Known =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Once a second. The keys arrive by RPC and nothing here is urgent to the frame;
        /// what matters is that it is well under the time it takes to walk anywhere.
        /// </summary>
        private const float Interval = 1f;

        private static float _timer;

        /// <summary>
        /// Forget everything. Called when the local player changes - a death, a logout, a
        /// different world - because the seeded state belongs to the world that was loaded
        /// when it was taken, and carrying it into the next one would announce another
        /// world's progress.
        /// </summary>
        public static void Forget()
        {
            Known.Clear();
            _timer = Interval;
        }

        public static void Check(Player player, float dt)
        {
            if (!UtangardConfig.AnnounceOpenings.Value) return;

            _timer += dt;
            if (_timer < Interval) return;
            _timer = 0f;

            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return;

            foreach (string key in UtangardConfig.AllGateKeys())
            {
                bool open = BiomeGate.Earned(zone, key);

                bool was;
                if (!Known.TryGetValue(key, out was))
                {
                    Known[key] = open;
                    continue;
                }

                if (was == open) continue;

                Known[key] = open;

                // Only the opening is worth a message. A gate that closes again is either
                // GateNeverRegresses being off or somebody editing the table, and neither is
                // news the way "you may go there now" is news.
                if (open) Announce(player, key);
            }
        }

        private static void Announce(Player player, string key)
        {
            string biomes = BiomesFor(key);
            if (string.IsNullOrEmpty(biomes)) return;

            // Plain text and not a $token: Message runs the string through
            // Localization.Localize, and an unregistered token renders as the raw word.
            string message = UtangardConfig.OpenedMessage.Value;
            if (string.IsNullOrEmpty(message)) return;

            // Replace rather than string.Format. The player owns this string, and a stray
            // brace in somebody's wording should not throw inside a message about good news.
            message = message.Replace("{biome}", biomes);

            player.Message(MessageHud.MessageType.Center, message);
            UtangardPlugin.Log.LogInfo("Gate opened: " + key + " (" + biomes + ").");
        }

        /// <summary>
        /// The biomes a key opens, named as the game names them.
        ///
        /// Plural because the table is a table: two rows may point at one key, and then one
        /// kill opens both. Naming only the first would be right most of the time, which is
        /// the worst kind of wrong for a message that appears twice a week.
        /// </summary>
        private static string BiomesFor(string key)
        {
            var names = new StringBuilder();

            foreach (Heightmap.Biome biome in UtangardConfig.GateableBiomes)
            {
                string required = UtangardConfig.RequiredKeyFor(biome);
                if (required == null
                    || !string.Equals(required, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (names.Length > 0) names.Append(" and ");
                names.Append(GateReport.BiomeName(biome));
            }

            return names.ToString();
        }
    }
}

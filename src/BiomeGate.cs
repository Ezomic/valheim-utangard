namespace Wither
{
    /// <summary>
    /// The one question the rest of the mod asks: is this player standing somewhere the
    /// world has not yet earned?
    ///
    /// Deliberately stateless. Every caller asks fresh, every tick, because the two inputs -
    /// the player's biome and the world's global keys - are both single lookups, and a cache
    /// here would only buy the chance of being wrong for a second after a boss dies.
    /// </summary>
    internal static class BiomeGate
    {
        /// <summary>
        /// True while the gate is closed on this player.
        ///
        /// Only ever true for the local player. Food and status effects in Valheim are the
        /// owner's business - the owning client runs SEMan, and writes from anyone else are
        /// discarded - so each client enforces this on itself and nothing is sent over the
        /// wire. The consequence worth knowing: a player without the mod is not gated. This
        /// is a rule for a group that all runs the same plugins, not an anti-cheat.
        /// </summary>
        public static bool IsWithered(Player player)
        {
            if (player == null || !WitherConfig.Enabled.Value) return false;
            if (player != Player.m_localPlayer) return false;

            string key = WitherConfig.RequiredKeyFor(player.GetCurrentBiome());
            if (key == null) return false;

            // No world yet means nothing to ask. Fail open: an unanswerable question must
            // not starve someone on a loading screen.
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return false;

            if (!WitherConfig.GateOnGroup.Value) return !zone.GetGlobalKey(key);

            return !Progression.GroupHasKey(key);
        }

        /// <summary>
        /// The names this player's current biome is still waiting on, or null.
        ///
        /// Separate from IsWithered and allocating, so the hot path stays free of it. Asked
        /// only when a message is about to be shown.
        /// </summary>
        public static string BlockersHere(Player player)
        {
            if (player == null || !WitherConfig.GateOnGroup.Value) return null;

            string key = WitherConfig.RequiredKeyFor(player.GetCurrentBiome());
            return key == null ? null : Progression.BlockersFor(key);
        }
    }
}

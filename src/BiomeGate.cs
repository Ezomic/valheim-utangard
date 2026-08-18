using System.Collections.Generic;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// The one question the rest of the mod asks: is this player standing somewhere the
    /// world has not yet earned?
    ///
    /// The player's own biome is a single lookup and is asked fresh every time. The border
    /// margin is not - it samples the biome at eight points around the player - so that part
    /// alone is cached, keyed on where the player was standing when it was computed.
    /// </summary>
    internal static class BiomeGate
    {
        /// <summary>
        /// Compass points, sampled at BorderMargin around the player.
        ///
        /// Eight, and no interior ring. A biome border is a smooth curve tens of metres
        /// across at its sharpest, so at five metres the gap between neighbouring samples is
        /// under four metres of arc, and a border that touches the ring at all crosses one
        /// of them.
        /// </summary>
        private static readonly Vector2[] Compass =
        {
            new Vector2(1f, 0f),
            new Vector2(0.7071f, 0.7071f),
            new Vector2(0f, 1f),
            new Vector2(-0.7071f, 0.7071f),
            new Vector2(-1f, 0f),
            new Vector2(-0.7071f, -0.7071f),
            new Vector2(0f, -1f),
            new Vector2(0.7071f, -0.7071f)
        };

        /// <summary>
        /// Where the ring was last sampled, and the distinct keys it found.
        ///
        /// The *keys* are cached and not the verdict on them. A verdict goes stale the moment
        /// a boss dies or a deadline passes, and a player standing still at a border would
        /// hold the old one until they moved - which is precisely when somebody is watching.
        /// Which biomes are within five metres, on the other hand, cannot change while the
        /// player does not move.
        /// </summary>
        private static Vector3 _sampledAt;
        private static float _sampledMargin = -1f;
        private static readonly List<string> _sampledKeys = new List<string>(2);

        /// <summary>
        /// How far the player may move before the margin is sampled again. A quarter of a
        /// metre is a few frames of walking and far less than the margin itself, so the
        /// error it can introduce is a fraction of a step at the very edge of the band.
        /// </summary>
        private const float ResampleDistance = 0.25f;

        /// <summary>
        /// The global key withering this player right now, or null if none is.
        ///
        /// This is the gate. The drain, the refusals, Sapped and the message naming who is
        /// owed are all downstream of this one answer, and they have to agree about *which*
        /// biome is doing it - which is why this returns the key and not a bool. With a
        /// border margin the biome withering you is often not the one you are standing in.
        ///
        /// Only ever answers for the local player. Food and status effects in Valheim are the
        /// owner's business - the owning client runs SEMan, and writes from anyone else are
        /// discarded - so each client enforces this on itself and nothing is sent over the
        /// wire. The consequence worth knowing: a player without the mod is not gated. This
        /// is a rule for a group that all runs the same plugins, not an anti-cheat.
        /// </summary>
        public static string GatingKey(Player player)
        {
            if (player == null || !UtangardConfig.Enabled.Value) return null;
            if (player != Player.m_localPlayer) return null;

            // No world yet means nothing to ask. Fail open: an unanswerable question must
            // not starve someone on a loading screen.
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return null;

            string key = UtangardConfig.RequiredKeyFor(player.GetCurrentBiome());
            if (key != null && !Earned(zone, key)) return key;

            // The margin is asked second, and only when the ground underfoot is allowed,
            // because inside a gated biome it can only ever agree - and it costs eight
            // heightmap lookups to say so.
            return NearbyGatingKey(player, zone);
        }

        /// <summary>True while the gate is closed on this player.</summary>
        public static bool IsWithered(Player player)
        {
            return GatingKey(player) != null;
        }

        /// <summary>
        /// The names this player's gate is still waiting on, or null.
        ///
        /// Separate from GatingKey and allocating, so the hot path stays free of it. Asked
        /// only when a message is about to be shown.
        /// </summary>
        public static string BlockersHere(Player player)
        {
            if (!UtangardConfig.GateOnGroup.Value) return null;

            string key = GatingKey(player);
            return key == null ? null : Progression.BlockersFor(key, excludeSelf: true);
        }

        /// <summary>Whether the world, or the group, counts this key as done.</summary>
        private static bool Earned(ZoneSystem zone, string key)
        {
            if (!UtangardConfig.GateOnGroup.Value) return zone.GetGlobalKey(key);

            return Progression.GroupHasKey(key);
        }

        /// <summary>
        /// The key of a gated biome within BorderMargin of the player, or null.
        ///
        /// Why it exists: without it the border is a line, and a line can be stood a step
        /// behind. The whole penalty - the drain, the refusal, the grudge - is escapable by
        /// walking three metres out of the Swamp, eating, and walking back, which turns a
        /// rule about where you may live into a rule about where you may chew. A band you
        /// have to genuinely clear cannot be crossed from the edge of the fight you are in.
        ///
        /// Heightmap.FindBiome rather than the player's own m_currentBiome, because that is
        /// a cached value updated once a second from the player's own position and there is
        /// no per-point equivalent. It compares only X and Z, so the dungeon case looks after
        /// itself exactly as the main gate does: a crypt interior sits above its entrance and
        /// samples the biome that entrance is in.
        /// </summary>
        private static string NearbyGatingKey(Player player, ZoneSystem zone)
        {
            float margin = UtangardConfig.BorderMargin.Value;
            if (margin <= 0f) return null;

            Vector3 here = player.transform.position;

            // Recompute on a change of place rather than on a timer. A player standing in a
            // doorway asks this every frame and the answer cannot have moved; a player
            // walking gets a fresh one every quarter of a metre.
            if (margin == _sampledMargin
                && (here - _sampledAt).sqrMagnitude < ResampleDistance * ResampleDistance)
                return FirstUnearned(zone);

            _sampledAt = here;
            _sampledMargin = margin;
            _sampledKeys.Clear();

            for (int i = 0; i < Compass.Length; i++)
            {
                Vector3 point = new Vector3(
                    here.x + Compass[i].x * margin, here.y, here.z + Compass[i].y * margin);

                string key = UtangardConfig.RequiredKeyFor(Heightmap.FindBiome(point));

                // Distinct by hand rather than with a HashSet: eight samples land on one or
                // two biomes in every case that is not a three-way corner, and a linear scan
                // of a list that short beats allocating anything.
                if (key != null && !_sampledKeys.Contains(key)) _sampledKeys.Add(key);
            }

            return FirstUnearned(zone);
        }

        /// <summary>The first of the sampled keys the group has not earned, or null.</summary>
        private static string FirstUnearned(ZoneSystem zone)
        {
            for (int i = 0; i < _sampledKeys.Count; i++)
                if (!Earned(zone, _sampledKeys[i])) return _sampledKeys[i];

            return null;
        }
    }
}

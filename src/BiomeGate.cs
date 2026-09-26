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
        /// The biome each sampled key came from, index for index. Kept since LHM-26, because a
        /// foothold belongs to a biome and not to a key: two rows of the table may share a key,
        /// and "the rules being applied here" has to name the ground they came from.
        /// </summary>
        private static readonly List<Heightmap.Biome> _sampledBiomes = new List<Heightmap.Biome>(2);

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
            Heightmap.Biome ignored;
            return GatingKey(player, out ignored);
        }

        /// <summary>
        /// GatingKey, and the biome whose rules are being applied: the locked biome underfoot, or
        /// the one whose border margin the player is standing in. Biome.None when nothing gates.
        ///
        /// A player's foothold (LHM-26) is asked about this biome and no other, so the relief and
        /// the rule it relieves always come from the same ground. The obvious shortcut, the
        /// player's own current biome, is wrong in exactly the margin: standing in an open Black
        /// Forest three metres from a locked Swamp, the rules are the Swamp's, and the Black
        /// Forest's bars say nothing about them.
        /// </summary>
        public static string GatingKey(Player player, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;

            if (player == null || !UtangardConfig.Enabled.Value) return null;
            if (player != Player.m_localPlayer) return null;

            // Never wither somebody who has no way to stop being withered.
            //
            // This is the single choke point the drain, both refusals, Sapped, the marker and
            // the entry message all hang off, so one answer here turns the punishing half of
            // the mod off without touching the reporting half - the compendium page and the
            // openings watcher read Earned directly and go on telling the truth about the
            // gate. Which is the shape this failure needs: say what is shut, stop starving
            // people over it. See Seams.PenaltyIsEscapable for when and why.
            if (!Seams.PenaltyIsEscapable()) return null;

            // No world yet means nothing to ask. Fail open: an unanswerable question must
            // not starve someone on a loading screen.
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return null;

            Heightmap.Biome underfoot = player.GetCurrentBiome();
            string key = UtangardConfig.RequiredKeyFor(underfoot);
            if (key != null && !Earned(zone, key))
            {
                biome = underfoot;
                return key;
            }

            // The margin is asked second, and only when the ground underfoot is allowed,
            // because inside a gated biome it can only ever agree - and it costs eight
            // heightmap lookups to say so.
            return NearbyGatingKey(player, zone, out biome);
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

        /// <summary>
        /// Whether the world, or the group, counts this key as done.
        ///
        /// Shared with the openings watcher rather than private, so that "open" means one
        /// thing: the message announcing a biome and the gate that stops withering you are
        /// then incapable of disagreeing, which they would eventually do as two copies.
        /// </summary>
        public static bool Earned(ZoneSystem zone, string key)
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
        private static string NearbyGatingKey(Player player, ZoneSystem zone, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;

            float margin = UtangardConfig.BorderMargin.Value;
            if (margin <= 0f) return null;

            Vector3 here = player.transform.position;

            // Recompute on a change of place rather than on a timer. A player standing in a
            // doorway asks this every frame and the answer cannot have moved; a player
            // walking gets a fresh one every quarter of a metre.
            if (margin == _sampledMargin
                && (here - _sampledAt).sqrMagnitude < ResampleDistance * ResampleDistance)
                return FirstUnearned(zone, out biome);

            _sampledAt = here;
            _sampledMargin = margin;
            _sampledKeys.Clear();
            _sampledBiomes.Clear();

            for (int i = 0; i < Compass.Length; i++)
            {
                Vector3 point = new Vector3(
                    here.x + Compass[i].x * margin, here.y, here.z + Compass[i].y * margin);

                Heightmap.Biome sampled = Heightmap.FindBiome(point);
                string key = UtangardConfig.RequiredKeyFor(sampled);

                // Distinct by hand rather than with a HashSet: eight samples land on one or
                // two biomes in every case that is not a three-way corner, and a linear scan
                // of a list that short beats allocating anything. Distinct by biome rather
                // than by key since LHM-26, so each entry still says which ground it is from.
                if (key != null && !_sampledBiomes.Contains(sampled))
                {
                    _sampledKeys.Add(key);
                    _sampledBiomes.Add(sampled);
                }
            }

            return FirstUnearned(zone, out biome);
        }

        /// <summary>The first sampled key the group has not earned, and its biome, or null.</summary>
        private static string FirstUnearned(ZoneSystem zone, out Heightmap.Biome biome)
        {
            for (int i = 0; i < _sampledKeys.Count; i++)
            {
                if (Earned(zone, _sampledKeys[i])) continue;

                biome = _sampledBiomes[i];
                return _sampledKeys[i];
            }

            biome = Heightmap.Biome.None;
            return null;
        }
    }
}

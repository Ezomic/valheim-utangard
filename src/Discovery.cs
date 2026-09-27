using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace Utangard
{
    /// <summary>
    /// How much of each biome's map this character has uncovered on its own feet. The Discovery
    /// half of a foothold (see Foothold).
    ///
    /// <b>What it reads.</b> Minimap.m_explored, one bit per map pixel, set when the fog lifts
    /// around you as you move. Not m_exploredOthers, which is what a map table hands out: Minimap
    /// keeps the two apart all the way down (AddSharedMapData only ever calls ExploreOthers), so
    /// reading the first one alone is exactly "walked it yourself". The map is saved per character
    /// per world in the PlayerProfile and loaded into the local Minimap, so this is the local
    /// character's own data on the local client, and nothing has to cross the network.
    ///
    /// <b>How a pixel is filed.</b> Under the biome WorldGenerator.GetBiome puts its centre in.
    /// The game has no exploration per biome; this works it out from what it does keep. The
    /// centre is the one Minimap.WorldToPixel implies, (x - size/2) * pixelSize, which is also
    /// what `utangard biomes` uses. Near a border the fog lifts on both sides of it, so walking the
    /// edge of the Plains uncovers some Plains as well. That is the map's rule, not ours, and it
    /// is not small: the fog lifts in a circle of m_exploreRadius around you (100 m in the game's
    /// code; the prefab's own value has not been read), so a walk along a border, or a sail along
    /// a coast, uncovers about 0.1 km2 of the far side per kilometre without setting foot there.
    /// The saved map keeps no record of where you stood when a pixel lifted, so counting only
    /// ground uncovered under that biome's rules would need a history the game does not keep.
    /// Said in the README and the cfg rather than fought, and made smaller instead: the full bar
    /// went from 0.5 km2 to 1 on 2026-09-27, Robbin's "let's just up the points required", so
    /// what used to take five kilometres of coastline now takes ten.
    ///
    /// <b>A pixel counts once.</b> The bit only ever goes from clear to set, and Minimap.Explore
    /// answers true only on that change, so running back and forth over the same ground earns
    /// nothing.
    ///
    /// <b>Cheap.</b> The map is 2048 x 2048 on 1.0, four million bits, and a biome lookup is a
    /// handful of noise samples. So nothing here walks the map per frame. Once per world a
    /// background count reads it a slice at a time from the tick, under a time budget, and from
    /// then on each newly uncovered pixel is counted as the minimap uncovers it, through a postfix
    /// on the private method that sets the bit.
    ///
    /// The two meet at a cursor. The background count has read every pixel below it; a pixel
    /// uncovered below the cursor is counted by the postfix, and one at or above it is left for
    /// the count to find. So nothing is counted twice and nothing is missed, however the two
    /// interleave. Anything that rewrites the map wholesale - loading it, resetting it, the
    /// exploremap cheat - puts the cursor back to zero, which also makes the postfix stand aside
    /// for the millions of bits those set, rather than looking up a biome for each one in a
    /// single frame.
    /// </summary>
    internal static class Discovery
    {
        /// <summary>The minimap the counts describe. A new world is a new Minimap.</summary>
        private static Minimap _map;

        /// <summary>Next pixel index the background count will read.</summary>
        private static int _cursor;

        private static bool _done;

        private static readonly Dictionary<Heightmap.Biome, int> Counts = new Dictionary<Heightmap.Biome, int>();

        /// <summary>
        /// How long the background count may run per tick. The tick is Player.UpdateFood, which
        /// comes at the physics rate, so one millisecond is about five percent of one core until
        /// the count finishes and nothing after. Measured nowhere yet; a heavily walked world is
        /// the case worth timing.
        /// </summary>
        private static readonly long Budget = Stopwatch.Frequency / 1000L;

        /// <summary>Pixels read between looks at the clock.</summary>
        private const int Slice = 256;

        /// <summary>
        /// Beyond this the world is the edge-of-the-world void. WorldGenerator.waterEdge, 10500 m;
        /// the fog lifts past it at the very rim, and none of that is a biome anybody walked.
        /// </summary>
        private const float Edge = WorldGenerator.waterEdge;

        private static bool _saidFailure;
        private static bool _gaveUp;

        /// <summary>
        /// Whether Discovery can be read at all this session: both halves of the hook went on, the
        /// tick that runs the background count went on, and the private bit array bound. False
        /// means the bar stays empty and healing stays locked, which is the lock as it was.
        ///
        /// The tick is part of it because nothing else advances the count. Step runs only from
        /// Player.UpdateFood, and until the count has passed a pixel the Uncover postfix leaves it
        /// alone, so without the tick every bar would sit at zero and the panel would say "still
        /// reading your map" for the rest of the session - true in no useful sense. Saying the
        /// map cannot be read is the honest version of the same empty bar.
        /// </summary>
        internal static bool Available()
        {
            return Seams.MapExplore && Seams.FoodTick && !_gaveUp && ExploredOf() != null;
        }

        /// <summary>True while this world's background count has not finished.</summary>
        internal static bool Counting()
        {
            return !_done;
        }

        /// <summary>Pixels of this biome the character uncovered on foot, counted so far.</summary>
        internal static int Pixels(Heightmap.Biome biome)
        {
            // Asked about the map the counts belong to, or it is another world's number.
            var map = Minimap.instance;
            if (map == null || !ReferenceEquals(map, _map)) return 0;

            int n;
            return Counts.TryGetValue(biome, out n) ? n : 0;
        }

        /// <summary>
        /// Pixels that make a full bar: DiscoveryFullKm2 over the area of one map pixel, read off
        /// the running minimap rather than assumed. 0 when the config asks for no walking.
        /// </summary>
        internal static int FullPixels()
        {
            float km2 = UtangardConfig.DiscoveryFullKm2.Value;
            if (km2 <= 0f) return 0;

            float pixelKm2 = PixelKm2();
            if (pixelKm2 <= 0f) return int.MaxValue;   // no map: unreachable, so the lock holds

            return (int)Math.Ceiling(km2 / pixelKm2);
        }

        /// <summary>One map pixel in square kilometres, or 0 without a map.</summary>
        internal static float PixelKm2()
        {
            var map = Minimap.instance;
            if (map == null) return 0f;

            float metres = map.m_pixelSize;
            return metres * metres / 1000000f;
        }

        /// <summary>Background count progress, 0 to 1, for the console.</summary>
        internal static float Progress()
        {
            if (_done) return 1f;

            var map = Minimap.instance;
            if (map == null || !ReferenceEquals(map, _map)) return 0f;

            int total = map.m_textureSize * map.m_textureSize;
            return total <= 0 ? 0f : (float)_cursor / total;
        }

        /// <summary>
        /// Advance the background count by one time slice. Called from the tick, for the local
        /// player only. Cheap once the count is done: two reference compares.
        /// </summary>
        internal static void Step()
        {
            if (_gaveUp || !Seams.MapExplore) return;
            if (!UtangardConfig.Enabled.Value || !UtangardConfig.FootholdEnabled.Value) return;

            var map = Minimap.instance;
            if (map == null) return;

            if (!ReferenceEquals(map, _map)) Restart(map);
            if (_done) return;

            try
            {
                BitArray explored = Bits(map);
                var world = WorldGenerator.instance;
                if (explored == null || world == null) return;

                int size = map.m_textureSize;
                int total = explored.Length;
                if (size <= 0 || size * size != total)
                {
                    GiveUp("the explored map is " + total + " bits for a " + size + " pixel square");
                    return;
                }

                long deadline = Stopwatch.GetTimestamp() + Budget;

                while (_cursor < total)
                {
                    int stop = Math.Min(total, _cursor + Slice);
                    for (; _cursor < stop; _cursor++)
                        if (explored[_cursor]) Count(map, world, _cursor % size, _cursor / size);

                    if (Stopwatch.GetTimestamp() >= deadline) return;
                }

                _done = true;

                if (UtangardConfig.Verbose.Value)
                    UtangardPlugin.Log.LogInfo("Discovery: counted this world's map. " + Describe());
            }
            catch (Exception e)
            {
                GiveUp(e.ToString());
            }
        }

        /// <summary>Start over on this map: counts cleared, cursor at the first pixel.</summary>
        private static void Restart(Minimap map)
        {
            _map = map;
            _cursor = 0;
            _done = false;
            Counts.Clear();
        }

        /// <summary>File one uncovered pixel under the biome at its centre.</summary>
        private static void Count(Minimap map, WorldGenerator world, int x, int y)
        {
            int half = map.m_textureSize / 2;
            float wx = (x - half) * map.m_pixelSize;
            float wz = (y - half) * map.m_pixelSize;
            if (wx * wx + wz * wz > Edge * Edge) return;

            var biome = world.GetBiome(wx, wz);

            int n;
            Counts.TryGetValue(biome, out n);
            Counts[biome] = n + 1;
        }

        /// <summary>Every biome's count, for the log and the console.</summary>
        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (var biome in UtangardConfig.GateableBiomes)
            {
                int n;
                if (Counts.TryGetValue(biome, out n) && n > 0) parts.Add(biome + " " + n);
            }

            return parts.Count == 0 ? "Nothing uncovered on foot yet." : string.Join(", ", parts.ToArray()) + " pixels.";
        }

        private static void GiveUp(string why)
        {
            _gaveUp = true;
            if (_saidFailure) return;
            _saidFailure = true;

            UtangardPlugin.Log.LogError(
                "Utangard could not count the explored map, so the Discovery bar stays empty and "
                + "healing stays locked in a locked biome, as it was before footholds existed. " + why);
        }

        // ------------------------------------------------------------------- the hooks ----

        /// <summary>
        /// A pixel the minimap just uncovered, counted the moment it happens.
        ///
        /// Minimap.Explore(int, int) is private and is the only thing that sets a bit in
        /// m_explored one at a time; it returns true only when the bit was clear. Its callers are
        /// the walking explore every two seconds, ExploreAll, and the pre-1.0 map loader - the
        /// last two rewrite the map wholesale and are covered by Rewrite below, which puts the
        /// cursor at zero before they start so this stands aside for them.
        ///
        /// Found by shape rather than by a pinned signature, since Minimap has a second Explore
        /// taking a position and a radius: the one with two int parameters returning bool.
        /// </summary>
        [HarmonyPatch]
        internal static class Uncover
        {
            [HarmonyTargetMethod]
            private static MethodBase Target()
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Minimap)))
                {
                    if (method.Name != "Explore" || method.ReturnType != typeof(bool)) continue;

                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length == 2 && args[0].ParameterType == typeof(int) && args[1].ParameterType == typeof(int))
                        return method;
                }

                UtangardPlugin.Log.LogError(
                    "Minimap has no Explore(int, int) any more - the Discovery bar cannot follow new "
                    + "exploration, so healing stays locked in a locked biome.");

                return null;
            }

            /// <param name="__0">x. Positional, so a renamed parameter cannot unseat it.</param>
            /// <param name="__1">y.</param>
            /// <param name="__result">True when this call uncovered the pixel.</param>
            [HarmonyPostfix]
            private static void Postfix(Minimap __instance, int __0, int __1, bool __result)
            {
                if (!__result || _gaveUp) return;

                try
                {
                    if (!ReferenceEquals(__instance, _map)) Restart(__instance);

                    // At or past the cursor the background count has not read yet, and will.
                    int index = __1 * __instance.m_textureSize + __0;
                    if (!_done && index >= _cursor) return;

                    var world = WorldGenerator.instance;
                    if (world == null) return;

                    Count(__instance, world, __0, __1);
                }
                catch (Exception e)
                {
                    // Never let a count break the fog lifting.
                    GiveUp(e.ToString());
                }
            }
        }

        /// <summary>
        /// Anything that rewrites the explored map wholesale sends the count back to the start.
        ///
        /// SetMapData is the load, per character per world, and it replaces every bit without
        /// going through Explore(int, int). Reset is the resetmap command and the old map
        /// format's loader. ExploreAll is the exploremap cheat, which uncovers all four million
        /// pixels through Explore(int, int) in one frame: with the cursor at zero the postfix above
        /// skips every one of them for the price of a comparison, and the background count picks
        /// them up over the following seconds instead.
        ///
        /// A prefix, so the cursor is back at zero before the first bit changes.
        /// </summary>
        [HarmonyPatch]
        internal static class Rewrite
        {
            private static readonly string[] Names = { "SetMapData", "Reset", "ExploreAll" };

            [HarmonyTargetMethods]
            private static IEnumerable<MethodBase> Targets()
            {
                var found = new List<MethodBase>();

                foreach (string name in Names)
                {
                    bool any = false;
                    foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Minimap)))
                    {
                        if (method.Name != name) continue;
                        found.Add(method);
                        any = true;
                    }

                    // All three or none. Missing one means a rewrite the count cannot see, and a
                    // count that silently disagrees with the map is worse than no count: throw, so
                    // Seams reports the seam broken and Discovery reads as unavailable.
                    if (!any)
                        throw new MissingMethodException("Minimap", name);
                }

                return found;
            }

            [HarmonyPrefix]
            private static void Prefix(Minimap __instance)
            {
                if (__instance != null) Restart(__instance);
            }
        }

        // ------------------------------------------------------------- private field ----

        private static AccessTools.FieldRef<Minimap, BitArray> _exploredOf;
        private static bool _exploredBound;

        /// <summary>Minimap.m_explored, bound lazily - see Reflect.</summary>
        private static AccessTools.FieldRef<Minimap, BitArray> ExploredOf()
        {
            if (_exploredBound) return _exploredOf;
            _exploredBound = true;

            _exploredOf = Reflect.Field<Minimap, BitArray>("m_explored", "the Discovery bar of a foothold");
            return _exploredOf;
        }

        private static BitArray Bits(Minimap map)
        {
            var of = ExploredOf();
            return of == null ? null : of(map);
        }
    }
}

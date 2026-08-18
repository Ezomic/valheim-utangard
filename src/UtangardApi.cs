namespace Utangard
{
    /// <summary>
    /// What other mods may ask Utangard.
    ///
    /// This exists because Yoke scales stack sizes by world progression, and "the world has
    /// seen this boss die" is not what this mod means by earned - a biome opens only when
    /// every member of the group was personally at that death. Those two answers part company
    /// the moment somebody is offline for a kill, and a Yoke reading the raw defeated_ key
    /// would hand out plains-era stacks for a biome still fenced off here: two of our own mods
    /// disagreeing out loud about the same word, in a way that reads as a bug in whichever one
    /// the player happens to be looking at.
    ///
    /// A façade rather than making Progression public, because Progression is where the
    /// roster, the latch and the deadline live and none of that is anyone else's business.
    /// Read-only by construction: nothing here writes a key, so a consumer cannot open a
    /// biome by asking about it.
    /// </summary>
    public static class UtangardApi
    {
        /// <summary>
        /// Whether the group has earned this boss - every member present at the kill, or the
        /// latch set, or the catch-up deadline run out. Falls back to the world key when
        /// there is no roster to speak for, which is what a fresh or solo world looks like.
        ///
        /// The boss key is a global key name, e.g. "defeated_bonemass".
        /// </summary>
        public static bool GroupHasKey(string bossKey)
        {
            return Progression.GroupHasKey(bossKey);
        }
    }
}

using System;
using HarmonyLib;

namespace Utangard
{
    /// <summary>
    /// A boss will not come to an altar in a biome the group has not earned.
    ///
    /// Robbin's rule, 2026-09-24. Without it the gate had a back door: the penalties make a
    /// locked biome hard to live in, but nothing stopped a player carrying a Moder egg into
    /// the Mountains before the group had opened them, summoning her, and killing her there -
    /// and a boss kill is what starts the next biome's catch-up deadline. One player could
    /// set the Plains clock running for everybody while the rest were still on Bonemass.
    ///
    /// There is no deadlock in refusing, and it is worth saying why, because it looks like
    /// one. Every boss's altar stands in the biome the previous boss opens: the Elder's in the
    /// Black Forest that Eikthyr opens, Bonemass's in the Swamp that the Elder opens, Moder's in
    /// the Mountains that Bonemass opens, and so on. An altar is therefore only ever refused
    /// while its own biome is still shut, and it answers again the moment the group opens that
    /// biome - by kills, or when the deadline runs out, which needs no boss at all.
    ///
    /// Asked of the world, not of the player. Earned() is the group's answer, the same one
    /// the compendium page and the openings watcher read, so an altar and the gate cannot
    /// disagree about whether a biome is open. A player's own foothold in a locked biome
    /// (LHM-26) does not open its altar; that is a different promise.
    ///
    /// InitiateSpawnBoss is the one seam both kinds of altar go through - using an item on it
    /// (trophies, eggs, bones) and pressing E on one that takes its offering on item stands
    /// (Fader's bells). The offering is only taken inside the RPC this method sends, so
    /// refusing here spawns nothing and costs nothing: the trophies stay in the bag and the
    /// bells stay on their stands.
    ///
    /// Not covered: a boss that is placed rather than summoned. The Queen waits behind a door
    /// in the Mistlands and never goes near an OfferingBowl.
    /// </summary>
    internal static class BossAltars
    {
        private static bool _saidFailure;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(OfferingBowl), "InitiateSpawnBoss")]
        private static bool Refuse(OfferingBowl __instance)
        {
            try
            {
                if (!UtangardConfig.Enabled.Value || !UtangardConfig.BlockBossSummons.Value) return true;

                // Fail open the way the penalties do: if a game update has broken the code that
                // records boss kills, a shut biome could never open again, and refusing its
                // altar as well would make that permanent.
                if (!Seams.PenaltyIsEscapable()) return true;

                var zone = ZoneSystem.instance;
                if (zone == null || __instance == null) return true;

                // FindBiome compares X and Z only, so an altar under a roof or inside a dungeon
                // answers for the biome above it, which is the biome that owns it.
                var key = UtangardConfig.RequiredKeyFor(Heightmap.FindBiome(__instance.transform.position));
                if (key == null || BiomeGate.Earned(zone, key)) return true;

                var player = Player.m_localPlayer;
                if (player != null)
                    player.Message(MessageHud.MessageType.Center, UtangardConfig.BossBlockedMessage.Value);

                if (UtangardConfig.Verbose.Value)
                    UtangardPlugin.Log.LogInfo("Refused an offering at " + __instance.name + ": its biome needs "
                                               + key + ", and the group has not earned it.");

                return false;
            }
            catch (Exception e)
            {
                // Never let a check about a rule break the altar itself.
                if (!_saidFailure)
                {
                    _saidFailure = true;
                    UtangardPlugin.Log.LogWarning("Could not check a boss altar against the gate, so it is "
                                                  + "allowed: " + e);
                }
                return true;
            }
        }
    }
}

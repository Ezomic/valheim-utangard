using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Utangard
{
    /// <summary>
    /// Applies the patches one seam at a time, remembers which ones took, and refuses to
    /// enforce a penalty the group would have no way out of.
    ///
    /// Why this exists at all is the Valheim 1.0 launch. Every patch used to go on with a
    /// single PatchAll(typeof(UtangardPatches)). SEMan.AddStatusEffect had gained a trailing
    /// `short variant = -1`, so the pinned five-type signature no longer named a method,
    /// PatchAll threw ArgumentException the moment it reached it, and Harmony stopped there -
    /// every patch declared after that one simply never went on. Utangard was completely
    /// inert while still registering with Core's version gate, which went on refusing
    /// mismatched clients on behalf of a mod that was not running. The exception went to
    /// Player.log, so the log people read said nothing was wrong.
    ///
    /// One class per seam and one try/catch per class makes a broken signature cost exactly
    /// the feature that sits on it. That is strictly better than all-or-nothing in every case
    /// but one, and that one is the reason for the second half of this file: partial patching
    /// can leave the punishment installed and the way out missing.
    /// </summary>
    internal static class Seams
    {
        /// <summary>Player.UpdateFood - the drain, the messages, and the progress publish.</summary>
        internal static bool FoodTick;

        /// <summary>Player.CanConsumeItem - the refused bite and the refused drink.</summary>
        internal static bool Eating;

        /// <summary>SEMan.AddStatusEffect - a buff being applied fresh.</summary>
        internal static bool NewBuffs;

        /// <summary>SEMan.Internal_AddStatusEffect - a buff already running being refreshed.</summary>
        internal static bool BuffRefresh;

        /// <summary>Player.StartGuardianPower - the power refused before it is spent.</summary>
        internal static bool GuardianPowers;

        /// <summary>Character.OnDeath - crediting everyone standing at the kill.</summary>
        internal static bool KillCredit;

        /// <summary>
        /// ZoneSystem.RPC_GlobalKeys, both halves of it.
        ///
        /// Read by the prefix itself, which is the point. Harmony applies a class's patches
        /// one method at a time, so a change that failed the postfix could otherwise leave
        /// the prefix installed and Progression.Settling stuck true for the life of the
        /// process - which would permanently disable the latch and the roster cache while
        /// looking like nothing at all had gone wrong. The prefix therefore raises the flag
        /// only once this says the postfix that lowers it is certainly installed too.
        /// </summary>
        internal static bool KeyArrival;

        /// <summary>ZoneSystem.GlobalKeyAdd - dropping the cached roster when a key lands.</summary>
        internal static bool KeyChange;

        /// <summary>ObjectDB.Awake - the buff set and the borrowed icons, for a local world.</summary>
        internal static bool EffectsLocal;

        /// <summary>ObjectDB.CopyOtherDB - the same, for a database handed over by a host.</summary>
        internal static bool EffectsJoined;

        /// <summary>TextsDialog.UpdateTextsList - the compendium page.</summary>
        internal static bool CompendiumPage;

        /// <summary>Player.OnSpawned - the diagnostics dump and the unenforceable-gate warning.</summary>
        internal static bool Spawn;

        /// <summary>What did not go on, in the words the log will use.</summary>
        private static readonly List<string> Broken = new List<string>();

        /// <summary>
        /// Put every seam on, each under its own guard, then say plainly what the session got.
        /// </summary>
        internal static void Apply(Harmony harmony)
        {
            FoodTick = Patch(harmony, typeof(UtangardPatches.FoodTick),
                "the food drain and the progress publish (Player.UpdateFood)");

            Eating = Patch(harmony, typeof(UtangardPatches.Eating),
                "the refusal to eat or drink (Player.CanConsumeItem)");

            NewBuffs = Patch(harmony, typeof(UtangardPatches.NewBuffs),
                "the refusal of new buffs (SEMan.AddStatusEffect)");

            BuffRefresh = Patch(harmony, typeof(UtangardPatches.BuffRefresh),
                "the refusal of refreshed buffs (SEMan.Internal_AddStatusEffect)");

            GuardianPowers = Patch(harmony, typeof(UtangardPatches.GuardianPowers),
                "the refusal of guardian powers (Player.StartGuardianPower)");

            KillCredit = Patch(harmony, typeof(UtangardPatches.KillCredit),
                "crediting everyone at a boss kill (Character.OnDeath)");

            KeyArrival = Patch(harmony, typeof(UtangardPatches.KeyArrival),
                "holding the latch shut while world keys arrive (ZoneSystem.RPC_GlobalKeys)");

            KeyChange = Patch(harmony, typeof(UtangardPatches.KeyChange),
                "dropping the cached roster when a key changes (ZoneSystem.GlobalKeyAdd)");

            EffectsLocal = Patch(harmony, typeof(UtangardPatches.EffectsLocal),
                "reading the buff set from a local world (ObjectDB.Awake)");

            EffectsJoined = Patch(harmony, typeof(UtangardPatches.EffectsJoined),
                "reading the buff set from a host (ObjectDB.CopyOtherDB)");

            CompendiumPage = Patch(harmony, typeof(UtangardPatches.CompendiumPage),
                "the compendium page (TextsDialog.UpdateTextsList)");

            Spawn = Patch(harmony, typeof(UtangardPatches.Spawn),
                "the diagnostics dump on spawn (Player.OnSpawned)");

            Report();
        }

        private static bool Patch(Harmony harmony, Type seam, string what)
        {
            try
            {
                harmony.PatchAll(seam);
                return true;
            }
            catch (Exception e)
            {
                Broken.Add(what);

                // The whole exception, not e.Message. The one thing this line has to be good
                // for is a bug report written by somebody who cannot read the code, and the
                // target-resolution failures that land here name the method they could not
                // find only in the stack.
                UtangardPlugin.Log.LogError(
                    "Utangard could not patch " + what + " - that part of the mod is off for "
                    + "this session, the rest still applies. " + e);

                return false;
            }
        }

        /// <summary>
        /// One line either way, because "which of these applied" is the first question worth
        /// asking after a game update and the answer should not require reading further.
        /// </summary>
        private static void Report()
        {
            if (Broken.Count == 0)
            {
                UtangardPlugin.Log.LogInfo("All seams patched cleanly.");
                return;
            }

            UtangardPlugin.Log.LogError(
                "Utangard is running DEGRADED - " + Broken.Count + " seam(s) did not apply: "
                + string.Join("; ", Broken.ToArray()) + ". A game update has probably changed "
                + "those methods. Everything else is still in force.");
        }

        private static bool _saidWhyNot;

        /// <summary>
        /// Whether a player the gate shuts out has any road back in.
        ///
        /// This is the one place a missing patch is allowed to change the rules rather than
        /// only soften them, and the reason is that the harm is not symmetrical. A missing
        /// refusal costs the mod some of its bite for a session, and a server owner can wait
        /// for a fix. A missing *credit* seam costs the group the gate itself: nothing is ever
        /// recorded as done, so GroupHasKey answers no for every member forever, so the latch
        /// never writes - and because the catch-up clock is only ever started by the first
        /// credit, no deadline ever runs out either. That is not a weaker mod, it is a world
        /// where a biome that shut can never open again, with no in-game way to learn why.
        ///
        /// Refusing to enforce is a failure a fix can undo. Starving a server for a week is
        /// not, so this fails towards letting people play.
        ///
        /// Credit only ever enters a world through two doors, and this asks whether either is
        /// still standing.
        /// </summary>
        internal static bool PenaltyIsEscapable()
        {
            if (Escapable()) return true;

            if (!_saidWhyNot)
            {
                _saidWhyNot = true;

                UtangardPlugin.Log.LogError(
                    "Utangard is NOT withering anybody. The group gate is on, but neither way "
                    + "of recording that a boss was killed survived patching, so a biome that "
                    + "is shut on this world could never open again - and a deadline cannot "
                    + "rescue it either, because the catch-up clock only starts on the first "
                    + "credit. Update the mod, or set Gate.GateOnGroup = false to gate on the "
                    + "world's own keys instead, which needs none of our patches to open.");
            }

            return false;
        }

        private static bool Escapable()
        {
            // Gating on the world's own key needs nothing of ours to open it. Vanilla writes
            // the defeat key when the boss dies, wherever it dies, and every client
            // replicates it - so the road out exists whatever became of these patches.
            if (!UtangardConfig.GateOnGroup.Value) return true;

            // The kill itself. Character.OnDeath -> CreditAttendees is the only thing that
            // records who was standing there when it died, and it is also what starts the
            // catch-up clock.
            if (KillCredit) return true;

            // The backfill. Player.UpdateFood -> PublishLocal reads the character's own
            // m_uniques on a timer and credits any boss this world has already seen die, so a
            // group can still turn a kill into progress on their next spawn - but only if the
            // tick that carries it went on, the owner has left it switched on, and the
            // private field it reads was still there to bind.
            return FoodTick
                && UtangardConfig.BackfillFromCharacter.Value
                && !Progression.BackfillUnavailable;
        }
    }
}

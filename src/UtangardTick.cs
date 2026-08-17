using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// The per-tick half of the mod: the drains, and the effects that carry them.
    ///
    /// It rides Player.UpdateFood rather than Update or a MonoBehaviour of its own, for
    /// three reasons that are all the same reason. UpdateFood is where the food timers live,
    /// so the drain is applied next to what it drains. It is called from UpdateStats, which
    /// already refuses to run during the intro and mid-teleport - two windows where starving
    /// someone would be a bug. And it comes with the dt the rest of the game is using, so
    /// there is no second clock to keep in step.
    /// </summary>
    internal static class UtangardTick
    {
        private static readonly AccessTools.FieldRef<Player, List<Player.Food>> FoodsOf =
            AccessTools.FieldRefAccess<Player, List<Player.Food>>("m_foods");

        // m_time is protected on StatusEffect. The Sapped effect gets at its own copy by
        // inheriting; draining somebody else's needs the field directly.
        private static readonly AccessTools.FieldRef<StatusEffect, float> TimeOf =
            AccessTools.FieldRefAccess<StatusEffect, float>("m_time");

        /// <summary>Last gate state, so the two messages fire on the edge and not per frame.</summary>
        private static bool _wasWithered;

        /// <summary>
        /// Which player that state belongs to. Death and logout hand you a new Player object,
        /// and without this the next one inherits the last one's state and opens with a
        /// message about leaving somewhere it has never been.
        /// </summary>
        private static Player _watching;

        /// <summary>
        /// Seconds between publishing the local character's progress. Not per frame, because
        /// a boss killed at any moment lands its credit in m_uniques a frame later and there
        /// is no event to hook - but the publish checks before it writes, so this interval
        /// governs how often a few dictionary lookups happen, not how much traffic there is.
        /// </summary>
        private const float PublishInterval = 5f;

        private static float _publishTimer;

        public static void Run(Player player, float dt)
        {
            if (player == null) return;

            // Player.FixedUpdate already refuses to reach UpdateStats unless it owns the
            // character and it is the local one, so this is belt and braces - but publishing
            // uses the local character's private unique-key set while writing under whichever
            // player it was handed, and that pairing has to be true here, not two assemblies
            // away in code nobody in this repo controls.
            if (player != Player.m_localPlayer) return;

            if (!ReferenceEquals(_watching, player))
            {
                _watching = player;
                _wasWithered = false;
                _publishTimer = PublishInterval;   // publish immediately for a new character
            }

            if (UtangardConfig.GateOnGroup.Value)
            {
                _publishTimer += dt;
                if (_publishTimer >= PublishInterval)
                {
                    _publishTimer = 0f;
                    Progression.PublishLocal(player);
                }
            }

            bool withered = BiomeGate.IsWithered(player);

            if (withered != _wasWithered)
            {
                _wasWithered = withered;
                Announce(player, withered);
            }

            if (!withered) return;

            UtangardEffectsRegistry.Ensure(player);
            DrainFood(player, dt);
            DrainBuffs(player, dt);
        }

        /// <summary>
        /// Burn the food down faster.
        ///
        /// Only m_time is touched, not the health and stamina values derived from it. Vanilla
        /// recomputes those from m_time once a second and removes anything that has run out,
        /// so pushing the timer past zero is enough - the numbers on the HUD follow within a
        /// second and the "your food is depleted" message still comes from the game. Writing
        /// the derived values here as well would mean two pieces of code owning them, and
        /// the second one would lose the moment vanilla's next tick landed.
        /// </summary>
        private static void DrainFood(Player player, float dt)
        {
            float extra = (UtangardConfig.FoodDrainMultiplier.Value - 1f) * dt;
            if (extra <= 0f) return;

            List<Player.Food> foods = FoodsOf(player);
            if (foods == null) return;

            for (int i = 0; i < foods.Count; i++)
            {
                Player.Food food = foods[i];
                if (food != null) food.m_time -= extra;
            }
        }

        /// <summary>
        /// Burn already-running buffs down faster.
        ///
        /// Same set as the one that gets blocked, which is deliberate: a rule the player can
        /// state in one sentence ("buffs do not work here") is worth more than a finely
        /// tuned pair of lists. Effects with no duration are skipped, because there is
        /// nothing to shorten - an effect with m_ttl of zero never expires on time at all.
        /// </summary>
        private static void DrainBuffs(Player player, float dt)
        {
            float extra = (UtangardConfig.BuffDrainMultiplier.Value - 1f) * dt;
            if (extra <= 0f) return;

            SEMan seman = player.GetSEMan();
            if (seman == null) return;

            List<StatusEffect> effects = seman.GetStatusEffects();
            for (int i = 0; i < effects.Count; i++)
            {
                StatusEffect effect = effects[i];
                if (effect == null || effect.m_ttl <= 0f) continue;
                if (!BlockedEffects.IsBlocked(effect)) continue;

                TimeOf(effect) += extra;
            }
        }

        private static void Announce(Player player, bool withered)
        {
            string message = withered
                ? UtangardConfig.EnterMessage.Value
                : UtangardConfig.LeaveMessage.Value;

            // Naming who is missing is not decoration. Under a group gate the honest question
            // a player asks is "why is this still shut when I killed it", and without an
            // answer the only way to find one is to read a log file.
            string blockedBy = withered ? BiomeGate.BlockersHere(player) : null;
            //
            // Plain text and not a $token: Message runs the string through
            // Localization.Localize, and an unregistered token renders as the raw word
            // rather than as anything a player would want to read.
            if (withered && !string.IsNullOrEmpty(blockedBy)
                && UtangardConfig.NameTheBlockers.Value)
            {
                message = message + "\n" + UtangardConfig.BlockedByPrefix.Value + " " + blockedBy;

                // A deadline nobody can see is not pressure, it is a surprise. Shown with the
                // names, because "who" and "how long" are the same question to a player
                // deciding whether to go and fetch somebody.
                string key = UtangardConfig.RequiredKeyFor(player.GetCurrentBiome());
                long left = key == null ? -1L : Progression.SecondsLeft(key);
                if (left >= 0L) message = message + " (" + GlobalKeyDump.Describe(left) + " left)";
            }

            if (!string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message);

            if (UtangardConfig.Verbose.Value)
                UtangardPlugin.Log.LogInfo(
                    (withered ? "Gate closed in " : "Gate opened in ") + player.GetCurrentBiome()
                    + (string.IsNullOrEmpty(blockedBy) ? "" : " - waiting on " + blockedBy));
        }
    }
}

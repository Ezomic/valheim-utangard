using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wither
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
    internal static class WitherTick
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

        public static void Run(Player player, float dt)
        {
            if (player == null) return;

            if (!ReferenceEquals(_watching, player))
            {
                _watching = player;
                _wasWithered = false;
            }

            bool withered = BiomeGate.IsWithered(player);

            if (withered != _wasWithered)
            {
                _wasWithered = withered;
                Announce(player, withered);
            }

            if (!withered) return;

            WitherEffectsRegistry.Ensure(player);
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
            float extra = (WitherConfig.FoodDrainMultiplier.Value - 1f) * dt;
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
            float extra = (WitherConfig.BuffDrainMultiplier.Value - 1f) * dt;
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
                ? WitherConfig.EnterMessage.Value
                : WitherConfig.LeaveMessage.Value;

            if (!string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message);

            if (WitherConfig.Verbose.Value)
                WitherPlugin.Log.LogInfo(
                    (withered ? "Gate closed in " : "Gate opened in ") + player.GetCurrentBiome());
        }
    }
}

using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// What you carry while the gate is closed on you: the icon, and the one penalty the
    /// game will only take through a status effect.
    ///
    /// It was pure signage first, and the drain and the refusals still live in the patches -
    /// they are edits to timers and to answers, and doing them from here would mean two
    /// places to look when a number is wrong. Health regeneration is different in kind.
    /// Player.UpdateFood asks SEMan for a multiplier and applies it (see
    /// ModifyHealthRegen below), so the seam vanilla offers is an effect, and a patch would
    /// be reimplementing a hook that already exists.
    ///
    /// It earns its keep as signage too: without it, "you cannot eat" is a message that
    /// flashes once and then a mystery, and the tooltip is the only thing in the mod that can
    /// say which boss you are missing.
    /// </summary>
    internal sealed class UtangardMarker : StatusEffect
    {
        /// <summary>
        /// No wounds close here.
        ///
        /// Valheim's only passive healing is food: UpdateFood adds up every meal's
        /// m_foodRegen every ten seconds, asks SEMan to modify it, and heals you by the
        /// result. So this multiplies exactly the healing the food you are not allowed to eat
        /// would have given, which is why it is the same rule rather than a second one - the
        /// biome that will not feed you does not mend you either.
        ///
        /// Multiplied in alongside every other effect's, like Sapped's stamina figure, so it
        /// composes with vanilla rather than overriding it.
        ///
        /// Left alone entirely for a character with a full foothold in the biome whose rules
        /// apply here (LHM-26): both bars full means multiplier 1, normal healing. The rest of
        /// the marker's work goes on. Asked once per regen, which vanilla does every ten
        /// seconds, so the kill tally and the map count are read that often and no more.
        /// </summary>
        public override void ModifyHealthRegen(ref float regenMultiplier)
        {
            if (Foothold.HealingAllowed(m_character as Player)) return;

            regenMultiplier *= Mathf.Clamp01(UtangardConfig.HealthRegenMultiplier.Value);
        }

        /// <summary>
        /// Ends the moment the player leaves. The gate re-adds it every tick while inside,
        /// so this is the whole lifecycle: exists in a gated biome, gone anywhere else.
        /// </summary>
        public override bool IsDone()
        {
            return !BiomeGate.IsWithered(m_character as Player);
        }

        public override string GetIconText()
        {
            return "";
        }

        /// <summary>
        /// The tooltip, plus a line for each rule that is actually in force on this character.
        ///
        /// Built here rather than baked into m_tooltip at construction because every line is
        /// live: a server that syncs a different multiplier, a player who changes one in
        /// ConfigurationManager, and since LHM-26 a character whose foothold has just filled,
        /// would otherwise read a description of a rule that no longer applies to them. Vanilla
        /// asks for this string every time it draws the tooltip, so there is nothing to
        /// invalidate.
        /// </summary>
        public override string GetTooltipString()
        {
            Player player = m_character as Player;
            var text = new System.Text.StringBuilder(m_tooltip);

            if (UtangardConfig.BlockEating.Value)
                text.Append('\n').Append(Foothold.EatingAllowed(player)
                    ? "You have fought enough here to eat."
                    : "Nothing you eat will take hold here.");

            if (UtangardConfig.BlockNewBuffs.Value)
                text.Append('\n').Append("Meads and powers will not take hold here.");

            float regen = Mathf.Clamp01(UtangardConfig.HealthRegenMultiplier.Value);
            if (regen < 1f)
                text.Append('\n').Append(Foothold.HealingAllowed(player)
                    ? "You know this place well enough that your wounds close."
                    : regen <= 0f ? "Wounds do not close here." : "Wounds close slowly here.");

            return text.ToString();
        }
    }

    /// <summary>
    /// The grudge. Seventy-five percent less stamina regeneration, and it follows you out.
    ///
    /// One second inside buys one second of it, up to a ceiling, and it only spends itself
    /// once you are somewhere the land tolerates you. So the shape of the penalty is: a dash
    /// into the Plains for a barley plant costs you a slow half-minute afterwards, and
    /// living there is a permanent tax rather than an escalating one.
    ///
    /// Timing note. Valheim tracks a status effect by elapsed time, not remaining time -
    /// m_time counts up and IsDone fires when it passes m_ttl. So "charging" the effect
    /// means pushing m_time backwards, and the effect starts at m_time == m_ttl with nothing
    /// left rather than at zero with everything. That is why Setup fills it and the charge
    /// empties it, which reads backwards until you know that.
    /// </summary>
    internal sealed class SappedEffect : StatusEffect
    {
        /// <summary>
        /// Arrive empty. A player who touches the border for one frame should walk away with
        /// one frame of penalty, not the full thirty seconds.
        /// </summary>
        public override void Setup(Character character)
        {
            base.Setup(character);
            m_ttl = Ceiling();
            m_time = m_ttl;
        }

        public override void UpdateStatusEffect(float dt)
        {
            // Re-read the ceiling every tick so editing MaxSeconds in ConfigurationManager
            // takes hold immediately. Lowering it below what is currently banked expires the
            // effect on this same pass, which is the behaviour you want from a slider.
            m_ttl = Ceiling();

            // Charge before the base ticks. SEMan.Update calls UpdateStatusEffect and then
            // IsDone in one pass, in that order, so an effect topped up here is never
            // collected on the frame it was added - which is exactly the race that would
            // otherwise make it flicker on and off at the biome border.
            if (BiomeGate.IsWithered(m_character as Player))
            {
                // Twice dt, because base.UpdateStatusEffect is about to give one of them
                // back. Net effect: a second of standing there is a second of banked
                // penalty. Clamped at zero, which is the ceiling expressed in elapsed time.
                m_time = Mathf.Max(0f, m_time - dt * 2f);
            }

            base.UpdateStatusEffect(dt);
        }

        /// <summary>
        /// This is the whole mechanic. SEMan multiplies every effect's contribution
        /// together, so it stacks with food and Rested rather than overriding them - a
        /// sapped player with good food still regenerates faster than a sapped player
        /// without, just badly.
        /// </summary>
        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            staminaRegen *= Mathf.Clamp01(UtangardConfig.SappedStaminaRegen.Value);
        }

        private static float Ceiling()
        {
            // Never zero. A zero ttl means "no duration" to IsDone, which would make the
            // effect permanent - the opposite of what setting the ceiling to zero means.
            return Mathf.Max(0.1f, UtangardConfig.SappedMaxSeconds.Value);
        }
    }
}

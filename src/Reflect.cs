using System;
using HarmonyLib;

namespace Utangard
{
    /// <summary>
    /// The one place this mod reaches for a private vanilla field, and the one place that
    /// reach is allowed to fail.
    ///
    /// Every AccessTools.FieldRefAccess in Utangard goes through here, and none of them is
    /// bound in a static field initialiser any more. That is not a style preference, and it
    /// is not hypothetical. FieldRefAccess throws when the field it names has been renamed or
    /// removed, and a throw inside a static initialiser poisons the whole type: from that
    /// moment every Harmony patch declared in that class throws TypeInitializationException
    /// instead of running its body. The symptom is never "Utangard is broken" - it is some
    /// unrelated vanilla mechanic failing, up to and including the world refusing to finish
    /// what it was doing - and because it happens mid-frame it lands in
    /// AppData\LocalLow\IronGate\Valheim\Player.log rather than in BepInEx's LogOutput, so the
    /// log everybody actually reads stays perfectly clean while the game misbehaves.
    ///
    /// So a binding is made on first use, inside a try/catch, exactly once. A field a game
    /// update has taken away then costs precisely the feature that needed it, says so in a
    /// line worth grepping for, and leaves every other patch in its class alone.
    /// </summary>
    internal static class Reflect
    {
        /// <summary>
        /// Bind a private field, or return null and say loudly what that cost.
        ///
        /// Callers cache the result behind their own "already tried" flag rather than
        /// retrying: a field that is not there on the first frame will not appear later, and
        /// a failed lookup that repeats turns one grep-able error into a log full of them.
        /// </summary>
        /// <param name="field">The vanilla field name, spelled as the game spells it.</param>
        /// <param name="cost">
        /// What stops working without it, in the words a player would use. This is the whole
        /// value of the log line: "could not bind m_foods" tells the reader nothing they can
        /// act on, "the faster food burn is off" tells them what to expect and what to report.
        /// </param>
        internal static AccessTools.FieldRef<TObject, TField> Field<TObject, TField>(
            string field, string cost)
        {
            try
            {
                AccessTools.FieldRef<TObject, TField> bound =
                    AccessTools.FieldRefAccess<TObject, TField>(field);

                if (bound != null) return bound;

                // Harmony throws rather than returning null on every version this mod has
                // been built against, but a delegate that came back null would sail straight
                // past a try/catch and throw a NullReferenceException at the call site
                // instead - somewhere with none of this context to report.
                Fail(field, typeof(TObject).Name, cost, "the lookup returned nothing.");
            }
            catch (Exception e)
            {
                Fail(field, typeof(TObject).Name, cost, e.ToString());
            }

            return null;
        }

        private static void Fail(string field, string owner, string cost, string why)
        {
            // LogError rather than a warning. This can only happen because a game update
            // moved a field under us, and the entire point of naming it here is that somebody
            // greps for it on patch day instead of discovering it by playing.
            //
            // Log is checked rather than assumed: binding is lazy, so in principle the first
            // call could come from somewhere that runs before the plugin's Awake, and a
            // NullReferenceException raised while reporting a failure would hide the failure.
            if (UtangardPlugin.Log == null) return;

            UtangardPlugin.Log.LogError(
                "Utangard could not bind " + owner + "." + field + " - " + cost
                + " is off for this session. A game update has probably renamed it. " + why);
        }
    }
}

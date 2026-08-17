using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// Decides what counts as a buff.
    ///
    /// The obvious implementation is a hardcoded list of status effect names, and it is
    /// wrong for the same reason Stow does not list items: the game keeps adding potions,
    /// and a list goes stale the first time it does. So the set is read off the game's own
    /// data instead - anything an item applies when you consume it is a potion or a mead by
    /// definition, whatever it ends up being called.
    ///
    /// Everything not caught here passes through untouched, which is the important half.
    /// Wet, Cold, Freezing, Burning, Poison, Frost, Smoked, Tared and Spirit are how the
    /// game does damage and weather; blocking those would not make the mod harsher, it would
    /// make the biome safer. An allowlist of harmful effects was written first and thrown
    /// away for exactly that reason - it can only ever be as complete as the day it was
    /// written, and every gap in it hands the player an immunity.
    /// </summary>
    internal static class BlockedEffects
    {
        /// <summary>Guardian powers all share this prefix, and nothing else does.</summary>
        private const string GuardianPrefix = "GP_";

        private static readonly HashSet<int> Blocked = new HashSet<int>();
        private static bool _built;

        /// <summary>
        /// Rebuild from the current ObjectDB.
        ///
        /// Called from both ObjectDB.Awake and ObjectDB.CopyOtherDB, because both really
        /// happen: a local world builds the database, and joining a server replaces it
        /// wholesale with the host's. A set built only on Awake is quietly stale the moment
        /// you connect to anything.
        /// </summary>
        public static void Rebuild()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null) return;

            Blocked.Clear();

            // 1. Anything an item hands you for eating or drinking it. This is every potion
            //    and every mead, and it costs one walk of the item list.
            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null) continue;

                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                    continue;

                StatusEffect consumed = drop.m_itemData.m_shared.m_consumeStatusEffect;
                if (consumed != null) Add(consumed);
            }

            // 2. The guardian powers, by prefix.
            foreach (StatusEffect effect in db.m_StatusEffects)
            {
                if (effect == null) continue;
                if (effect.name != null && effect.name.StartsWith(GuardianPrefix))
                    Add(effect);
            }

            // 3. Rested and Resting. These are not potions and have no prefix in common with
            //    anything, so they can only be named. They are also the difference between a
            //    harsh mod and a brutal one, hence the switch.
            if (UtangardConfig.BlockRested.Value)
            {
                Blocked.Add(SEMan.s_statusEffectRested);
                Blocked.Add(SEMan.s_statusEffectResting);
            }

            // 4. Whatever the config adds, then whatever it takes back out. Exemptions run
            //    last so NeverBlock beats every rule above it, including its own AlsoBlock.
            foreach (string name in UtangardConfig.AlsoBlockNames)
                Blocked.Add(name.GetStableHashCode());

            foreach (string name in UtangardConfig.NeverBlockNames)
                Blocked.Remove(name.GetStableHashCode());

            _built = true;

            UtangardPlugin.Log.LogInfo("Buff set rebuilt: " + Blocked.Count + " status effects.");
            if (UtangardConfig.LogBlockedEffects.Value) LogNames(db);
        }

        private static void Add(StatusEffect effect)
        {
            if (UtangardConfig.NeverBlockNames.Contains(effect.name)) return;
            Blocked.Add(effect.NameHash());
        }

        /// <summary>
        /// Whether this effect is one of the ones the gate takes away.
        ///
        /// Fails open when the set has not been built - before ObjectDB exists there is no
        /// way to tell a potion from a poison, and guessing in that window would block the
        /// spawn-in effects.
        /// </summary>
        public static bool IsBlocked(StatusEffect effect)
        {
            if (!_built || effect == null) return false;
            if (effect is UtangardMarker || effect is SappedEffect) return false;
            return Blocked.Contains(effect.NameHash());
        }

        /// <summary>
        /// The same question asked with a hash, for the paths that never resolve the effect
        /// to an object - SEMan refreshes an already-running effect by hash alone and never
        /// touches the StatusEffect the caller asked for.
        /// </summary>
        public static bool IsBlockedHash(int nameHash)
        {
            if (!_built) return false;
            if (nameHash == UtangardEffectsRegistry.MarkerHash) return false;
            if (nameHash == UtangardEffectsRegistry.SappedHash) return false;
            return Blocked.Contains(nameHash);
        }

        /// <summary>
        /// Names, for the config comment to send people to. Printed from ObjectDB rather
        /// than from the hash set because a hash cannot be turned back into a name, and the
        /// whole point of the dump is to give the user something to paste into NeverBlock.
        /// </summary>
        private static void LogNames(ObjectDB db)
        {
            var caught = new List<string>();
            var passed = new List<string>();

            foreach (StatusEffect effect in db.m_StatusEffects)
            {
                if (effect == null) continue;
                (Blocked.Contains(effect.NameHash()) ? caught : passed).Add(effect.name);
            }

            caught.Sort();
            passed.Sort();

            UtangardPlugin.Log.LogInfo("Treated as buffs (blocked and drained):");
            UtangardPlugin.Log.LogInfo("  " + Join(caught));
            UtangardPlugin.Log.LogInfo("Left alone:");
            UtangardPlugin.Log.LogInfo("  " + Join(passed));
        }

        private static string Join(List<string> names)
        {
            if (names.Count == 0) return "(none)";

            var text = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(names[i]);
            }
            return text.ToString();
        }
    }
}

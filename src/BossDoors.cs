using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// The Queen's door stays sealed while the group has not earned the Mistlands.
    ///
    /// Robbin, 2026-09-26, the day the altar rule was finished: "yes block the queen door too".
    /// She is the one boss the altar rule could not reach, because nobody summons her. She waits
    /// in a Dvergr ruin behind a door that opens with the Sealbreaker, so the altar's question is
    /// asked at the door instead, through the same BossAltars.Unearned - the door and the altars
    /// cannot disagree about a biome, and BlockBossSummons turns both off.
    ///
    /// Picked out by the key the door wants (Door.m_keyItem), not by the door's own prefab name.
    /// The key is what makes it a boss door, and a list of keys keeps another sealed boss door one
    /// config line away. Crypt keys are left out: Robbin asked for the Queen, and a swamp crypt is
    /// loot, not a boss.
    ///
    /// Vanilla has two ways into a keyed door and spends the key in both before the door moves:
    /// Interact (E with the key in the pack) and UseItem (the key used from the hotbar). Refusing
    /// in a prefix of each spends nothing. A door that is already open is left alone: vanilla never
    /// lets a keyed door close again, so nothing here can seal anybody inside with her.
    ///
    /// No deadlock, for the altars' reason. The Mistlands open with Yagluth or with the deadline,
    /// and the Queen is what opens the Ashlands, not the Mistlands.
    /// </summary>
    internal static class BossDoors
    {
        private static bool _saidFailure;

        private static string _parsedFrom;
        private static readonly HashSet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Arguments are positional (__0, __1) and the door's ZNetView is fetched rather than
        // injected as ___m_nview, by the rule at the top of UtangardPatches: Harmony binds by
        // name, a renamed parameter or private field is a throw, and a throw here costs the
        // whole class. Door.Awake takes its m_nview with GetComponent<ZNetView>() on the same
        // object, so asking for it the same way finds the same component, and only on a press.

        /// <param name="__0">The one pressing. Positional: vanilla calls it `character` today.</param>
        /// <param name="__1">Held, rather than tapped. Vanilla calls it `hold`.</param>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Door), nameof(Door.Interact))]
        private static bool RefuseInteract(Door __instance, Humanoid __0, bool __1, ref bool __result)
        {
            // Vanilla ignores a held E on a door, and so does this.
            if (__1) return true;
            if (!Refused(__instance, __0)) return true;

            // Handled: the player was told why, and nothing else should act on the press.
            __result = true;
            return false;
        }

        /// <param name="__0">The user. Vanilla calls it `user` today.</param>
        /// <param name="__1">The item used on the door. Vanilla calls it `item`.</param>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Door), nameof(Door.UseItem))]
        private static bool RefuseUseItem(Door __instance, Humanoid __0, ItemDrop.ItemData __1, ref bool __result)
        {
            // UseItem answers false for any item that is not this door's key, and the hotbar then
            // tries the item elsewhere. Only the key itself is this file's business.
            if (__instance == null || __1 == null || __1.m_shared == null || __instance.m_keyItem == null) return true;
            if (__1.m_shared.m_name != __instance.m_keyItem.m_itemData.m_shared.m_name) return true;
            if (!Refused(__instance, __0)) return true;

            __result = true;
            return false;
        }

        /// <summary>
        /// Says once per world which doors BossDoorKeys actually guards. That the Queen's door is
        /// a Door with the Sealbreaker for its key is asset data, readable only in the running
        /// game, and this rule was written without having seen it - so the log carries the proof,
        /// or the warning that the default guards nothing.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static void ReportGuarded(ZNetScene __instance)
        {
            try
            {
                if (__instance == null || __instance.m_prefabs == null) return;
                if (string.IsNullOrEmpty((UtangardConfig.BossDoorKeys.Value ?? "").Trim())) return;

                var found = new List<string>();
                foreach (var prefab in __instance.m_prefabs)
                {
                    Door door;
                    if (prefab == null || !prefab.TryGetComponent(out door) || door.m_keyItem == null) continue;
                    if (Guarded(door.m_keyItem)) found.Add(prefab.name + " (" + door.m_keyItem.gameObject.name + ")");
                }

                if (found.Count > 0)
                    UtangardPlugin.Log.LogInfo("Boss doors kept sealed in a locked biome: " + string.Join(", ", found.ToArray()));
                else
                    UtangardPlugin.Log.LogWarning("BossDoorKeys (" + UtangardConfig.BossDoorKeys.Value + ") matches no "
                                                  + "door in this world, so no boss door is guarded.");
            }
            catch (Exception e)
            {
                UtangardPlugin.Log.LogWarning("Could not list the boss doors: " + e.Message);
            }
        }

        /// <summary>
        /// Whether this door must stay shut for this person right now. Shows the message and the
        /// door's own locked effect when it does.
        /// </summary>
        private static bool Refused(Door door, Humanoid who)
        {
            try
            {
                if (door == null || door.m_keyItem == null) return false;
                if (!Guarded(door.m_keyItem)) return false;

                // Open already. Vanilla will not close a keyed door and this does not either.
                ZNetView nview;
                if (!door.TryGetComponent(out nview) || !nview.IsValid()) return false;
                if (nview.GetZDO().GetInt(ZDOVars.s_state) != 0) return false;

                var key = BossAltars.Unearned(door.transform.position);
                if (key == null) return false;

                // The same rattle vanilla plays for a door you have no key for, so the refusal
                // reads as the door, not as something the mod did to the player.
                door.m_lockedEffects.Create(door.transform.position, door.transform.rotation);

                if (who != null && who == Player.m_localPlayer)
                    who.Message(MessageHud.MessageType.Center, UtangardConfig.BossDoorBlockedMessage.Value);

                if (UtangardConfig.Verbose.Value)
                    UtangardPlugin.Log.LogInfo("Kept " + door.name + " shut: its biome needs " + key
                                               + ", and the group has not earned it.");

                return true;
            }
            catch (Exception e)
            {
                // Never let a check about a rule jam a door.
                if (!_saidFailure)
                {
                    _saidFailure = true;
                    UtangardPlugin.Log.LogWarning("Could not check a boss door against the gate, so it "
                                                  + "opens: " + e);
                }
                return false;
            }
        }

        /// <summary>
        /// Whether the door's key is one of BossDoorKeys. By the key's prefab name, the name a
        /// person types. Parsed again only when the setting changes - Core can deliver the host's
        /// value after the world has loaded.
        /// </summary>
        private static bool Guarded(ItemDrop keyItem)
        {
            var line = UtangardConfig.BossDoorKeys.Value ?? "";
            if (!string.Equals(line, _parsedFrom, StringComparison.Ordinal))
            {
                Keys.Clear();
                foreach (var raw in line.Split(','))
                {
                    var name = raw.Trim();
                    if (name.Length > 0) Keys.Add(name);
                }
                _parsedFrom = line;
            }

            return Keys.Count > 0 && Keys.Contains(keyItem.gameObject.name);
        }
    }
}

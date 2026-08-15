using HarmonyLib;

namespace Wither
{
    /// <summary>
    /// Every patch the mod installs. Five of them, and each one is a single seam that the
    /// game funnels a whole category of behaviour through.
    /// </summary>
    internal static class WitherPatches
    {
        /// <summary>
        /// The tick. Private in Player, which Harmony does not mind, so it is named by
        /// string rather than nameof.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdateFood")]
        private static void OnUpdateFood(Player __instance, float dt, bool forceUpdate)
        {
            // forceUpdate is the recompute EatFood triggers after a bite; dt is zero and it
            // is not a tick. Draining on it would be harmless today and wrong the moment
            // anything else starts calling it.
            if (forceUpdate) return;

            WitherTick.Run(__instance, dt);
        }

        /// <summary>
        /// Refuse the bite and the drink.
        ///
        /// CanConsumeItem, and emphatically not EatFood. EatFood looks like the obvious seam
        /// and it is a trap: ConsumeItem calls it, ignores what it returns, and removes the
        /// item from the inventory anyway. A prefix there would refuse the meal and destroy
        /// the food at the same time - and do the same to every potion, since ConsumeItem
        /// applies the status effect and removes the item on separate lines with nothing
        /// between them.
        ///
        /// CanConsumeItem is the gate that path actually respects, it is where vanilla puts
        /// its own "$msg_cantconsume" refusal, and every other way of eating in the game -
        /// interacting with a placed food piece, ItemDrop.Eat - goes through it too. All of
        /// its callers are real attempts rather than hover prompts, so a message here fires
        /// once per try and not once per frame.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.CanConsumeItem))]
        private static bool OnCanConsumeItem(
            Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (item == null || item.m_shared == null) return true;
            if (!BiomeGate.IsWithered(__instance)) return true;

            bool isFood = item.m_shared.m_food > 0f
                || item.m_shared.m_foodStamina > 0f
                || item.m_shared.m_foodEitr > 0f;

            if (isFood && WitherConfig.BlockEating.Value)
                return Refuse(__instance, ref __result, WitherConfig.EatBlockedMessage.Value);

            // A potion whose effect would be refused a moment later is a potion thrown away.
            // Stopping it here is the difference between a rule and a punishment.
            if (WitherConfig.BlockNewBuffs.Value
                && BlockedEffects.IsBlocked(item.m_shared.m_consumeStatusEffect))
                return Refuse(__instance, ref __result, WitherConfig.BuffBlockedMessage.Value);

            return true;
        }

        private static bool Refuse(Player player, ref bool __result, string message)
        {
            if (!string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message);

            __result = false;
            return false;
        }

        /// <summary>
        /// Refuse the buff, on the path that adds a new one.
        ///
        /// Every add in the game funnels through this overload eventually - the hash overload
        /// resolves the effect out of ObjectDB and calls straight into it, and the RPC path
        /// goes through the hash overload. So one patch covers potions, guardian powers,
        /// equipment effects, and anything a future update routes through SEMan.
        ///
        /// It does not cover the refresh case. See OnInternalAddStatusEffect below.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect),
            typeof(StatusEffect), typeof(bool), typeof(int), typeof(float))]
        private static bool OnAddStatusEffect(
            SEMan __instance, StatusEffect statusEffect, ref StatusEffect __result)
        {
            if (!WitherConfig.BlockNewBuffs.Value) return true;
            if (statusEffect == null) return true;
            if (!BlockedEffects.IsBlocked(statusEffect)) return true;

            if (!BiomeGate.IsWithered(CharacterOf(__instance) as Player)) return true;

            if (WitherConfig.Verbose.Value)
                WitherPlugin.Log.LogInfo("Refused status effect " + statusEffect.name);

            __result = null;
            return false;
        }

        /// <summary>
        /// Refuse the buff, on the path that refreshes one already running.
        ///
        /// This one is easy to miss and it matters more than the add. When the effect is
        /// already on you, Internal_AddStatusEffect calls ResetTime on it in place and
        /// returns - it never reaches the public overload above, so a prefix there sees
        /// nothing. Sitting by a fire in a gated biome refreshes Rested several times a
        /// minute through exactly this path, which would top it back up faster than the drain
        /// could ever take it down and quietly make BlockRested do nothing.
        ///
        /// Classified by hash, because at this point the effect has not been resolved to an
        /// object yet and resolving it just to ask would be doing ObjectDB's lookup twice.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SEMan), "Internal_AddStatusEffect")]
        private static bool OnInternalAddStatusEffect(
            SEMan __instance, int nameHash, ref StatusEffect __result)
        {
            if (!WitherConfig.BlockNewBuffs.Value) return true;
            if (!BlockedEffects.IsBlockedHash(nameHash)) return true;
            if (!BiomeGate.IsWithered(CharacterOf(__instance) as Player)) return true;

            __result = null;
            return false;
        }

        /// <summary>
        /// Refuse the guardian power before it is spent, not after.
        ///
        /// Blocking the status effect alone would be the obvious thing and is a trap:
        /// StartGuardianPower sets the cooldown and only then applies the effect, so the
        /// player would burn twenty minutes of power on nothing and get no explanation. This
        /// prefix stops it a line earlier, so the power is still there when they leave.
        ///
        /// It defers to the same classification as everything else, so putting a GP_ name in
        /// NeverBlock leaves that power usable here too.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.StartGuardianPower))]
        private static bool OnStartGuardianPower(Player __instance, ref bool __result)
        {
            if (!WitherConfig.BlockNewBuffs.Value) return true;
            if (!BiomeGate.IsWithered(__instance)) return true;

            StatusEffect power = GuardianSeOf(__instance);
            if (!BlockedEffects.IsBlocked(power)) return true;

            string message = WitherConfig.EatBlockedMessage.Value;
            if (!string.IsNullOrEmpty(message))
                __instance.Message(MessageHud.MessageType.Center, message);

            __result = false;
            return false;
        }

        private static readonly AccessTools.FieldRef<SEMan, Character> CharacterOf =
            AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        private static readonly AccessTools.FieldRef<Player, StatusEffect> GuardianSeOf =
            AccessTools.FieldRefAccess<Player, StatusEffect>("m_guardianSE");

        /// <summary>
        /// Credit everyone at the kill, the moment a boss dies.
        ///
        /// This patch runs on exactly one machine: the client that owns the creature's ZDO.
        /// It reads as though it runs everywhere, because OnDeath pushes vanilla's unique key
        /// above an `if (!m_nview.IsOwner()) return;` - but that guard is unreachable.
        /// CheckDeath is OnDeath's only caller, and CheckDeath is itself called from one place,
        /// inside `if (zDO.IsOwner())` in Character.CustomFixedUpdate.
        ///
        /// So there is no "every client present" to inherit, from a prefix, a postfix or
        /// anything else. The owner has to do the crediting for the whole fight, which is why
        /// this hands over a position rather than crediting the local player.
        ///
        /// Position comes from the transform rather than from a cached value because a
        /// postfix runs before ZNetScene.Destroy has taken effect - the object is still where
        /// it died.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static void OnCharacterDeath(Character __instance)
        {
            if (!WitherConfig.Enabled.Value || !WitherConfig.GateOnGroup.Value) return;
            if (__instance == null) return;

            Progression.CreditAttendees(
                __instance.transform.position, __instance.m_defeatSetGlobalKey);
        }

        /// <summary>
        /// Both ObjectDB entry points, because both really happen: Awake builds the database
        /// for a local world, and CopyOtherDB replaces it wholesale with the host's when you
        /// join a server. Rebuilding on only one leaves the buff set and the borrowed icons
        /// pointing at a database that no longer exists.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static void OnObjectDbAwake()
        {
            RebuildFromObjectDb();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static void OnObjectDbCopy()
        {
            RebuildFromObjectDb();
        }

        private static void RebuildFromObjectDb()
        {
            BlockedEffects.Rebuild();
            WitherEffectsRegistry.Build();
        }

        /// <summary>
        /// Spawning is the first moment the world's global keys are certainly present - on a
        /// client they arrive by RPC some time after ZoneSystem starts, so anything logged
        /// earlier reads as an empty world with no bosses dead. Which is also the failure
        /// this dump exists to catch, so it has to be late enough to be true.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static void OnPlayerSpawned(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (WitherConfig.LogGlobalKeys.Value) GlobalKeyDump.Log();
        }
    }
}

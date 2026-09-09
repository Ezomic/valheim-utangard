using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Utangard
{
    /// <summary>
    /// Every patch the mod installs, one nested class per seam.
    ///
    /// The nesting is isolation, not filing. All of this used to be one class put on with a
    /// single PatchAll, and on the day Valheim 1.0 shipped that cost the whole mod: one
    /// changed signature threw out of PatchAll and every patch declared after it never went
    /// on. Seams.Apply now patches each class under its own try/catch, so a method the game
    /// has moved costs exactly the feature standing on it. The full account is in Seams.
    ///
    /// Two rules follow from that day and are enforced here rather than remembered:
    ///
    ///   - No patch argument is bound by the vanilla parameter's NAME. Harmony injects by
    ///     name, an unmatched name is a throw, and renaming a parameter is cheaper for a
    ///     studio than changing a signature. Positional __0 / __1 cannot be renamed. The four
    ///     patches that took `dt`, `forceUpdate`, `item`, `statusEffect` and `nameHash` by
    ///     name were also the four declared first, so every one of them stood in front of the
    ///     rest of the mod.
    ///   - No overload is pinned by its full argument list where that list can grow. See
    ///     NewBuffs.Target, which is the exact failure that happened.
    ///
    /// The classes are internal rather than private because Seams names each of them.
    /// </summary>
    internal static class UtangardPatches
    {
        // ------------------------------------------------------------------ the tick -----

        /// <summary>
        /// The tick. Private in Player, which Harmony does not mind, so it is named by
        /// string rather than nameof.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateFood")]
        internal static class FoodTick
        {
            /// <param name="__0">dt. Positional, so a rename cannot unseat it.</param>
            /// <param name="__1">forceUpdate.</param>
            [HarmonyPostfix]
            private static void Postfix(Player __instance, float __0, bool __1)
            {
                // forceUpdate is the recompute EatFood triggers after a bite; dt is zero and
                // it is not a tick. Draining on it would be harmless today and wrong the
                // moment anything else starts calling it.
                if (__1) return;

                UtangardTick.Run(__instance, __0);
            }
        }

        // ---------------------------------------------------------------- the refusals ---

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
        [HarmonyPatch(typeof(Player), nameof(Player.CanConsumeItem))]
        internal static class Eating
        {
            /// <param name="__0">The item. Positional: vanilla calls it `item` today.</param>
            [HarmonyPrefix]
            private static bool Prefix(
                Player __instance, ItemDrop.ItemData __0, ref bool __result)
            {
                if (__0 == null || __0.m_shared == null) return true;
                if (!BiomeGate.IsWithered(__instance)) return true;

                bool isFood = __0.m_shared.m_food > 0f
                    || __0.m_shared.m_foodStamina > 0f
                    || __0.m_shared.m_foodEitr > 0f;

                if (isFood && UtangardConfig.BlockEating.Value)
                    return Refuse(__instance, ref __result, UtangardConfig.EatBlockedMessage.Value);

                // A potion whose effect would be refused a moment later is a potion thrown
                // away. Stopping it here is the difference between a rule and a punishment.
                if (UtangardConfig.BlockNewBuffs.Value
                    && BlockedEffects.IsBlocked(__0.m_shared.m_consumeStatusEffect))
                    return Refuse(__instance, ref __result, UtangardConfig.BuffBlockedMessage.Value);

                return true;
            }
        }

        /// <summary>
        /// Refuse the buff, on the path that adds a new one.
        ///
        /// Every add in the game funnels through this overload eventually - the hash overload
        /// resolves the effect out of ObjectDB and calls straight into it, and the RPC path
        /// goes through the hash overload. So one patch covers potions, guardian powers,
        /// equipment effects, and anything a future update routes through SEMan.
        ///
        /// It does not cover the refresh case. See BuffRefresh below.
        /// </summary>
        [HarmonyPatch]
        internal static class NewBuffs
        {
            /// <summary>
            /// The overload taking a StatusEffect, found by its FIRST parameter rather than
            /// by its whole signature.
            ///
            /// SEMan has two AddStatusEffect overloads, so the target cannot be named by
            /// string alone. Pinning the full argument list is what broke on 1.0: the game
            /// added a trailing `short variant = -1`, four pinned types stopped matching
            /// anything, and the ArgumentException that came out of PatchAll took the whole
            /// mod down with it. What actually distinguishes the two overloads is the first
            /// parameter and nothing else, so that is all this asks about - and a sixth
            /// optional argument in some later update costs nothing.
            /// </summary>
            [HarmonyTargetMethod]
            private static MethodBase Target()
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(SEMan)))
                {
                    if (method.Name != nameof(SEMan.AddStatusEffect)) continue;

                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length > 0 && args[0].ParameterType == typeof(StatusEffect))
                        return method;
                }

                // Returning null makes Harmony throw, and Seams.Apply catches that and reports
                // the seam as broken. Saying it here as well names the thing that moved, which
                // the generic patch failure cannot.
                UtangardPlugin.Log.LogError(
                    "SEMan has no AddStatusEffect(StatusEffect, ...) any more - new buffs will "
                    + "not be refused in a gated biome.");

                return null;
            }

            /// <param name="__0">The effect being applied.</param>
            [HarmonyPrefix]
            private static bool Prefix(
                SEMan __instance, StatusEffect __0, ref StatusEffect __result)
            {
                if (!UtangardConfig.BlockNewBuffs.Value) return true;
                if (__0 == null) return true;
                if (!BlockedEffects.IsBlocked(__0)) return true;

                AccessTools.FieldRef<SEMan, Character> characterOf = CharacterOf();
                if (characterOf == null) return true;

                if (!BiomeGate.IsWithered(characterOf(__instance) as Player)) return true;

                if (UtangardConfig.Verbose.Value)
                    UtangardPlugin.Log.LogInfo("Refused status effect " + __0.name);

                __result = null;
                return false;
            }
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
        ///
        /// Named by string with no argument list: it is private, so there is only one of it,
        /// and leaving the list off is what lets it survive another added parameter.
        /// </summary>
        [HarmonyPatch(typeof(SEMan), "Internal_AddStatusEffect")]
        internal static class BuffRefresh
        {
            /// <param name="__0">nameHash.</param>
            [HarmonyPrefix]
            private static bool Prefix(SEMan __instance, int __0, ref StatusEffect __result)
            {
                if (!UtangardConfig.BlockNewBuffs.Value) return true;
                if (!BlockedEffects.IsBlockedHash(__0)) return true;

                AccessTools.FieldRef<SEMan, Character> characterOf = CharacterOf();
                if (characterOf == null) return true;

                if (!BiomeGate.IsWithered(characterOf(__instance) as Player)) return true;

                __result = null;
                return false;
            }
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
        [HarmonyPatch(typeof(Player), nameof(Player.StartGuardianPower))]
        internal static class GuardianPowers
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (!UtangardConfig.BlockNewBuffs.Value) return true;
                if (!BiomeGate.IsWithered(__instance)) return true;

                AccessTools.FieldRef<Player, StatusEffect> guardianSeOf = GuardianSeOf();
                if (guardianSeOf == null) return true;

                StatusEffect power = guardianSeOf(__instance);
                if (!BlockedEffects.IsBlocked(power)) return true;

                // BuffBlockedMessage, not EatBlockedMessage. This said "the land will not feed
                // you here" when a guardian power was refused, which is the food refusal
                // wearing the wrong hat - the message was written before the buff one existed
                // and did not follow it when it arrived.
                return Refuse(__instance, ref __result, UtangardConfig.BuffBlockedMessage.Value);
            }
        }

        private static bool Refuse(Player player, ref bool __result, string message)
        {
            if (!string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message);

            __result = false;
            return false;
        }

        // --------------------------------------------------------------- the progress ----

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
        ///
        /// This is one of the two doors credit comes through, so whether it went on decides
        /// whether the mod is allowed to wither anybody at all. See Seams.PenaltyIsEscapable.
        /// </summary>
        [HarmonyPatch(typeof(Character), "OnDeath")]
        internal static class KillCredit
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance)
            {
                if (!UtangardConfig.Enabled.Value || !UtangardConfig.GateOnGroup.Value) return;
                if (__instance == null) return;

                Progression.CreditAttendees(
                    __instance.transform.position, __instance.m_defeatSetGlobalKey);
            }
        }

        /// <summary>
        /// Hold a flag across the world's key list being rebuilt.
        ///
        /// ZoneSystem.RPC_GlobalKeys clears every global key and re-adds them one at a time,
        /// and it runs on every client every time anyone sets any key, because SetGlobalKey
        /// ends in SendGlobalKeys(Everybody). For the length of that loop the dictionary this
        /// mod reads its roster and its credits out of is incomplete.
        ///
        /// Vanilla never notices, because the refill is synchronous and no frame boundary
        /// falls inside it. A Harmony postfix on GlobalKeyAdd does notice, and Yoke has one -
        /// it hooks there deliberately, to catch the bulk list a server sends on connect - so
        /// every key in that list makes Yoke ask this mod whether the group has cleared a
        /// boss, while the answer is built from whatever fraction has arrived.
        ///
        /// A prefix and a postfix rather than a wrapper: RPC_GlobalKeys is private and takes
        /// a List&lt;string&gt;, and the flag has to be down again even if something inside
        /// throws, which is what finally would buy in a wrapper and what the postfix buys
        /// here. Both halves live in one class so they go on or stay off together, and the
        /// prefix checks that they did - see Seams.KeyArrival.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), "RPC_GlobalKeys")]
        internal static class KeyArrival
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                // Never raise a flag whose only way down might not have been installed.
                // Harmony puts a class's patches on one method at a time, so the postfix
                // failing while this succeeded would strand Settling at true for the life of
                // the process, which permanently disables the latch and the roster cache and
                // looks exactly like nothing being wrong.
                if (!Seams.KeyArrival) return;

                Progression.Settling = true;
            }

            [HarmonyPostfix]
            private static void Postfix()
            {
                Progression.Settling = false;

                // The list that was just installed is a different world state from the one
                // the cached roster was built against, whatever it was built from.
                Progression.InvalidateRoster();
            }
        }

        /// <summary>
        /// Any key at all changes the answer this mod caches, so the cache goes.
        ///
        /// It is one field assignment, and it fires for every key in the game rather than
        /// only ours - which is still far cheaper than the alternative, a roster that
        /// outlives the world state it describes by up to two seconds. That is exactly how
        /// long one frame of half-filled keys needed to survive in order to reach the latch.
        /// </summary>
        [HarmonyPatch(typeof(ZoneSystem), "GlobalKeyAdd", new[] { typeof(string), typeof(bool) })]
        internal static class KeyChange
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                Progression.InvalidateRoster();
            }
        }

        // ------------------------------------------------------------------ the rest -----

        /// <summary>
        /// Both ObjectDB entry points really happen, and they are two seams rather than one.
        /// Awake builds the database for a local world; CopyOtherDB replaces it wholesale
        /// with the host's when you join a server. Rebuilding on only one leaves the buff set
        /// and the borrowed icons pointing at a database that no longer exists - so they are
        /// patched separately, and losing one no longer costs the other.
        /// </summary>
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        internal static class EffectsLocal
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                RebuildFromObjectDb();
            }
        }

        /// <summary>The host's database, handed over on connect. See EffectsLocal.</summary>
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        internal static class EffectsJoined
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                RebuildFromObjectDb();
            }
        }

        private static void RebuildFromObjectDb()
        {
            BlockedEffects.Rebuild();
            UtangardEffectsRegistry.Build();
        }

        /// <summary>
        /// A Utangard page in the compendium, beside Logs and Active Effects.
        ///
        /// This is the mod's whole UI, and it is somebody else's UI. Vanilla builds that list
        /// in UpdateTextsList and then instantiates a row per entry in FillTextList, so a
        /// postfix here is an entry with the game's own skin, font, scrolling, gamepad
        /// handling and close behaviour - none of which this mod then owns. An IMGUI window
        /// would have been four patches (both TakeInput overloads, InInventoryEtc and
        /// GameCamera.UpdateMouseCapture) and a keybind, to end up with something that looks
        /// like a different game.
        ///
        /// Inserted at the front because the question it answers - why is this biome shut and
        /// who am I waiting on - is the one a player opens this screen to ask.
        /// </summary>
        [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
        internal static class CompendiumPage
        {
            [HarmonyPostfix]
            private static void Postfix(TextsDialog __instance)
            {
                if (!UtangardConfig.Enabled.Value
                    || !UtangardConfig.ShowCompendiumPage.Value) return;

                AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> textsOf = TextsOf();
                if (textsOf == null) return;

                List<TextsDialog.TextInfo> texts = textsOf(__instance);
                if (texts == null)
                {
                    UtangardPlugin.Log.LogWarning("Compendium: no m_texts list to add to.");
                    return;
                }

                string topic = UtangardConfig.CompendiumTopic.Value;
                if (string.IsNullOrEmpty(topic)) topic = UtangardPlugin.PluginName;

                // Wrapped, and not because the body looks risky. This runs inside somebody
                // else's UI build: a throw here leaves vanilla's own list half-built, so the
                // failure would present as "the compendium is broken" rather than as "a mod
                // is". Unity swallows the stack in a UI callback often enough that it is
                // worth saying so in our own log rather than hoping it lands in the game's.
                try
                {
                    texts.Insert(0, new TextsDialog.TextInfo(topic, GateReport.Page()));

                    // Behind Verbose: this fires every time the screen is opened, and a line
                    // per glance at the compendium buries the ones worth reading.
                    if (UtangardConfig.Verbose.Value)
                        UtangardPlugin.Log.LogInfo("Compendium: added '" + topic + "' ("
                            + texts.Count + " entries in the list).");
                }
                catch (System.Exception e)
                {
                    UtangardPlugin.Log.LogError("Compendium page failed to build: " + e);
                }
            }
        }

        /// <summary>
        /// Spawning is the first moment the world's global keys are certainly present - on a
        /// client they arrive by RPC some time after ZoneSystem starts, so anything logged
        /// earlier reads as an empty world with no bosses dead. Which is also the failure
        /// this dump exists to catch, so it has to be late enough to be true.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        internal static class Spawn
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                if (UtangardConfig.LogGlobalKeys.Value) GlobalKeyDump.Log();

                WarnIfGateIsUnenforceable();
            }
        }

        /// <summary>
        /// Say so, once, when the group gate is running somewhere it cannot be enforced.
        ///
        /// Standalone Utangard is fully functional and singleplayer needs nothing else, so the
        /// standalone path is deliberately not an error. But a group gate on a server with no
        /// Core is the one combination that looks like it is working and is not: everyone who
        /// installed the mod is gated, anyone who did not is not gated at all, and nothing
        /// distinguishes that from a correctly gated server except a player walking into the
        /// Ashlands on day one. Failing silently there is the worst of the options.
        ///
        /// Spawn rather than Awake because that is the first moment ZNet has an answer -
        /// asking at plugin load reports a singleplayer session on every machine.
        /// </summary>
        private static void WarnIfGateIsUnenforceable()
        {
            if (UtangardPlugin.CorePresent) return;
            if (!UtangardConfig.GateOnGroup.Value) return;

            // Peers, not IsServer: a listen host with nobody connected is still effectively
            // singleplayer, and a solo player has no roster to disagree with.
            ZNet net = ZNet.instance;
            if (net == null || net.GetPeerConnections() <= 0) return;

            UtangardPlugin.Log.LogWarning(
                "The group gate is on in a multiplayer session, but Core is not installed. "
                + "Nothing can refuse a player who does not have Utangard, so anyone without it "
                + "is not gated at all. Install Core on the server and every client to enforce "
                + "it, or set GateOnGroup = false to gate on the world instead.");
        }

        // ------------------------------------------------------------- private fields ----
        //
        // Bound on first use and never in a static initialiser - see Reflect for why that
        // distinction is the difference between losing one feature and poisoning every patch
        // in the class that declares it. Each keeps its own "already tried" flag so a field a
        // game update removed is reported once rather than every frame.

        private static AccessTools.FieldRef<SEMan, Character> _characterOf;
        private static bool _characterBound;

        /// <summary>
        /// SEMan.m_character - whose SEMan this is. Without it neither buff refusal can tell
        /// the local player from a greydwarf, so both stand aside rather than guess.
        /// </summary>
        private static AccessTools.FieldRef<SEMan, Character> CharacterOf()
        {
            if (_characterBound) return _characterOf;
            _characterBound = true;

            _characterOf = Reflect.Field<SEMan, Character>(
                "m_character", "refusing buffs in a gated biome");

            return _characterOf;
        }

        private static AccessTools.FieldRef<Player, StatusEffect> _guardianSeOf;
        private static bool _guardianBound;

        /// <summary>Player.m_guardianSE - which power the forsaken altar gave this character.</summary>
        private static AccessTools.FieldRef<Player, StatusEffect> GuardianSeOf()
        {
            if (_guardianBound) return _guardianSeOf;
            _guardianBound = true;

            _guardianSeOf = Reflect.Field<Player, StatusEffect>(
                "m_guardianSE", "refusing guardian powers in a gated biome");

            return _guardianSeOf;
        }

        private static AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> _textsOf;
        private static bool _textsBound;

        /// <summary>
        /// TextsDialog.m_texts is private and there is no other way in. TextInfo itself is
        /// public, so only the list needs reaching for.
        /// </summary>
        private static AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> TextsOf()
        {
            if (_textsBound) return _textsOf;
            _textsBound = true;

            _textsOf = Reflect.Field<TextsDialog, List<TextsDialog.TextInfo>>(
                "m_texts", "the Utangard page in the compendium");

            return _textsOf;
        }
    }
}

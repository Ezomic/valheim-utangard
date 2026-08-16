using BepInEx;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Wither
{
    /// <summary>
    /// Wither. A biome you have not earned will not feed you.
    ///
    /// The gameplay is still client-side - food timers and status effects belong to the
    /// owning client, and nothing here reaches into another player's character. But the
    /// group gate changes what "client-side" is worth. It decides whether a biome is open
    /// from what every member of the roster has done, so a player running without the plugin
    /// never publishes their own progress and would hold every gate shut for everybody. That
    /// makes the mod something the server has to insist on rather than something a group
    /// agrees to, which is what Requirement.Everyone below buys.
    ///
    /// There is deliberately no BepInProcess attribute. A dedicated server runs
    /// valheim_server.exe, and the refusal in Core's gate only happens on the server side of
    /// RPC_PeerInfo - so this has to load there to be enforced at all.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("ezomic.valheim.core", BepInDependency.DependencyFlags.HardDependency)]
    public class WitherPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.wither";
        public const string PluginName = "Wither";
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            WitherConfig.Bind(Config);

            // Everyone, not HostOnly. A missing copy is not a player quietly opting out - it
            // is a character that never publishes its boss credit, which under the group gate
            // reads as "has done nothing" and withers the whole roster out of every biome.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.Everyone);

            // The host decides the rules of the gate; clients keep their own presentation.
            // Syncing the messages or the icons would be forcing someone's wording on
            // somebody else, and syncing the diagnostics would turn a debug flag into a
            // server-wide one.
            Suite.Sync(
                WitherConfig.Enabled,
                WitherConfig.GateOnGroup,
                WitherConfig.GateNeverRegresses,
                WitherConfig.BackfillFromCharacter,
                WitherConfig.RosterDays,
                WitherConfig.RosterDaysPerBoss,
                WitherConfig.CatchUpDays,
                WitherConfig.CatchUpDaysPerBoss,
                WitherConfig.CreditRadius,
                WitherConfig.ExcludePlayerIds,
                WitherConfig.FoodDrainMultiplier,
                WitherConfig.BlockEating,
                WitherConfig.BuffDrainMultiplier,
                WitherConfig.BlockNewBuffs,
                WitherConfig.BlockRested,
                WitherConfig.AlsoBlock,
                WitherConfig.NeverBlock,
                WitherConfig.SappedStaminaRegen,
                WitherConfig.SappedMaxSeconds);

            Suite.Sync(WitherConfig.GateKeyEntries());

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(WitherPatches));

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}

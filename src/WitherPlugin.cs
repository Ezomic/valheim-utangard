using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
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
    // Soft, not hard. Wither has to be installable on its own, because it is worth wanting on
    // its own and a stranger should not have to install two mods to get one. Soft still buys
    // the load-order guarantee when Core is present, which is all that registering needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class WitherPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.wither";
        public const string PluginName = "Wither";
        public const string PluginVersion = "1.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        private const string CoreGuid = "ezomic.valheim.core";

        internal static ManualLogSource Log;

        /// <summary>
        /// Whether Core answered at load. Read by the multiplayer warning on spawn, which is
        /// the one place the difference is visible to a player rather than only in a log.
        /// </summary>
        internal static bool CorePresent;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            WitherConfig.Bind(Config);

            TryRegisterWithCore();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(WitherPatches));

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// What is lost standing alone is worth naming exactly, because it is not the mod. All
        /// of Wither runs without Core: the drain, the refusal and Sapped are local patches,
        /// and the group gate travels over vanilla global keys, which every client already
        /// replicates. Singleplayer is unaffected in every respect.
        ///
        /// What is lost is the *enforcement*. Core is what refuses a client that does not have
        /// Wither, and without that refusal a player without the mod is not gated at all - they
        /// walk into the Ashlands on day one while everyone else waits on the roster. The gate
        /// becomes an agreement between players rather than a rule of the server. That is a
        /// real loss and a legitimate choice; it is the server owner's to make, not this
        /// plugin's, which is why this logs rather than refusing to run.
        /// </summary>
        private void TryRegisterWithCore()
        {
            CorePresent = Chainloader.PluginInfos.ContainsKey(CoreGuid);

            if (!CorePresent)
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a method
        /// needs when it first compiles that method, so a Suite call sitting directly in Awake
        /// would drag Ezomic.Core in before the check above could prevent it - and the
        /// missing-assembly exception would land during plugin load, which is the failure this
        /// whole arrangement exists to avoid. Isolating it means the type is only ever resolved
        /// on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
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
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Wither
{
    /// <summary>
    /// Wither. A biome you have not earned will not feed you.
    ///
    /// Client-side by construction, and the BepInProcess attribute says so: food timers and
    /// status effects belong to the owning client in Valheim, so there is nothing for a
    /// dedicated server to enforce and nothing sent over the wire. The practical consequence
    /// is that this is a rule for a group that all runs it, not a lock - a player without the
    /// plugin plays vanilla.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class WitherPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.wither";
        public const string PluginName = "Wither";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            WitherConfig.Bind(Config);

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

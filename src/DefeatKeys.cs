using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// Which creatures in this world set which global key when they die - asked of the game
    /// rather than assumed.
    ///
    /// The mod's one silent failure mode is a key name in the Gate table that nothing ever
    /// sets. `GetGlobalKey` answers false for it forever, so the biome withers permanently
    /// and looks exactly like a working gate against a boss nobody has killed. Two of the
    /// shipped defaults are exactly that risk: `defeated_queen` and `defeated_fader` are not
    /// in the game's `GlobalKeys` enum, because those two bosses carry their key in prefab
    /// data instead, so they could only ever be read off a running game.
    ///
    /// So read it off a running game. `Character.m_defeatSetGlobalKey` is a public string
    /// field on every creature prefab, and `Character.OnDeath` passes it straight to
    /// `ZoneSystem.SetGlobalKey`. Walking ZNetScene's prefab list therefore produces the
    /// complete and authoritative list of keys any death in this world can set - including
    /// the ones another mod's creatures add, for free.
    ///
    /// This checks rather than corrects. A gate row pointed at some other mod's key, or at a
    /// key a location sets, is a supported thing to want, so a name nothing here recognises
    /// is a warning and never an edit to somebody's config.
    /// </summary>
    internal static class DefeatKeys
    {
        /// <summary>Key to the prefabs that set it. Rebuilt per world, not per spawn.</summary>
        private static readonly Dictionary<string, List<string>> Setters =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Key to the localisation token of the creature that sets it - "$enemy_gdking" and
        /// friends, which is what turns 'defeated_gdking' into 'The Elder' on the panel.
        ///
        /// A boss wins over anything else that sets the same key, because several creatures
        /// can and the boss is the one a player is being asked about. Stored as the token and
        /// localised at the moment of display: the panel is built while the player reads it,
        /// and Localization is not necessarily loaded when this scan runs.
        /// </summary>
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The scene the map was built from.
        ///
        /// Identity, not a bool. ZNetScene is destroyed and rebuilt whenever a world is
        /// loaded - including logging out to the menu and back in - and a flag would answer
        /// "already scanned" about a scene that no longer exists. That exact mistake is
        /// written up in the prefab-registration notes for a reason.
        /// </summary>
        private static ZNetScene _scannedScene;

        /// <summary>
        /// Build the map if this world has not been scanned yet.
        ///
        /// One `GetComponent` per prefab over a list of a few thousand, once per world load.
        /// Measured against the alternatives it is the cheap one: the map is what makes the
        /// check possible at all, and every other route to it is a hardcoded list that goes
        /// stale on the next game update.
        /// </summary>
        private static void Scan()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || ReferenceEquals(scene, _scannedScene)) return;

            Setters.Clear();
            Names.Clear();
            _scannedScene = scene;

            List<GameObject> prefabs = scene.m_prefabs;
            if (prefabs == null) return;

            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null) continue;

                Character character;
                if (!prefab.TryGetComponent(out character)) continue;

                string key = character.m_defeatSetGlobalKey;
                if (string.IsNullOrEmpty(key)) continue;

                List<string> names;
                if (!Setters.TryGetValue(key, out names))
                {
                    names = new List<string>(1);
                    Setters[key] = names;
                }

                names.Add(prefab.name);

                // First one in wins unless a boss turns up later. m_boss is the flag vanilla
                // itself uses to decide who gets the health bar at the top of the screen, so
                // it is exactly the "is this the creature the player means" question.
                if (string.IsNullOrEmpty(character.m_name)) continue;
                if (!Names.ContainsKey(key) || character.m_boss) Names[key] = character.m_name;
            }
        }

        /// <summary>
        /// What to call the thing that sets this key, in the player's language, or null when
        /// nothing in this world sets it.
        ///
        /// This is why the scan is worth having twice over: the panel wanted to say "needs
        /// The Elder" and the only place that mapping exists is the prefab that carries both
        /// the key and the name. Hardcoding a table of eight would have been a second list to
        /// keep in step with the config, wrong for any mod-added boss, and untranslated.
        /// </summary>
        public static string NameFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            Scan();

            string token;
            if (!Names.TryGetValue(key, out token)) return null;

            Localization loc = Localization.instance;
            if (loc == null) return null;

            string name = loc.Localize(token);
            return string.IsNullOrEmpty(name) || name == token ? null : name;
        }

        /// <summary>Whether any creature in this world sets that key on death.</summary>
        public static bool IsSetByACreature(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            Scan();
            return Setters.ContainsKey(key);
        }

        /// <summary>
        /// Log the map, and warn about any gate row nothing in this world can satisfy.
        ///
        /// The warning is the point and the listing is the fix: told that `defeated_queen`
        /// is not set by anything here, the next question is always "then what is it called",
        /// and the answer is in the lines above it.
        /// </summary>
        public static void Report()
        {
            Scan();

            if (Setters.Count == 0)
            {
                // Not a warning. A dedicated server reaches this before its scene is built,
                // and so does the menu - saying "no creature sets any key" there would be a
                // scary line about a world that has not loaded yet.
                UtangardPlugin.Log.LogInfo(
                    "No prefab scene to read defeat keys from yet - gate keys unverified.");
                return;
            }

            if (UtangardConfig.LogDefeatKeys.Value) LogAll();

            var missing = new List<string>();
            foreach (Heightmap.Biome biome in UtangardConfig.GateableBiomes)
            {
                string key = UtangardConfig.RequiredKeyFor(biome);
                if (key == null || Setters.ContainsKey(key)) continue;

                missing.Add(biome + " -> '" + key + "'");
            }

            if (missing.Count == 0)
            {
                UtangardPlugin.Log.LogInfo(
                    "Gate keys verified against this world's creatures: every gated biome "
                    + "names a key something here actually sets on death.");
                return;
            }

            var warning = new StringBuilder();
            warning.Append("These gate rows name a key no creature in this world sets: ");
            for (int i = 0; i < missing.Count; i++)
            {
                if (i > 0) warning.Append(", ");
                warning.Append(missing[i]);
            }

            warning.Append(". That is fine if the key comes from somewhere else - another "
                + "mod, or a location - and is a permanently shut biome if it is a typo, "
                + "which looks exactly the same from inside the game. The keys creatures "
                + "here do set are: ").Append(BossKeys());

            UtangardPlugin.Log.LogWarning(warning.ToString());
        }

        /// <summary>Every discovered key, with what sets it. Verbose by nature, hence a flag.</summary>
        private static void LogAll()
        {
            UtangardPlugin.Log.LogInfo("Defeat keys set by creatures in this world ("
                + Setters.Count + "):");

            var keys = new List<string>(Setters.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string key in keys)
                UtangardPlugin.Log.LogInfo(
                    "  " + key + " <- " + string.Join(", ", Setters[key].ToArray()));
        }

        /// <summary>
        /// The keys that look like a boss's, for the warning.
        ///
        /// Trolls, surtlings and bats carry a defeat key too, and listing all thirty of them
        /// next to a warning buries the three lines that answer the question. "defeated_" is
        /// vanilla's own prefix for the ones that gate progression.
        /// </summary>
        private static string BossKeys()
        {
            var found = new List<string>();
            foreach (string key in Setters.Keys)
                if (key.StartsWith("defeated_", StringComparison.OrdinalIgnoreCase))
                    found.Add(key);

            found.Sort(StringComparer.OrdinalIgnoreCase);

            return found.Count == 0
                ? "(none begin with 'defeated_'; turn on Diagnostics.LogDefeatKeys for the "
                  + "full list)"
                : string.Join(", ", found.ToArray());
        }
    }
}

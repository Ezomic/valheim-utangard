using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;

namespace Utangard
{
    /// <summary>
    /// Every knob the mod has.
    ///
    /// The design argument, in short: Valheim already gates biomes, but it gates them with
    /// damage, and damage is something you can out-gear or out-run. A boss wall that simply
    /// refuses to let you in is the other extreme - it turns exploration into a locked door
    /// and takes away the one genuinely Valheim thing about walking into the Plains at the
    /// wrong time, which is the fear. This sits between the two. You can go anywhere. The
    /// land just will not feed you while you are there, and it holds a grudge on the way out.
    ///
    /// The gate is a table rather than a hardcoded progression because the interesting
    /// variations are all table edits: point every biome at one key for a single-boss gate,
    /// blank a row to let a biome through, or point a row at a key some other mod sets.
    /// </summary>
    internal static class UtangardConfig
    {
        private const string SecGate = "Gate";
        private const string SecFood = "Food";
        private const string SecBuffs = "Buffs";
        private const string SecSapped = "Sapped";
        private const string SecShow = "Presentation";
        private const string SecDiag = "Diagnostics";

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> GateOnGroup;
        public static ConfigEntry<bool> GateNeverRegresses;
        public static ConfigEntry<float> RosterDays;
        public static ConfigEntry<bool> BackfillFromCharacter;
        public static ConfigEntry<string> RosterDaysPerBoss;
        public static ConfigEntry<float> CatchUpDays;
        public static ConfigEntry<string> CatchUpDaysPerBoss;
        public static ConfigEntry<float> CreditRadius;
        public static ConfigEntry<string> ExcludePlayerIds;
        public static ConfigEntry<float> BorderMargin;

        // One row per biome. A global key name, or empty for "this biome is not gated".
        public static ConfigEntry<string> KeyMeadows;
        public static ConfigEntry<string> KeyBlackForest;
        public static ConfigEntry<string> KeySwamp;
        public static ConfigEntry<string> KeyMountain;
        public static ConfigEntry<string> KeyPlains;
        public static ConfigEntry<string> KeyMistlands;
        public static ConfigEntry<string> KeyAshLands;
        public static ConfigEntry<string> KeyDeepNorth;
        public static ConfigEntry<string> KeyOcean;

        public static ConfigEntry<float> FoodDrainMultiplier;
        public static ConfigEntry<bool> BlockEating;
        public static ConfigEntry<string> EatBlockedMessage;
        public static ConfigEntry<float> HealthRegenMultiplier;

        public static ConfigEntry<float> BuffDrainMultiplier;
        public static ConfigEntry<bool> BlockNewBuffs;
        public static ConfigEntry<bool> BlockRested;
        public static ConfigEntry<string> AlsoBlock;
        public static ConfigEntry<string> NeverBlock;
        public static ConfigEntry<string> BuffBlockedMessage;

        public static ConfigEntry<float> SappedStaminaRegen;
        public static ConfigEntry<float> SappedMaxSeconds;

        public static ConfigEntry<bool> ShowStatusEffects;
        public static ConfigEntry<string> MarkIconFrom;
        public static ConfigEntry<string> SappedIconFrom;
        public static ConfigEntry<string> EnterMessage;
        public static ConfigEntry<string> LeaveMessage;
        public static ConfigEntry<bool> NameTheBlockers;
        public static ConfigEntry<string> BlockedByPrefix;

        public static ConfigEntry<bool> Verbose;
        public static ConfigEntry<bool> LogGlobalKeys;
        public static ConfigEntry<bool> LogBlockedEffects;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(SecGate, "Enabled", true,
                "Master switch. Off leaves the game completely untouched - the patches stay "
                + "installed but every one of them returns immediately.");

            GateOnGroup = config.Bind(SecGate, "GateOnGroup", true,
                "Gate on whether everyone in the group has done the boss, rather than on "
                + "whether the boss is dead in this world.\n"
                + "The difference is the whole point. A world key is set once, by whoever "
                + "landed the kill, and credits everybody - including someone who joined "
                + "afterwards and has never seen the fight. On, the biome stays shut until "
                + "every character on the roster was personally present at that boss's "
                + "death, so carrying a friend through means actually bringing them.\n"
                + "The point is to keep a group together. Valheim groups come apart along "
                + "progression: one person plays more, gets ahead, and the rest arrive to find "
                + "the interesting content already cleared - so nobody wants to redo it and "
                + "the person ahead has no reason to go back. This makes helping the people "
                + "behind you the way you move forward yourself, rather than a favour. If your "
                + "friend has not killed The Elder, the Swamp is shut for you too, so going "
                + "back is the next thing on your own list.\n"
                + "Off restores the original behaviour: one kill opens the biome for all.");

            GateNeverRegresses = config.Bind(SecGate, "GateNeverRegresses", true,
                "Once the whole group has cleared a boss, that biome stays open forever, even "
                + "if someone new joins later who has not done it.\n"
                + "Without this the gate runs backwards: a friend arriving with a fresh "
                + "character decides nobody has done Eikthyr and shuts the Black Forest for "
                + "the people who killed him - retroactively, and for as long as that friend "
                + "keeps logging in. Progress the group paid for should not be revocable by "
                + "somebody else's arrival.\n"
                + "What a newcomer still gates is everything the group has NOT yet cleared, "
                + "which is where the 'bring your friend' pressure belongs anyway. Off makes "
                + "the gate strictly weakest-link at all times.");

            RosterDays = config.Bind(SecGate, "RosterDays", 14f,
                "How many days a character counts for after it was last seen. Real days, not "
                + "in-game ones.\n"
                + "This is the answer to the obvious problem with a group gate: an alt made "
                + "once, or a friend who visited for one evening, would otherwise hold every "
                + "biome shut forever. Leaving is automatic and needs no admin command - stop "
                + "logging in and you stop counting. The cost is that someone on a long "
                + "holiday silently drops out and the gate may open without them, so set this "
                + "comfortably longer than your group's normal gap between sessions.");

            BackfillFromCharacter = config.Bind(SecGate, "BackfillFromCharacter", true,
                "Credit a character for a boss its own file says it has attended, as long as "
                + "that boss has already died in this world.\n"
                + "This is the migration path and nothing else: nobody's past kills were "
                + "recorded before this mod existed, so without it, installing on a "
                + "long-running world gates every biome until every boss is killed again.\n"
                + "It is also the one hole in the 'you had to be there' rule. The record it "
                + "reads lives on the client and is not world-specific, so a player who was "
                + "never at the fight is credited the moment somebody else kills that boss "
                + "here. Requiring the world to have seen the boss die bounds that - imported "
                + "credit cannot open a biome on its own - but it does not close it.\n"
                + "Off makes the rule exact: credit comes only from kills this mod watched "
                + "happen. Correct for a world that started with the mod installed, and "
                + "punishing for one that did not.");

            RosterDaysPerBoss = config.Bind(SecGate, "RosterDaysPerBoss", "",
                "Per-boss overrides for RosterDays, as 'key:days' pairs separated by commas. "
                + "For example: defeated_eikthyr:7, defeated_fader:60\n"
                + "A short window for an early boss says 'keep up or stop counting'; a long "
                + "one for a late boss keeps somebody on holiday from being written out of a "
                + "fight the group has been building towards for weeks. Anything not listed "
                + "uses RosterDays.\n"
                + "Written as one line rather than nine config entries because the interesting "
                + "case is overriding one or two of them, not filling in a table.");

            CatchUpDays = config.Bind(SecGate, "CatchUpDays", 0f,
                "Fallback deadline, in days, for any boss not named in CatchUpDaysPerBoss. "
                + "0 means no deadline for those.\n"
                + "Without a deadline of some kind, one person who stops logging in holds a "
                + "biome shut for everybody until RosterDays finally drops them, which can be "
                + "weeks.");

            CatchUpDaysPerBoss = config.Bind(SecGate, "CatchUpDaysPerBoss",
                "defeated_eikthyr:1, defeated_gdking:2, defeated_bonemass:3, "
                + "defeated_dragon:4, defeated_goblinking:5, defeated_queen:6, defeated_fader:7",
                "Days the rest of the group has to catch up once the first player has done a "
                + "boss, after which the biome opens whether they did or not.\n"
                + "The clock starts the first time this mod records anyone as having that boss "
                + "in this world, and is written once and never moved.\n"
                + "The default is a ladder: one day for Eikthyr and one more for each boss "
                + "after. Early bosses are short trips somebody can be brought along on the "
                + "same evening, so a day is enough pressure; a late boss is a whole evening's "
                + "expedition that has to be organised, and holding the group to one day there "
                + "would just mean the deadline always wins and the gate never does.");

            CreditRadius = config.Bind(SecGate, "CreditRadius", 100f,
                "How close to a dying boss a character has to be to be credited for it, in "
                + "metres.\n"
                + "Deliberately generous. Only one machine sees a boss die - the client that "
                + "owns it - so it credits everyone standing nearby on the group's behalf, "
                + "and the two ways of getting that wrong are not symmetric. Too tight and "
                + "somebody who fought the whole battle is not credited, which holds the gate "
                + "shut for the entire group with no way to fix it short of killing the boss "
                + "again. Too loose and a bystander is credited, which costs one person's "
                + "sense of having earned it. Prefer too loose.");

            ExcludePlayerIds = config.Bind(SecGate, "ExcludePlayerIds", "",
                "Character IDs that never count towards the group gate, comma-separated. The "
                + "manual override for a character that has to keep playing but should not "
                + "hold the gate. IDs rather than names, because global keys are lowercased "
                + "and two characters can share a name. The roster dump on spawn prints both.");

            BorderMargin = config.Bind(SecGate, "BorderMargin", 5f,
                "How far the gate reaches past the edge of a gated biome, in metres. Without "
                + "it the border is a line you can stand a step behind: walk out of the "
                + "Swamp, eat, walk back, and the drain is a mild inconvenience rather than "
                + "a reason to leave. With it the edge is a band you have to actually clear, "
                + "and the biome you can see from is still the biome you are in. Costs eight "
                + "biome lookups a step, cached, so it is not free but it is close. 0 turns "
                + "it off and puts the gate exactly on the border.");

            // The defaults are the vanilla progression, offset by one: the key that lets you
            // into a biome is the boss of the biome before it. Note this walls off the Black
            // Forest copper run until Eikthyr is down, which is the intended shape but is
            // also the one row most people will want to blank first.
            //
            // The key names are the strings the game itself writes into the world's global
            // key set. The first five are in the GlobalKeys enum in assembly_valheim; the
            // Queen's and Fader's are set from prefab data instead and so are not in that
            // enum - they still work here because ZoneSystem.GetGlobalKey takes a plain
            // string. Turn on Diagnostics.LogGlobalKeys to see what your world actually has.
            KeyMeadows = BindKey(config, "Meadows", "",
                "Ungated by default. This is where you start.");
            KeyBlackForest = BindKey(config, "BlackForest", "defeated_eikthyr",
                "Eikthyr.");
            KeySwamp = BindKey(config, "Swamp", "defeated_gdking",
                "The Elder. 'gdking' really is the game's name for him.");
            KeyMountain = BindKey(config, "Mountain", "defeated_bonemass",
                "Bonemass.");
            KeyPlains = BindKey(config, "Plains", "defeated_dragon",
                "Moder.");
            KeyMistlands = BindKey(config, "Mistlands", "defeated_goblinking",
                "Yagluth.");
            KeyAshLands = BindKey(config, "AshLands", "defeated_queen",
                "The Queen. Not in the GlobalKeys enum - verify against LogGlobalKeys.");
            KeyDeepNorth = BindKey(config, "DeepNorth", "defeated_fader",
                "Fader. Not in the GlobalKeys enum - verify against LogGlobalKeys.");
            KeyOcean = BindKey(config, "Ocean", "",
                "Ungated by default. Gating the ocean gates every crossing to every biome, "
                + "including the ones you are allowed into.");

            FoodDrainMultiplier = config.Bind(SecFood, "FoodDrainMultiplier", 5f,
                "How much faster food burns down in a gated biome. 5 turns a 1600-second "
                + "meal into a 320-second one. 1 disables the drain and leaves only the "
                + "refusal to eat, which is a much gentler mod - you keep what you brought.");

            BlockEating = config.Bind(SecFood, "BlockEating", true,
                "Refuse to eat anything at all in a gated biome. This is the 'force' half: "
                + "the drain alone can be beaten by carrying more food, and the block is "
                + "what makes the timer real.");

            EatBlockedMessage = config.Bind(SecFood, "EatBlockedMessage",
                "The land will not feed you here",
                "Shown centre-screen when a bite is refused.");

            HealthRegenMultiplier = config.Bind(SecFood, "HealthRegenMultiplier", 0f,
                "Health regeneration in a gated biome, as a fraction of normal. 0 is 'wounds "
                + "do not close here'. It belongs in this section because food is the only "
                + "passive healing Valheim has - Player.UpdateFood adds up m_foodRegen every "
                + "ten seconds and heals you by it - so this multiplies exactly the healing "
                + "the food you are not allowed to eat would have given. Healing that comes "
                + "from an effect you were carrying is drained by the Buffs section instead. "
                + "1 leaves healing alone, which makes a gated biome survivable in a way "
                + "resting off a bad fight makes it liveable.");

            BuffDrainMultiplier = config.Bind(SecBuffs, "BuffDrainMultiplier", 5f,
                "How much faster an already-running buff burns down in a gated biome. Only "
                + "applies to the effects this mod considers buffs - see the Buffs section "
                + "below. Harmful effects are never touched, because speeding up Poison or "
                + "Freezing would be a mercy, not a penalty.");

            BlockNewBuffs = config.Bind(SecBuffs, "BlockNewBuffs", true,
                "Refuse to apply any new buff in a gated biome.");

            BlockRested = config.Bind(SecBuffs, "BlockRested", true,
                "Treat Rested and Resting as buffs, so a fire and a roof buy you nothing "
                + "inside a gated biome. This is the single harshest switch in the mod: it "
                + "means a forward base in the Plains gives shelter and warmth but no "
                + "regeneration. Turn it off and a well-built camp becomes a real answer.");

            AlsoBlock = config.Bind(SecBuffs, "AlsoBlock", "",
                "Extra status effect names to treat as buffs, comma-separated. The mod finds "
                + "potions and meads by walking ObjectDB for anything an item applies when "
                + "consumed, and guardian powers by their GP_ prefix, so this list is only "
                + "for the odd one out. Turn on Diagnostics.LogBlockedEffects to see names.");

            NeverBlock = config.Bind(SecBuffs, "NeverBlock", "Puke",
                "Status effect names to leave alone even if the rules above caught them, "
                + "comma-separated. Wins over AlsoBlock.\n"
                + "Puke is here because the first run in a real world found it: something "
                + "applies it on consume, so the potion rule catches it, and it is a debuff. "
                + "Blocking it would have handed the player an immunity to bad food in the "
                + "one biome meant to be punishing them, and the drain would have made it "
                + "wear off faster there than anywhere else. This is the exact failure the "
                + "harmful-allowlist design was meant to avoid, arriving from the other "
                + "direction - worth remembering if a future update adds another debuff that "
                + "an item hands you.");

            BuffBlockedMessage = config.Bind(SecBuffs, "BuffBlockedMessage",
                "The land turns your power aside",
                "Shown centre-screen when a potion or a guardian power is refused. Effects "
                + "that arrive without the player asking - equipment, weather, another "
                + "player's buff - are refused silently, because a message for each of those "
                + "would be a wall of text nobody reads.");

            SappedStaminaRegen = config.Bind(SecSapped, "StaminaRegenMultiplier", 0.25f,
                "Stamina regeneration while Sapped, as a fraction of normal. 0.25 is the "
                + "75% penalty. This is a multiplier applied alongside every other one the "
                + "game has, so it stacks with food and with Rested rather than replacing "
                + "them.");

            SappedMaxSeconds = config.Bind(SecSapped, "MaxSeconds", 30f,
                "Ceiling on how much Sapped you can accumulate. One second in a gated biome "
                + "buys one second of it, so this is also 'how long you must stand in the "
                + "biome before the penalty is at full length'. It keeps ticking down after "
                + "you leave, which is the point - a dash in and out still costs you.");

            ShowStatusEffects = config.Bind(SecShow, "ShowStatusEffects", true,
                "Show the two effects on the HUD. Off makes the mod invisible, which is "
                + "atmospheric and also completely baffling to a new player.");

            // The HUD skips any status effect with a null icon (SEMan.GetHUDStatusEffects
            // checks it), so these are not decoration - without a resolvable icon the
            // effects exist and do their work but never appear. Borrowing a vanilla sprite
            // costs nothing and matches the art by construction; a hand-drawn png is the
            // obvious upgrade and is one field away.
            MarkIconFrom = config.Bind(SecShow, "MarkIconFrom", "Poison",
                "Name of the vanilla status effect whose icon the in-biome marker borrows.");

            SappedIconFrom = config.Bind(SecShow, "SappedIconFrom", "Encumbered",
                "Name of the vanilla status effect whose icon Sapped borrows. Encumbered's "
                + "weight reads as 'heavy and slow', which is what the effect does.");

            EnterMessage = config.Bind(SecShow, "EnterMessage",
                "Something here refuses you",
                "Shown once on entering a gated biome. Blank to say nothing.");

            LeaveMessage = config.Bind(SecShow, "LeaveMessage",
                "The land loosens its grip",
                "Shown once on leaving a gated biome. Blank to say nothing.");

            NameTheBlockers = config.Bind(SecShow, "NameTheBlockers", true,
                "Under a group gate, name the characters the biome is still waiting on. "
                + "Worth leaving on: the honest question a player asks is 'why is this still "
                + "shut when I killed it myself', and without an answer the only way to find "
                + "one is to read a log file. Off if you would rather it stayed mysterious.");

            BlockedByPrefix = config.Bind(SecShow, "BlockedByPrefix", "Still owed by:",
                "Prefix for that list of names.");

            Verbose = config.Bind(SecDiag, "Verbose", false,
                "Log every gate transition and every blocked effect as it happens.");

            LogGlobalKeys = config.Bind(SecDiag, "LogGlobalKeys", true,
                "Log the world's global keys once when a world loads. This is how you check "
                + "that the key names in the Gate table match what your world actually "
                + "records - a typo there fails open and gates nothing, silently.");

            LogBlockedEffects = config.Bind(SecDiag, "LogBlockedEffects", false,
                "Log the full list of status effects the mod decided are buffs, once, when "
                + "ObjectDB is built. The list to consult before editing AlsoBlock.");

            // The buff set is computed once from ObjectDB, so anything that feeds into it has
            // to say when it changes or the edit appears to do nothing until the next world
            // load - which is exactly the "I changed the config and it had no effect" trap.
            AlsoBlock.SettingChanged += (s, e) => InvalidateNameSets();
            NeverBlock.SettingChanged += (s, e) => InvalidateNameSets();
            BlockRested.SettingChanged += (s, e) => BlockedEffects.Rebuild();
            ExcludePlayerIds.SettingChanged += (s, e) => _excludedIds = null;
        }

        private static ConfigEntry<string> BindKey(
            ConfigFile config, string biome, string defaultKey, string note)
        {
            return config.Bind(SecGate, "Key_" + biome, defaultKey,
                note + " Global key that must be set before this biome stops withering you. "
                + "Blank means the biome is never gated.");
        }

        /// <summary>
        /// The global key a biome demands, or null if it demands nothing.
        ///
        /// Read straight off the ConfigEntry rather than through a cached dictionary, so
        /// editing the file in ConfigurationManager takes effect on the next step rather
        /// than the next restart. It is a switch and a string compare; it can afford to run
        /// per tick.
        /// </summary>
        public static string RequiredKeyFor(Heightmap.Biome biome)
        {
            string key;
            switch (biome)
            {
                case Heightmap.Biome.Meadows: key = KeyMeadows.Value; break;
                case Heightmap.Biome.BlackForest: key = KeyBlackForest.Value; break;
                case Heightmap.Biome.Swamp: key = KeySwamp.Value; break;
                case Heightmap.Biome.Mountain: key = KeyMountain.Value; break;
                case Heightmap.Biome.Plains: key = KeyPlains.Value; break;
                case Heightmap.Biome.Mistlands: key = KeyMistlands.Value; break;
                case Heightmap.Biome.AshLands: key = KeyAshLands.Value; break;
                case Heightmap.Biome.DeepNorth: key = KeyDeepNorth.Value; break;
                case Heightmap.Biome.Ocean: key = KeyOcean.Value; break;

                // Biome.None is what a position with no heightmap reports, which in practice
                // means dungeon interiors - they sit in their own far-off zone. Leaving it
                // ungated means a crypt is a refuge from the biome above it. That is a
                // consequence of the game's layout rather than a decision, but it is a
                // defensible one and the alternative is tracking the player through doors.
                default: return null;
            }

            return string.IsNullOrEmpty(key) ? null : key.Trim();
        }

        /// <summary>
        /// Every distinct global key the gate table names.
        ///
        /// Publishing is driven off this rather than off a fixed list of bosses, so pointing
        /// a row at some other mod's key makes that key start replicating too, with no code
        /// change. Blank rows drop out on their own.
        /// </summary>
        public static IEnumerable<string> AllGateKeys()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Heightmap.Biome biome in GateableBiomes)
            {
                string key = RequiredKeyFor(biome);
                if (key != null && seen.Add(key)) yield return key;
            }
        }

        /// <summary>
        /// The nine gate rows, for handing to Core's config sync in one go. A row that
        /// differed between host and client would put two players in different biomes as far
        /// as the gate is concerned, which is the exact desync the sync exists to stop.
        /// </summary>
        public static ConfigEntryBase[] GateKeyEntries()
        {
            return new ConfigEntryBase[]
            {
                KeyMeadows, KeyBlackForest, KeySwamp, KeyMountain, KeyPlains,
                KeyMistlands, KeyAshLands, KeyDeepNorth, KeyOcean
            };
        }

        /// <summary>
        /// How many days a character counts for towards this particular boss.
        ///
        /// Per boss rather than per biome, because the boss is what is being waited on - two
        /// biomes pointed at one key are one wait, not two.
        /// </summary>
        public static float RosterDaysFor(string bossKey)
        {
            float over;
            return TryPerBoss(RosterDaysPerBoss.Value, bossKey, out over) ? over : RosterDays.Value;
        }

        /// <summary>Days the group has to catch up on this boss, or 0 for no deadline.</summary>
        public static float CatchUpDaysFor(string bossKey)
        {
            float over;
            return TryPerBoss(CatchUpDaysPerBoss.Value, bossKey, out over) ? over : CatchUpDays.Value;
        }

        /// <summary>
        /// Parse a "key:days, key:days" line. Re-read rather than cached: these are consulted
        /// a handful of times per gate query on lines that hold two or three entries, and a
        /// cache would be another thing to invalidate when the config changes.
        /// </summary>
        private static bool TryPerBoss(string line, string bossKey, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(bossKey)) return false;

            foreach (string entry in line.Split(','))
            {
                int split = entry.IndexOf(':');
                if (split <= 0) continue;

                string name = entry.Substring(0, split).Trim();
                if (!string.Equals(name, bossKey, StringComparison.OrdinalIgnoreCase)) continue;

                return float.TryParse(entry.Substring(split + 1).Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            return false;
        }

        /// <summary>Whether any row of the gate table asks for this key.</summary>
        public static bool IsGateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            foreach (string gateKey in AllGateKeys())
                if (string.Equals(gateKey, key, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        public static readonly Heightmap.Biome[] GateableBiomes =
        {
            Heightmap.Biome.Meadows,
            Heightmap.Biome.BlackForest,
            Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains,
            Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands,
            Heightmap.Biome.DeepNorth,
            Heightmap.Biome.Ocean
        };

        private static HashSet<string> _excludedIds;

        public static HashSet<string> ExcludedPlayerIds
        {
            get
            {
                if (_excludedIds == null) _excludedIds = Split(ExcludePlayerIds.Value);
                return _excludedIds;
            }
        }

        private static HashSet<string> _alsoBlock;
        private static HashSet<string> _neverBlock;

        public static HashSet<string> AlsoBlockNames
        {
            get { EnsureNameSets(); return _alsoBlock; }
        }

        public static HashSet<string> NeverBlockNames
        {
            get { EnsureNameSets(); return _neverBlock; }
        }

        private static void InvalidateNameSets()
        {
            _alsoBlock = null;
            _neverBlock = null;
            BlockedEffects.Rebuild();
        }

        private static void EnsureNameSets()
        {
            if (_alsoBlock != null) return;
            _alsoBlock = Split(AlsoBlock.Value);
            _neverBlock = Split(NeverBlock.Value);
        }

        private static HashSet<string> Split(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in (csv ?? "").Split(','))
            {
                var name = entry.Trim();
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Utangard
{
    /// <summary>
    /// `utangard`, the console command. Five verbs: `foothold`, the local character's two bars
    /// in every biome as the unlocks read them; `biomes` and `creatures`, the raw material
    /// the foothold tables were written from; `players`, the tabs of the compendium page with the numbers each holds (LHM-61); and `deaths`, which machine ran a creature's death
    /// (see DeathsReport).
    ///
    /// The design Robbin settled on 2026-09-24 is that a character earns back eating and health
    /// regeneration in a biome that is still gated, one biome at a time, by fighting there and
    /// by exploring it. Numbers for that cannot be picked from a desk, because both halves of
    /// the answer are data that exists only in a running world: which creatures the spawn
    /// tables put in each biome, and how big each biome is on this world's map. This prints
    /// both, beside what the local character has actually done, so the thresholds are chosen
    /// from real lists rather than guessed.
    ///
    /// Read-only, so registered isCheat: false. It needs no devcommands and marks nothing.
    /// `utangardtest` is the one that writes, and it is a cheat on purpose (see RegisterTest).
    ///
    /// <b>What each half reads, and what it cannot see.</b>
    ///
    ///   Kills come from the tally Utangard keeps for itself since 2026-09-27 (KillTally): kills
    ///   this character made or helped with in this world, seen as they happened, keyed by the
    ///   creature's display token ($enemy_wolf). It used to be the game's lifetime tally in the
    ///   PlayerProfile, which took in other worlds, devcommands and tamed animals. The tally only
    ///   keeps kinds a points line pays for, so every other creature reads as 0 here.
    ///
    ///   Creatures per biome come from the world's spawn tables, the same source the shared
    ///   BiomeIndex reads. Those are the overworld spawns only: a draugr placed by a crypt's own
    ///   spawner, or a camp's fulings, are not in them. A row gated behind a world key or a
    ///   weather is marked, because a creature you cannot meet yet is not one a threshold
    ///   should count.
    ///
    ///   Exploration comes from the minimap's own explored bits - m_explored, the fog this
    ///   character lifted by walking, not m_exploredOthers, which a cartography table hands
    ///   out - checked against WorldGenerator.GetBiome at each map pixel. The game does not keep
    ///   exploration per biome; this works it out from what it does keep.
    /// </summary>
    internal static class DevConsole
    {
        /// <summary>
        /// Process-wide: Terminal's command table is a private static nothing clears, so a
        /// second registration is a duplicate that outlives the world.
        /// </summary>
        private static bool _registered;

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        internal static class Hook
        {
            private static void Postfix()
            {
                Register();
            }
        }

        private static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("utangard",
                "utangard foothold | biomes | creatures | players | deaths [<creature> [since <notowner>]] - your foothold in each locked biome, the raw numbers behind it, every player tab on the compendium page, and which machine ran a creature's death",
                OnCommand, isCheat: false);

            RegisterTest();
        }

        /// <summary>
        /// `utangardtest kills &lt;creature&gt; &lt;count&gt;`, which sets Utangard's own count of one
        /// creature for this character in this world, so a scenario can put a Fighting bar where it
        /// needs it without fifty kills first.
        ///
        /// A command of its own, registered isCheat, rather than a fourth verb of `utangard`: the
        /// cheat flag belongs to a whole command, and `utangard` has to stay one a player can type
        /// on a server without devcommands. This one hands out a foothold for nothing, so it is a
        /// cheat. Devkit's `mod` step calls its handler directly, past RunAction, so a scenario
        /// runs it without devcommands and without the cheat mark on the character. Vaettir's
        /// `furrow` and `furrowtest` are split the same way for the same reason.
        ///
        /// Failable, so a refusal comes back as a string: RunAction prints it in red, and Devkit
        /// fails the step on it rather than reading a refusal as having run.
        /// </summary>
        private static void RegisterTest()
        {
            new Terminal.ConsoleCommand("utangardtest",
                "utangardtest kills <creature> <count> - set how many of that creature Utangard has counted you killing in this world, for tests",
                new Terminal.ConsoleEventFailable(OnTest), isCheat: true);
        }

        private static object OnTest(Terminal.ConsoleEventArgs args)
        {
            var term = args.Context;
            if (term == null) return "no console to answer in";

            var what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (what == "kills") return SetKills(term, args);

            term.AddString("utangardtest kills <creature> <count> - set this character's count of that creature in this world, as if it had killed that many. 0 clears it.");
            term.AddString("<creature> is a prefab name from a points line: JotunWarrior, Troll, Greydwarf...");
            return true;
        }

        private static object SetKills(Terminal term, Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 4) return "utangardtest kills <creature> <count>";

            var scene = ZNetScene.instance;
            if (scene == null) return "no world loaded yet";

            int count;
            if (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 0)
                return "'" + args[3] + "' is not a count";

            var go = scene.GetPrefab(args[2]);
            Character character;
            if (go == null || !go.TryGetComponent(out character)) return "no creature called " + args[2];

            var token = character.m_name ?? "";

            // Only a kind some points line pays for, because that is all the tally ever keeps.
            // Setting any other would write a number nothing reads, and a scenario would pass on
            // it. Zero is the exception: it is already zero, and a scenario clearing a whole line
            // should not fail on a creature the line no longer pays for.
            if (!Foothold.Pays(token))
            {
                if (count != 0)
                    return args[2] + " (" + token + ") is worth nothing on any points line, so Utangard does not count it";

                Say(term, "utangardtest kills: " + go.name + " (" + token + ") is worth nothing on any points line, so its count is 0 already.");
                return true;
            }

            int had;
            if (!KillTally.Set(token, count, out had))
                return "there is no character in a world to set it on";

            Say(term, "utangardtest kills: " + go.name + " (" + token + ") was " + had + ", now " + count
                      + ", for " + Player.m_localPlayer.GetPlayerName() + " in this world.");
            return true;
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            var term = args.Context;
            if (term == null) return;

            var what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (what == "foothold") { FootholdReport(term); return; }
            if (what == "biomes") { Biomes(term); return; }
            if (what == "creatures") { Creatures(term); return; }
            if (what == "deaths") { DeathsReport(term, args); return; }
            if (what == "players") { PlayersReport(term); return; }

            term.AddString("utangard foothold - per biome: your Fighting and Discovery bars, what each kind of creature put in, and what is unlocked");
            term.AddString("utangard biomes - per biome: its creatures, the kills of each Utangard counted for you in this world (only creatures a points line pays for), and how much of it you have explored");
            term.AddString("utangard creatures - every creature in the foothold tables, checked against the game");
            term.AddString("utangard deaths <creature> - whether it dies through its animation, how many this machine saw die this session and whether it had them, and who has the nearest live one");
            term.AddString("utangard players - every tab on the compendium page: who, whether online, and the bars each has published for the locked biomes");
            term.AddString("utangard deaths - every creature that dies through its animation; utangard deaths <creature> since <notowner> - whether this machine ran one it did not own since notowner stood there");
        }

        // ----------------------------------------------------------------- players ------

        /// <summary>
        /// `utangard players`: the compendium's player tabs as lines, with the numbers each shows
        /// (LHM-61). Read-only. For the paired scenarios, which cannot read a bar off the screen
        /// without knowing the other character's name, and for asking why a tab says "no data".
        /// </summary>
        private static void PlayersReport(Terminal term)
        {
            var members = PlayerBars.Members();
            if (members.Count == 0)
            {
                Say(term, "utangard players: no world or no character yet.");
                return;
            }

            foreach (var member in members) Say(term, PlayerBars.Describe(member));
        }

        // ------------------------------------------------------------------ deaths ------

        /// <summary>What `utangard deaths` counts for one prefab, this session only.</summary>
        private sealed class DeathCount
        {
            internal int AsOwner;
            internal int NotOwner;
            internal int BossCredit;
        }

        private static readonly Dictionary<string, DeathCount> Deaths =
            new Dictionary<string, DeathCount>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How far `utangard deaths` looks for a live one. Devkit's `kill` reaches as far and takes
        /// the nearest the same way, so the two name the same creature.
        /// </summary>
        private const float DeathsReach = 40f;

        /// <summary>
        /// A creature's OnDeath was entered on this machine. Called from KillTally's prefix, which
        /// runs wherever OnDeath is reached, before OnDeath's own owner check and before the body is
        /// destroyed, so the view can still say who has the creature. A postfix could not: OnDeath
        /// ends in ZNetScene.Destroy, which resets the view, and after that IsValid is false on the
        /// owner as well.
        /// </summary>
        internal static void SawDeath(Character creature)
        {
            try
            {
                if (creature == null || creature.IsPlayer()) return;

                ZNetView nview;
                if (!creature.TryGetComponent(out nview) || !nview.IsValid()) return;

                var count = DeathsOf(Utils.GetPrefabName(creature.gameObject));
                if (nview.IsOwner()) count.AsOwner++;
                else count.NotOwner++;
            }
            catch (Exception)
            {
                // A test readout is not worth a broken death. The drops, the defeat key and the
                // despawn all come after the prefix this runs in.
            }
        }

        /// <summary>
        /// KillCredit got past its owner check for this creature and is about to credit the players
        /// at the kill.
        /// </summary>
        internal static void RanBossCredit(Character creature)
        {
            try
            {
                if (creature == null) return;
                DeathsOf(Utils.GetPrefabName(creature.gameObject)).BossCredit++;
            }
            catch (Exception)
            {
                // As in SawDeath: never into a death.
            }
        }

        private static DeathCount DeathsOf(string prefab)
        {
            var name = prefab ?? "";

            DeathCount count;
            if (!Deaths.TryGetValue(name, out count)) Deaths[name] = count = new DeathCount();
            return count;
        }

        /// <summary>
        /// `utangard deaths &lt;creature&gt;`: which machine ran that creature's death, and what came of it.
        ///
        /// Written for paired-kill-credit-killer.txt and paired-kill-credit-watcher.txt (LHM-36). In
        /// 1.0 a creature with m_deathAnimation reaches Character.OnDeath through its animation's Die
        /// event on every client animating it, not only on the one that has it, and three mods
        /// credited kills in OnDeath postfixes without asking who had the creature. Whether their
        /// fixes hold is a question about the OTHER machine, and nothing a scenario could read said
        /// whether that machine had run the death at all. Without that, "the watcher's count did not
        /// move" passes just as well for a watcher that never saw anything die.
        ///
        /// Every token is about this machine only:
        ///   deathanim   whether the creature dies through its animation, the only road by which a
        ///               machine that does not have it reaches OnDeath at all;
        ///   gatekey     whether its death writes a key the gate table asks for, which is when the
        ///               boss credit has anything to do;
        ///   asowner, notowner   how many entered OnDeath here this session, by whether this
        ///               machine had the creature at that moment;
        ///   bosscredit  how many times Utangard's boss credit ran here for one, past its owner check;
        ///   nearest     who has the nearest live one, the one Devkit's `kill` would hit, so a
        ///               scenario can check that it owns what it is about to kill;
        ///   kills       your kills of it in Utangard's own tally for this world;
        ///   nonowner    only with `since <n>`: how many deaths of it this machine ran without
        ///               having the creature since notowner stood at n, in words.
        ///
        /// Read-only. The counts are kept in memory from the start of the process, so a scenario
        /// reads them before and after rather than for a number of its own.
        ///
        /// With no creature it lists every creature that dies through its animation. The paired
        /// scenarios were written around Greydwarf and Eikthyr on the belief that both did, and
        /// their first run on 2026-09-30 printed deathanim=no for each: neither can ever be run by
        /// a machine that does not have it, so that run could not test a single owner check. The
        /// list is what a pair that can test them has to be built from, instead of another guess.
        /// </summary>
        private static void DeathsReport(Terminal term, Terminal.ConsoleEventArgs args)
        {
            var scene = ZNetScene.instance;
            var player = Player.m_localPlayer;
            if (scene == null || player == null)
            {
                Say(term, "utangard deaths: no character in a world yet.");
                return;
            }

            if (args.Length < 3)
            {
                Say(term, DeathAnimations(scene));
                Say(term, "utangard deaths <creature> [since <notowner>] - one of them by prefab name: Greydwarf, Eikthyr...");
                return;
            }

            var go = scene.GetPrefab(args[2]);
            Character creature;
            if (go == null || !go.TryGetComponent(out creature))
            {
                Say(term, "utangard deaths: no creature called " + args[2] + ".");
                return;
            }

            var name = go.name;
            var head = "utangard deaths " + name + ": ";

            var key = creature.m_defeatSetGlobalKey;
            var gate = string.IsNullOrEmpty(key) ? "none" : UtangardConfig.IsGateKey(key) ? "yes" : "no";

            Say(term, head + "deathanim=" + (creature.m_deathAnimation ? "yes" : "no") + " gatekey=" + gate + "   ("
                      + (creature.m_deathAnimation
                          ? "it dies through its animation, so every machine animating it reaches OnDeath"
                          : "it dies straight from CheckDeath, so only the machine that has it reaches OnDeath")
                      + (string.IsNullOrEmpty(key) ? "" : "; its death writes " + key) + ")");

            DeathCount seen;
            Deaths.TryGetValue(name, out seen);

            var off = (Seams.FootholdKills ? "" : " The death hook did not go on this session, so asowner and notowner cannot move.")
                      + (Seams.KillCredit && UtangardConfig.Enabled.Value && UtangardConfig.GateOnGroup.Value
                          ? ""
                          : " The boss credit is off this session (its hook, Enabled or GateOnGroup), so bosscredit cannot move.");

            Say(term, head + "asowner=" + (seen != null ? seen.AsOwner : 0)
                      + " notowner=" + (seen != null ? seen.NotOwner : 0)
                      + " bosscredit=" + (seen != null ? seen.BossCredit : 0)
                      + "   (deaths this machine ran this session, by whether it had the creature, and the boss credits it ran for one)"
                      + off);

            Say(term, head + NearestAlive(name, player));
            Say(term, head + KillsOf(creature));

            int since;
            if (args.Length > 4 && string.Equals(args[3], "since", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out since))
                Say(term, head + NonOwnerSince(seen != null ? seen.NotOwner : 0, since, creature.m_deathAnimation));
        }

        /// <summary>
        /// Whether this machine ran a death of it that it did not own since notowner stood at
        /// <paramref name="since"/>, said in words.
        ///
        /// For the paired kill-credit scenarios, which note notowner at their start and hand it
        /// back here at their end. Whether the refusal of a machine that does not have the creature
        /// was exercised at all is the one thing a passing run cannot say by itself, and a
        /// scenario has no way to subtract two numbers and print a sentence about the answer.
        ///
        /// It names the refusal, not "the owner checks": every death this machine owned went
        /// through the owner's side of the same check, so a line saying the checks were not
        /// exercised would have been false on the machine that had the creature.
        /// </summary>
        private static string NonOwnerSince(int now, int since, bool deathAnimation)
        {
            var ran = now - since;
            var from = " since notowner=" + since.ToString(CultureInfo.InvariantCulture);

            if (ran < 0)
                return "nonowner=unknown" + from + "   (notowner is " + now + " now, below that, so the number given was not read in this session)";

            if (ran > 0)
                return "nonowner=" + ran + from + "   (this machine ran " + ran
                       + " death(s) of it that it did not own, so the refusal of a machine that does not have it was exercised here)";

            return "nonowner=0" + from + "   (this machine ran no death of it that it did not own, so the refusal of a machine that does not have it was NOT exercised here"
                   + (deathAnimation ? ")" : ", and none can: it dies straight from CheckDeath, so only the machine that has it reaches OnDeath)");
        }

        /// <summary>
        /// Every creature in this world's prefab list that dies through its animation, which makes
        /// them the only ones whose death a machine that does not have them can run at all.
        /// </summary>
        private static string DeathAnimations(ZNetScene scene)
        {
            var names = new List<string>();

            if (scene.m_prefabs != null)
            {
                foreach (var prefab in scene.m_prefabs)
                {
                    Character character;
                    if (prefab == null || !prefab.TryGetComponent(out character) || character is Player) continue;
                    if (character.m_deathAnimation) names.Add(prefab.name);
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);

            return "utangard deaths: deathanim=" + names.Count
                   + "   (creatures that die through their animation, so a machine that does not have one can still run its death"
                   + (names.Count == 0 ? "; there are none in this world)" : "): " + string.Join(", ", names.ToArray()));
        }

        /// <summary>The nearest live one within DeathsReach, and which machine has it.</summary>
        private static string NearestAlive(string prefab, Player player)
        {
            var all = Character.GetAllCharacters();
            var reach = DeathsReach * DeathsReach;
            var nearest = reach;
            var alive = 0;
            Character best = null;

            if (all != null)
            {
                foreach (var character in all)
                {
                    if (character == null || character.IsDead()) continue;
                    if (!string.Equals(Utils.GetPrefabName(character.gameObject), prefab, StringComparison.OrdinalIgnoreCase)) continue;

                    var distance = (character.transform.position - player.transform.position).sqrMagnitude;
                    if (distance > reach) continue;

                    alive++;
                    if (distance > nearest) continue;

                    nearest = distance;
                    best = character;
                }
            }

            if (best == null) return "nearest=none alive=0   (none alive within " + DeathsReach.ToString("0", CultureInfo.InvariantCulture) + " m)";

            ZNetView nview;
            var who = !best.TryGetComponent(out nview) || !nview.IsValid() ? "nobody"
                    : nview.IsOwner() ? "here" : "elsewhere";

            return "nearest=" + who + " alive=" + alive + "   (the nearest live one, "
                   + Mathf.Sqrt(nearest).ToString("0.0", CultureInfo.InvariantCulture) + " m away, belongs to "
                   + (who == "here" ? "this machine" : who == "elsewhere" ? "another machine" : "no machine") + ")";
        }

        /// <summary>Your kills of it in Utangard's tally for this world, or why there is no number.</summary>
        private static string KillsOf(Character creature)
        {
            var token = creature.m_name ?? "";
            if (!Foothold.Pays(token))
                return "kills=unpaid   (" + token + " is on no points line, so Utangard does not count it)";

            var kills = KillTally.Here();
            if (kills == null)
                return "kills=unreadable   (" + (KillTally.WhyNot() ?? "the kill tally cannot be read") + ")";

            int n;
            kills.TryGetValue(token, out n);

            return "kills=" + n.ToString(CultureInfo.InvariantCulture) + "   (your kills of " + token
                   + " that Utangard counted in this world, assists included)";
        }

        /// <summary>
        /// `utangard foothold`: the local character's two bars in every biome, the way the unlocks
        /// read them. Read through Foothold.Read, the same call the compendium panel makes, so the
        /// console and the panel cannot disagree about a number.
        ///
        /// Every kind is listed, killed or not, with the cap shown where it bit: "why is my bar
        /// stuck at half" is nearly always one kind at its cap, and that is only visible beside the
        /// kinds that have not been touched yet. The kill counts are Utangard's own, per world.
        /// </summary>
        private static void FootholdReport(Terminal term)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Say(term, "utangard foothold: no character in a world yet.");
                return;
            }

            var inv = CultureInfo.InvariantCulture;
            var full = Discovery.FullPixels();
            var pixelKm2 = Discovery.PixelKm2();
            var bar = Foothold.FullBar();

            Say(term, "utangard foothold: " + player.GetPlayerName() + ". "
                      + (UtangardConfig.FootholdEnabled.Value ? "" : "FootholdEnabled is OFF, so nothing below unlocks anything. ")
                      + "A full Fighting bar is " + bar + " points. Eating at " + Foothold.EatAtPercent() + "% ("
                      + Foothold.EatAtPoints(bar) + " points), healing at a full bar and full discovery. No kind above "
                      + UtangardConfig.MaxFromOneKindPercent.Value + "% (" + Foothold.KindCap(bar) + " points).");

            // Said every time, because it is the first thing anybody asks when a bar is lower than
            // their memory of the fighting: which kills were counted at all.
            var whyNot = KillTally.WhyNot();
            Say(term, whyNot != null
                ? "utangard foothold: kills cannot be counted right now: " + whyNot + ". Every Fighting bar reads as empty."
                : "utangard foothold: kills are the ones Utangard saw you make or help with in this world. Not from "
                  + "other worlds or before this version, not of tamed animals, and not with devcommands.");

            if (!Discovery.Available())
                Say(term, "utangard foothold: the explored map cannot be read this session, so every Discovery bar is empty.");
            else
                Say(term, "utangard foothold: full discovery is "
                          + UtangardConfig.DiscoveryFullKm2.Value.ToString("0.##", inv) + " km2 = " + full + " map pixels of "
                          + (pixelKm2 * 1000000f).ToString("0", inv) + " m2. "
                          + (Discovery.Counting()
                              ? "Still counting this world's map (" + (Discovery.Progress() * 100f).ToString("0", inv)
                                + "%), so discovery can only go up from here."
                              : "Map counted. " + Discovery.Describe()));

            foreach (var s in Foothold.ReadAll())
            {
                if (!s.Gated && s.Kinds.Count == 0) continue;

                var state = !s.Gated ? "ungated" : s.Locked ? "LOCKED" : "open";
                if (s.Here) state += ", its rules apply to you now";

                var unlocked = !s.Locked
                    ? "nothing to unlock"
                    : (s.CanEat ? "eating allowed" : "eating at " + s.EatAtPercent + "%")
                      + ", " + (s.CanHeal ? "healing allowed" : "healing needs both full");

                // The percent is the one the panel and the refused meal print, rounded down.
                var fighting = s.FightingAvailable
                    ? s.Fighting + "/" + s.Full + " (" + s.FightingPercent.ToString(inv) + "%)"
                    : "unreadable";

                // Rounded down, as the compendium panel rounds it, so the two print the same number
                // and neither says 100% a pixel short of full. ToString("0") rounds half up, and
                // it had the console one ahead of the panel whenever the fraction passed a half.
                var discovery = s.DiscoveryAvailable
                    ? s.Discovered + "/" + s.DiscoveryFull + " px (" + Math.Floor(s.DiscoveryPercent).ToString("0", inv) + "%"
                      + (s.DiscoveryCounting ? ", still counting" : "") + ")"
                    : "unreadable";

                Say(term, s.Biome + " (" + state + "): fighting " + fighting + ", discovery " + discovery + " - " + unlocked + ".");

                if (s.Kinds.Count == 0)
                {
                    Say(term, "    no creatures on this biome's points line, so its Fighting bar cannot fill.");
                    continue;
                }

                var parts = new List<string>();
                foreach (var k in s.Kinds)
                {
                    var part = k.Name + " " + k.Points + "pt x" + k.Kills + " = " + k.Contribution;
                    if (k.Capped) part += " (capped at " + s.KindCap + ", " + k.Earned + " earned)";
                    parts.Add(part);
                }

                Say(term, "    " + string.Join(", ", parts.ToArray()));
            }
        }

        private static readonly Heightmap.Biome[] Order =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands,
            Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        private sealed class Creature
        {
            internal string Prefab;
            internal string Token;
            internal float Health;
            internal bool Unconditional;
            internal readonly List<string> Rows = new List<string>();
        }

        private static void Biomes(Terminal term)
        {
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            if (profile == null || Player.m_localPlayer == null)
            {
                Say(term, "utangard biomes: no character in a world yet.");
                return;
            }

            // Utangard's own tally, so "your kills" here is the number the Fighting bar uses. Empty
            // rather than missing when it cannot be read, and the header line says why.
            var kills = KillTally.Here() ?? new Dictionary<string, int>();
            var creatures = SpawnTables();
            Dictionary<Heightmap.Biome, int> area, seen;
            float pixelKm2;
            var mapNote = Exploration(out area, out seen, out pixelKm2);

            var whyNot = KillTally.WhyNot();
            Say(term, "utangard biomes: " + profile.GetName() + ". " + mapNote
                      + (whyNot != null ? " Kills cannot be read: " + whyNot + "." : ""));

            foreach (var biome in Order)
            {
                area.TryGetValue(biome, out var total);
                seen.TryGetValue(biome, out var mine);

                var totalKm2 = total * pixelKm2;
                var mineKm2 = mine * pixelKm2;
                var share = total > 0 ? 100f * mine / total : 0f;

                creatures.TryGetValue(biome, out var list);
                var rows = list != null ? list.Values.OrderBy(c => c.Prefab, StringComparer.Ordinal).ToList() : new List<Creature>();

                var kinds = 0;
                var killed = 0;
                var sum = 0L;
                var parts = new List<string>();
                foreach (var c in rows)
                {
                    kills.TryGetValue(c.Token ?? "", out var n);
                    if (c.Unconditional) kinds++;
                    if (n > 0) { killed++; sum += n; }
                    parts.Add(c.Prefab + (c.Unconditional ? "" : "*") + " " + c.Health.ToString("0", CultureInfo.InvariantCulture)
                              + "hp x" + n.ToString(CultureInfo.InvariantCulture));
                }

                Say(term, biome + ": " + totalKm2.ToString("0.0", CultureInfo.InvariantCulture) + " km2 on this map, you explored "
                          + mineKm2.ToString("0.00", CultureInfo.InvariantCulture) + " km2 ("
                          + share.ToString("0.0", CultureInfo.InvariantCulture) + "%). Creatures: "
                          + kinds + " kinds you can always meet. Utangard counted " + killed + " kinds, "
                          + sum.ToString(CultureInfo.InvariantCulture) + " kills.");
                if (parts.Count > 0) Say(term, "    " + string.Join(", ", parts.ToArray()));
            }

            Say(term, "utangard biomes: * = only spawns behind a world key, a weather or a persistent event. "
                      + "Spawn tables only - creatures a dungeon or a camp places itself are not in them. "
                      + "Each entry is health, then your kills: the ones Utangard counted in this world, assists "
                      + "included, and only for creatures a points line pays for.");

            // A creature in most biomes' tables is either a real wanderer or a row gated by
            // something this does not read. Print its raw rows so which one is decidable.
            var seenIn = new Dictionary<string, Creature>();
            var count = new Dictionary<string, int>();
            foreach (var pair in creatures)
                foreach (var c in pair.Value.Values)
                {
                    seenIn[c.Prefab] = c;
                    count.TryGetValue(c.Prefab, out var k);
                    count[c.Prefab] = k + 1;
                }

            foreach (var pair in count.Where(p => p.Value >= 5).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Say(term, "  in " + pair.Value + " biomes: " + pair.Key + ", rows:");
                foreach (var row in seenIn[pair.Key].Rows) Say(term, "    " + row);
            }
        }

        /// <summary>
        /// Every creature each biome's spawn rows can produce, bosses left out. A creature with
        /// even one unconditional row in a biome counts as always met there; one that only
        /// arrives behind a world key or a weather is marked, since a threshold that counted it
        /// could be impossible to meet on the day.
        /// </summary>
        private static Dictionary<Heightmap.Biome, Dictionary<string, Creature>> SpawnTables()
        {
            var result = new Dictionary<Heightmap.Biome, Dictionary<string, Creature>>();

            var spawn = UnityEngine.Object.FindObjectOfType<SpawnSystem>();
            if (spawn == null || spawn.m_spawnLists == null) return result;

            foreach (var list in spawn.m_spawnLists)
            {
                if (list == null || list.m_spawners == null) continue;

                foreach (var row in list.m_spawners)
                {
                    if (row == null || !row.m_enabled || row.m_devDisabled || row.m_prefab == null) continue;

                    Character character;
                    if (!row.m_prefab.TryGetComponent(out character)) continue;
                    if (character.IsBoss()) continue;

                    // Three gates, not two. m_requiredPersistentEvent was missed at first, and it
                    // is what put Elaking and the Jotun in every biome's list, Meadows included.
                    var conditional = !string.IsNullOrEmpty(row.m_requiredGlobalKey)
                                      || !string.IsNullOrEmpty(row.m_requiredPersistentEvent)
                                      || (row.m_requiredEnvironments != null && row.m_requiredEnvironments.Count > 0);

                    var describe = "'" + row.m_name + "' biomes=" + row.m_biome + " area=" + row.m_biomeArea
                                   + " key='" + row.m_requiredGlobalKey + "' event='" + row.m_requiredPersistentEvent
                                   + "' env=" + (row.m_requiredEnvironments != null ? row.m_requiredEnvironments.Count : 0)
                                   + " chance=" + row.m_spawnChance.ToString("0.#", CultureInfo.InvariantCulture)
                                   + " max=" + row.m_maxSpawned
                                   + " day=" + row.m_spawnAtDay + " night=" + row.m_spawnAtNight;

                    foreach (var biome in Order)
                    {
                        if ((row.m_biome & biome) == 0) continue;

                        if (!result.TryGetValue(biome, out var inBiome))
                            result[biome] = inBiome = new Dictionary<string, Creature>();

                        if (!inBiome.TryGetValue(row.m_prefab.name, out var c))
                            inBiome[row.m_prefab.name] = c = new Creature
                            {
                                Prefab = row.m_prefab.name, Token = character.m_name, Health = character.m_health,
                            };

                        if (!conditional) c.Unconditional = true;
                        if (!c.Rows.Contains(describe)) c.Rows.Add(describe);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Map pixels per biome, and how many of them this character uncovered on foot.
        /// Sampled every other pixel on a large map, which is plenty for a size and keeps the
        /// command under a couple of seconds.
        /// </summary>
        private static string Exploration(out Dictionary<Heightmap.Biome, int> area,
                                          out Dictionary<Heightmap.Biome, int> seen,
                                          out float pixelKm2)
        {
            area = new Dictionary<Heightmap.Biome, int>();
            seen = new Dictionary<Heightmap.Biome, int>();
            pixelKm2 = 0f;

            var map = Minimap.instance;
            var world = WorldGenerator.instance;
            if (map == null || world == null) return "No minimap or world generator, so no exploration.";

            BitArray explored = null;
            try
            {
                explored = Traverse.Create(map).Field("m_explored").GetValue<BitArray>();
            }
            catch (Exception)
            {
                explored = null;
            }

            var size = map.m_textureSize;
            var metres = map.m_pixelSize;
            var stride = size >= 1024 ? 2 : 1;
            var half = size / 2;
            const float Edge = 10000f;

            for (var y = 0; y < size; y += stride)
            {
                for (var x = 0; x < size; x += stride)
                {
                    var wx = (x - half) * metres;
                    var wz = (y - half) * metres;
                    if (wx * wx + wz * wz > Edge * Edge) continue;

                    var biome = world.GetBiome(wx, wz);
                    area.TryGetValue(biome, out var a);
                    area[biome] = a + 1;

                    if (explored != null && explored[y * size + x])
                    {
                        seen.TryGetValue(biome, out var s);
                        seen[biome] = s + 1;
                    }
                }
            }

            pixelKm2 = stride * stride * metres * metres / 1000000f;

            return "Map " + size + "x" + size + " at " + metres.ToString("0.#", CultureInfo.InvariantCulture)
                   + " m a pixel, explore radius " + map.m_exploreRadius.ToString("0", CultureInfo.InvariantCulture) + " m"
                   + (explored == null ? ", and the explored bits could not be read." : ".");
        }

        /// <summary>
        /// Prefabs deliberately left out of the foothold tables, checked alongside them so the
        /// prey reading can be compared with Robbin's calls. Two spellings where the game files
        /// carry both and only one of them is the creature.
        /// </summary>
        private static readonly string[] LeftOut =
        {
            "Deer", "Bat", "Hare", "SeekerBrood", "Seal", "Seal_Pup", "Seal_pup", "Moose_calf",
            "Asksvin_hatchling", "AsksvinHatchling", "Skeleton_Swamps",
        };

        /// <summary>
        /// Every creature in Foothold's tables, resolved against the running game: whether the
        /// prefab exists, the token the kill tally will file it under, its health, its faction,
        /// and whether its brain is a monster's or an animal's. Answers three questions before
        /// any of LHM-26 is built on the tables:
        ///
        ///   which of the new creatures the game treats as prey - an AnimalAI, or the AnimalsVeg
        ///   faction - since prey is meant to be worth nothing, as deer are;
        ///
        ///   which prefabs share a token, since the tally cannot tell them apart and they cannot
        ///   then be given different points, or be counted in one biome and not another;
        ///
        ///   the health of the camp and town creatures the spawn tables never showed.
        /// </summary>
        private static void Creatures(Terminal term)
        {
            var scene = ZNetScene.instance;
            if (scene == null) { Say(term, "utangard creatures: no world loaded yet."); return; }

            // Utangard's own tally, the one the bar pays from. Null when it cannot be read, and each
            // line then says "?" rather than a 0 that would look like a fact.
            var kills = KillTally.Here();
            var bar = Foothold.FullBar();

            var byToken = new Dictionary<string, List<string>>();

            // The live Points_ lines, not the defaults in code, so this checks what the bar pays.
            foreach (var biome in UtangardConfig.GateableBiomes)
            {
                var entries = Foothold.Parse(UtangardConfig.PointsLineFor(biome));
                if (entries.Count == 0) continue;

                Say(term, "utangard creatures: " + biome + ", " + entries.Count + " kinds, full bar " + bar
                          + " points, no kind above " + Foothold.KindCap(bar) + ".");

                foreach (var e in entries)
                    Say(term, "    " + e.Value + " pt  " + Describe(scene, e.Key, kills, byToken, biome + " " + e.Key + "=" + e.Value));
            }

            Say(term, "utangard creatures: left out on purpose -");
            foreach (var name in LeftOut)
                Say(term, "    -     " + Describe(scene, name, kills, byToken, "left out " + name));

            var shared = 0;
            foreach (var pair in byToken)
            {
                if (pair.Value.Count < 2) continue;
                shared++;
                Say(term, "utangard creatures: SHARED TOKEN " + pair.Key + " - " + string.Join("; ", pair.Value.ToArray()));
            }

            Say(term, shared == 0
                ? "utangard creatures: every creature has a token of its own."
                : "utangard creatures: " + shared + " token(s) shared - those creatures cannot be scored apart.");

            // The rule applied, so what the tables actually pay is visible beside what they say.
            var dropped = 0;
            foreach (var entry in Foothold.Resolve(scene))
            {
                if (string.IsNullOrEmpty(entry.Dropped)) continue;
                dropped++;
                Say(term, "utangard creatures: DROPPED " + entry.Biome + " " + entry.Prefab + " - " + entry.Dropped);
            }

            Say(term, dropped == 0
                ? "utangard creatures: after the shared-name rule, every entry in the tables counts."
                : "utangard creatures: after the shared-name rule, " + dropped + " entr(ies) do not count.");
        }

        private static string Describe(ZNetScene scene, string prefabName, Dictionary<string, int> kills,
                                       Dictionary<string, List<string>> byToken, string label)
        {
            var go = scene.GetPrefab(prefabName);
            if (go == null) return prefabName + " - NOT FOUND in ZNetScene";

            Character character;
            if (!go.TryGetComponent(out character)) return prefabName + " - not a creature";

            string brain = "no AI";
            if (go.GetComponent<MonsterAI>() != null) brain = "monster";
            else if (go.GetComponent<AnimalAI>() != null) brain = "ANIMAL";

            var tame = go.GetComponent<Tameable>() != null ? ", tameable" : "";
            var prey = brain == "ANIMAL" || character.m_faction == Character.Faction.AnimalsVeg ? "  <- prey" : "";

            var token = character.m_name ?? "";
            if (!byToken.TryGetValue(token, out var list)) byToken[token] = list = new List<string>();
            list.Add(label);

            var n = 0;
            var counted = kills != null && kills.TryGetValue(token, out n)
                ? n.ToString(CultureInfo.InvariantCulture)
                : kills != null ? "0" : "?";

            return prefabName + " " + token + " " + character.m_health.ToString("0", CultureInfo.InvariantCulture) + "hp "
                   + character.m_faction + " " + brain + tame + ", your kills here " + counted + prey;
        }

        /// <summary>To the console and the log, so a run leaves the numbers on disk.</summary>
        private static void Say(Terminal term, string line)
        {
            term.AddString(line);
            UtangardPlugin.Log.LogInfo(line);
        }
    }
}

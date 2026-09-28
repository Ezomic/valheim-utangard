using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;

namespace Utangard
{
    /// <summary>
    /// Utangard's own kill tally, the one a foothold's Fighting bar reads (LHM-26).
    ///
    /// <b>Why the mod keeps its own.</b> The bar first read the game's lifetime tally,
    /// PlayerProfile.m_playerStats[0].m_enemyStats[0]. That tally lives in the character file, so
    /// it took in kills from every world the character had played, kills from before the mod was
    /// installed, creatures spawned with devcommands, and pets slaughtered at home. Robbin's calls,
    /// 2026-09-27, in this order: tamed kills must not count, and of devcommands and other worlds,
    /// "only kills the mod saw happening". So the mod counts for itself, and everybody's footholds
    /// start from zero on the day this ships.
    ///
    /// <b>Where a kill is seen.</b> Character.OnDeath on the client that owns the creature, which
    /// is the only machine the game itself credits a kill from. It is a prefix, because the ZDO
    /// that says who hit the creature is gone by the time the body finishes: OnDeath ends in
    /// ZNetScene.Destroy, which resets the view's ZDO before anything after it could read it.
    ///
    /// The owner check is repeated here and it is not ceremony. CLAUDE.md used to record OnDeath's
    /// own `!IsOwner()` return as dead code, and it was, until 1.0 gave creatures a death animation:
    /// with Character.m_deathAnimation set, CheckDeath starts a coroutine instead of calling
    /// OnDeath, and OnDeath then comes from CharacterAnimEvent.Die, an animation event that fires
    /// on every client animating the creature. On all of them but the owner the guard is live.
    /// Being a prefix is also what lets the check work at all: the reset that takes the attackers
    /// takes the owner's answer with it, so the same check in a postfix refuses the owner too.
    /// That is what happened to KillCredit's check, found 2026-09-28; this one was never affected.
    ///
    /// <b>Who is credited.</b> Exactly the players the game credits, found the way OnDeath finds
    /// them: every entry in ZNet's player list whose name the creature's ZDO marked as an attacker
    /// (Character.Damage sets `ZDOVars.s_attackers + name` on each player who hits it). So an assist
    /// counts, and the mod never credits somebody the game would not. The owner counts its own
    /// local player directly and sends everybody else a routed RPC carrying the creature's name
    /// token, to the peer the game sends RPC_RegisterKill to. Utangard is Requirement.Everyone, so
    /// every machine has the handler. A player who is offline, or has left, misses the kill.
    ///
    /// <b>What never counts.</b> Checked on the owner, for everybody: a tamed creature, which
    /// covers anything born to tamed parents, since Procreation calls SetTamed on the offspring
    /// the moment it is made; a creature the game has marked as cheated; and one the console's
    /// kill commands wiped out (see WipedByCommand). Checked on the receiving machine, for its own
    /// player only: devcommands in force, god mode, ghost mode, debug flight, or a weapon spawned
    /// with devcommands in hand (see Heard).
    ///
    /// The split is forced by what each machine can see, and it leaves one gap on purpose. The
    /// game's cheat mark is written by Character.ApplyDamage on the owner, from the owner's copy
    /// of the player who hit, and god mode, ghost mode and that player's inventory exist only on
    /// the player's own machine. So the mark catches Terminal's spawn command, which sets it on
    /// everything spawned, and a cheating player hitting a creature their own machine has. It
    /// never sees a god-mode player hitting a creature somebody else's machine has. That player
    /// is refused at home by Heard; a friend who helped them is still credited, because nothing
    /// on the friend's side can tell. Closing that would take a message from the cheater's machine
    /// to the owner on every hit, which is more machinery than one host cheating in his own
    /// hosted game is worth. The README says so.
    ///
    /// <b>Where it is kept.</b> In the character, one entry of Player.m_customData per world, keyed
    /// by the world's UID: a kill in one world never counts in another. m_customData is written by
    /// Player.Save into the character's saved data, which Game saves with the rest of the
    /// character, including on death before respawning, so this has no save of its own. One count
    /// per creature kind, and only for kinds some points line pays for, so an entry is a few
    /// hundred bytes at most.
    ///
    /// <b>A failure costs the relief, never the game.</b> Every path here is inside a try/catch
    /// that ends in "not counted". An unreadable tally reads as an empty Fighting bar, which is
    /// the lock as it was before footholds.
    /// </summary>
    internal static class KillTally
    {
        /// <summary>
        /// The routed RPC. Namespaced, because a routed RPC is keyed on the stable hash of this
        /// string across every mod in the process.
        /// </summary>
        internal const string RpcName = "Utangard_Kill";

        /// <summary>Prefix of the m_customData key, followed by the world's UID.</summary>
        private const string DataPrefix = "utangard.kills.";

        // ------------------------------------------------------------------ the hooks -----

        /// <summary>
        /// The death, on the machine that owns the creature. A prefix: see the class comment for
        /// why the body is too late.
        /// </summary>
        [HarmonyPatch(typeof(Character), "OnDeath")]
        internal static class Witness
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance)
            {
                // Counted for `utangard deaths` whoever has the creature, because which machines
                // reach this line at all is the question that readout exists for (LHM-36).
                DevConsole.SawDeath(__instance);

                Seen(__instance);
            }
        }

        /// <summary>
        /// Registers the handler for kills made on somebody else's machine.
        ///
        /// ZNet.Awake builds a new ZRoutedRpc with an empty function table for every world,
        /// logging out to the menu and back included, so a handler registered once per process
        /// would be gone by the second world. Kvedja and Merki register theirs the same way.
        /// </summary>
        [HarmonyPatch(typeof(ZNet), "Awake")]
        internal static class Listen
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                // A plain class, not a UnityEngine.Object, so an ordinary null check is right.
                if (ZRoutedRpc.instance == null) return;

                try
                {
                    ZRoutedRpc.instance.Register<string>(RpcName, OnKill);
                }
                catch (Exception e)
                {
                    SayFailure("could not listen for kills made on other machines", e);
                }
            }
        }

        // ------------------------------------------------------------- seeing a kill -----

        /// <summary>
        /// A creature dying on this machine. Credits every player the game credits, or nobody
        /// when the kill is one that never counts.
        /// </summary>
        private static void Seen(Character creature)
        {
            if (!On()) return;
            if (creature == null || creature.IsPlayer()) return;

            try
            {
                ZNetView nview;
                if (!creature.TryGetComponent(out nview) || !nview.IsValid() || !nview.IsOwner()) return;

                // ZDO is a plain class, so these are ordinary null checks.
                ZDO zdo = nview.GetZDO();
                ZNet net = ZNet.instance;
                if (zdo == null || net == null) return;

                string token = creature.m_name;
                if (string.IsNullOrEmpty(token) || !Foothold.Pays(token)) return;

                // Both, because the owner's field is what the game answers from and the ZDO is
                // what it saves. A creature tamed on another machine a moment before it changed
                // hands is the case where they could differ, and either one is enough.
                if (creature.IsTamed() || zdo.GetBool(ZDOVars.s_tamed))
                {
                    Note("a tamed " + token + " died, and a tamed kill does not count");
                    return;
                }

                if (zdo.GetBool(ZDOVars.s_cheated))
                {
                    Note("a " + token + " the game marked as cheated died, and a kill made with devcommands does not count");
                    return;
                }

                if (WipedByCommand(creature))
                {
                    Note("a " + token + " was wiped out by killall or killenemies, and a kill made with devcommands does not count");
                    return;
                }

                string localName = LocalName();

                foreach (ZNet.PlayerInfo info in net.GetPlayerList())
                {
                    if (string.IsNullOrEmpty(info.m_name)) continue;

                    // The key Character.Damage writes for each player who hits the creature, built
                    // the way OnDeath builds it: the int hash and the name, as one string.
                    if (!zdo.GetBool(ZDOVars.s_attackers + info.m_name)) continue;

                    // The first test is vanilla's. The second covers the list being behind: ZNet
                    // rebuilds it every two seconds, and a respawn sets this machine's character
                    // to None in between, so for a moment after spawning your own entry can still
                    // say None. Vanilla credits you anyway there, through the broadcast below, so
                    // an entry with no character and your own name is taken to be you.
                    bool local = Player.m_localPlayer != null
                        && (info.m_characterID == net.LocalPlayerCharacterID
                            || (info.m_characterID.IsNone() && localName != null && info.m_name == localName));

                    if (local)
                    {
                        Heard(token);
                        continue;
                    }

                    // Vanilla sends RPC_RegisterKill to this player's UserID even when the player
                    // has no character, and ZDOID.None's UserID is 0, which is ZRoutedRpc.Everybody:
                    // one player between lives credits the whole server. Here that player misses it.
                    if (info.m_characterID.IsNone()) continue;

                    if (ZRoutedRpc.instance != null)
                        ZRoutedRpc.instance.InvokeRoutedRPC(info.m_characterID.UserID, RpcName, token);
                }
            }
            catch (Exception e)
            {
                // Never let a count break a death. This runs in front of the game's own OnDeath,
                // and the drops, the defeat key and the despawn are all behind it.
                SayFailure("could not count a kill", e);
            }
        }

        /// <summary>
        /// A kill made on another machine that credits this one. Any client can aim a routed RPC at
        /// any other, and nothing here can prove the sender saw a death - like the rest of the mod,
        /// this is a rule for a group running the same plugins, not an anti-cheat.
        /// </summary>
        private static void OnKill(long sender, string token)
        {
            try
            {
                Heard(token);
            }
            catch (Exception e)
            {
                // Never let this out: it runs inside ZRoutedRpc's dispatch, in the middle of that
                // connection's other messages, and a kill count is not worth a broken connection.
                SayFailure("could not count a kill made on another machine", e);
            }
        }

        /// <summary>
        /// The lethal blow came from the console's kill commands. killall, killenemies and
        /// killenemycreatures hit every creature near whoever typed them with new HitData(1E+10f):
        /// no attacker and ten billion damage. With no attacker, Character.ApplyDamage never
        /// writes the cheat mark, so the creature dies clean and OnDeath credits everybody who
        /// had hit it before. Only a host can run those commands, and the friend it would credit
        /// may well have devcommands off, so the receiving side cannot catch it. Only this can.
        ///
        /// m_lastHit is the lethal hit, because ApplyDamage stores a hit only while the creature
        /// still has health before it. Nothing in play comes near a billion: the edge of the world
        /// hits for 99,999 and a creature's attack that kills itself for 9,999,999, and the most
        /// the game's scaling takes off ten billion is a factor of a few.
        ///
        /// Bound lazily, and a failed binding costs this one check rather than every kill. The
        /// rule elsewhere is that a failure keeps the lock as it was, but here that would mean no
        /// Fighting bar for anybody until an update, over a command only a host can type.
        /// </summary>
        private static bool WipedByCommand(Character creature)
        {
            AccessTools.FieldRef<Character, HitData> of = LastHitOf();
            if (of == null) return false;

            // HitData is a plain class, so an ordinary null check is right.
            HitData hit = of(creature);
            return hit != null && !hit.HaveAttacker() && hit.m_damage.m_damage >= 1E+09f;
        }

        private static AccessTools.FieldRef<Character, HitData> _lastHitOf;
        private static bool _lastHitBound;

        /// <summary>Character.m_lastHit, protected, bound lazily - see Reflect.</summary>
        private static AccessTools.FieldRef<Character, HitData> LastHitOf()
        {
            if (_lastHitBound) return _lastHitOf;
            _lastHitBound = true;

            _lastHitOf = Reflect.Field<Character, HitData>(
                "m_lastHit", "refusing foothold kills made with killall");

            return _lastHitOf;
        }

        /// <summary>
        /// This character's name as ZNet puts it in the player list: UpdatePlayerList takes the
        /// host's own entry from the profile, and a client's peer name comes from the same place.
        /// Null when there is no game.
        /// </summary>
        private static string LocalName()
        {
            Game game = Game.instance;
            if (game == null) return null;

            // PlayerProfile is a plain class.
            PlayerProfile profile = game.GetPlayerProfile();
            return profile != null ? profile.GetName() : null;
        }

        /// <summary>One kill credited to the local character, if it counts.</summary>
        private static void Heard(string token)
        {
            if (!On() || string.IsNullOrEmpty(token)) return;

            if (CheatsOn())
            {
                Note("your " + token + " kill does not count, because devcommands are on");
                return;
            }

            if (CheatingNow())
            {
                Note("your " + token + " kill does not count, because you are in god mode, ghost mode or "
                     + "debug flight, or carry a weapon spawned with devcommands");
                return;
            }

            if (!Foothold.Pays(token)) return;

            int had;
            if (!Change(token, n => n + 1, out had))
            {
                Note("your " + token + " kill could not be counted: there is no character in a world to file it under");
                return;
            }

            Note("counted your " + token + " kill, now " + (had + 1) + " in this world");
        }

        /// <summary>
        /// Devcommands in force on this machine. Terminal.IsCheatsEnabled is the test a cheat
        /// command must pass before it runs, so it is also what decides whether the game's own
        /// cheat mark can be written. It reads Terminal.m_cheat and whether this machine is the
        /// server, which is why a client's devcommands on a dedicated server never count as on:
        /// the game ignores them there too.
        ///
        /// Asked of the console when there is one. A client always has one; without it the flag
        /// alone answers, which can only be stricter.
        /// </summary>
        private static bool CheatsOn()
        {
            global::Console console = global::Console.instance;
            if (console != null) return console.IsCheatsEnabled();

            return Terminal.m_cheat;
        }

        /// <summary>
        /// The local character is doing one of the four things Character.ApplyDamage treats as
        /// cheating: god mode, ghost mode, debug flight, or a damaging item spawned with
        /// devcommands in hand.
        ///
        /// Asked here as well as through the owner's cheat mark, because only this machine can
        /// answer for three of them. InGodMode and InGhostMode return plain fields that nothing
        /// sends anywhere, and a player's inventory is never loaded on another machine, so the
        /// owner's copy of you says no to all three whatever you are doing. Only debug flight
        /// reaches the owner, through your ZDO. CheatsOn does not cover them either: `devcommands`
        /// flips Terminal.m_cheat and nothing else, so god mode stays on after devcommands go off.
        /// </summary>
        private static bool CheatingNow()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return false;

            if (player.InGodMode() || player.InGhostMode() || player.IsDebugFlying()) return true;

            // Inventory is a plain class.
            Inventory inventory = player.GetInventory();
            return inventory != null && inventory.CheatedDamagingItemEquipped();
        }

        private static bool On()
        {
            return UtangardConfig.Enabled.Value && UtangardConfig.FootholdEnabled.Value;
        }

        // ------------------------------------------------------------------ the store -----

        /// <summary>The counts below belong to this character and this key.</summary>
        private static Player _player;
        private static string _key;

        /// <summary>
        /// The m_customData value the counts were parsed from, by reference. A different string
        /// there means somebody wrote it since, so it is read again rather than trusted.
        /// </summary>
        private static string _raw;

        private static bool _loaded;

        private static long _keyWorld;
        private static string _keyCached;

        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

        /// <summary>
        /// The local character's counts in this world, for the Fighting bar. Null when kills are
        /// not being counted this session or there is nowhere to read them from, which every
        /// caller reads as an empty bar.
        ///
        /// Do not write to the dictionary; go through Change or Set, which save.
        /// </summary>
        internal static Dictionary<string, int> Here()
        {
            if (!Seams.FootholdKills) return null;

            try
            {
                return Load();
            }
            catch (Exception e)
            {
                SayFailure("could not read the kill tally", e);
                return null;
            }
        }

        /// <summary>
        /// Why Here() answers null, in a line for the console, or null when it does not.
        /// </summary>
        internal static string WhyNot()
        {
            if (!Seams.FootholdKills) return "the kill hook did not go on this session, so no kill is being counted";
            if (Player.m_localPlayer == null) return "there is no character in a world yet";
            if (Key() == null) return "there is no world to file kills under yet";
            return null;
        }

        /// <summary>
        /// Set one creature's count in this world, for tests: `utangardtest kills`. False when
        /// there is no character or no world.
        /// </summary>
        internal static bool Set(string token, int count, out int had)
        {
            return Change(token, n => Math.Max(0, count), out had);
        }

        private static bool Change(string token, Func<int, int> to, out int had)
        {
            had = 0;

            Dictionary<string, int> counts = Load();
            if (counts == null) return false;

            counts.TryGetValue(token, out had);

            int now = to(had);
            if (now <= 0) counts.Remove(token);
            else counts[token] = now;

            Save();
            return true;
        }

        /// <summary>
        /// The parsed counts, read again only when the character, the world or the stored string
        /// has changed. A new Player object arrives with every respawn and every login, and each
        /// one carries the m_customData the game loaded for it.
        /// </summary>
        private static Dictionary<string, int> Load()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.m_customData == null) return null;

            string key = Key();
            if (key == null) return null;

            string raw;
            player.m_customData.TryGetValue(key, out raw);

            if (_loaded && ReferenceEquals(player, _player) && key == _key && ReferenceEquals(raw, _raw))
                return Counts;

            Counts.Clear();
            Parse(raw, Counts);

            _player = player;
            _key = key;
            _raw = raw;
            _loaded = true;
            return Counts;
        }

        /// <summary>
        /// Writes the counts back into the character. Only on a change, and a change is a kill,
        /// so this happens a few times a minute at most.
        /// </summary>
        private static void Save()
        {
            if (_player == null || _key == null) return;

            if (Counts.Count == 0)
            {
                _player.m_customData.Remove(_key);
                _raw = null;
                return;
            }

            var text = new StringBuilder(Counts.Count * 24);
            foreach (KeyValuePair<string, int> pair in Counts)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            _raw = text.ToString();
            _player.m_customData[_key] = _raw;
        }

        /// <summary>
        /// "token=count" lines. Split at the last '=' so a creature whose name happens to carry
        /// one still reads back; a line that does not parse is skipped rather than thrown.
        /// </summary>
        private static void Parse(string raw, Dictionary<string, int> into)
        {
            if (string.IsNullOrEmpty(raw)) return;

            foreach (string line in raw.Split('\n'))
            {
                int split = line.LastIndexOf('=');
                if (split <= 0) continue;

                int count;
                if (!int.TryParse(line.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) continue;
                if (count <= 0) continue;

                into[line.Substring(0, split)] = count;
            }
        }

        /// <summary>
        /// The m_customData key for this world, or null before there is one. ZNet.GetWorldUID
        /// dereferences a world a client only has once the server's peer info has arrived, so it
        /// is asked behind GetWorldName, which checks.
        /// </summary>
        private static string Key()
        {
            ZNet net = ZNet.instance;
            if (net == null || net.GetWorldName() == null) return null;

            long world = net.GetWorldUID();
            if (_keyCached == null || world != _keyWorld)
            {
                _keyWorld = world;
                _keyCached = DataPrefix + world.ToString(CultureInfo.InvariantCulture);
            }

            return _keyCached;
        }

        // --------------------------------------------------------------------- logging ----

        private static void Note(string line)
        {
            if (UtangardConfig.Verbose.Value) UtangardPlugin.Log.LogInfo("Foothold: " + line + ".");
        }

        private static bool _saidFailure;

        /// <summary>
        /// Once per session. A death happens every few seconds in a fight, and one line is all a
        /// bug report needs.
        /// </summary>
        private static void SayFailure(string what, Exception e)
        {
            if (_saidFailure) return;
            _saidFailure = true;

            UtangardPlugin.Log.LogError("Utangard " + what + ", so the Fighting bar of a foothold "
                + "may stay short and eating and healing stay locked as they were before footholds. " + e);
        }
    }
}

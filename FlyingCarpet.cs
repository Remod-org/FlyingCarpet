//#define DEBUG
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProtoBuf;
using Rust;
using System.Linq;
using Network;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Globalization;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("FlyingCarpet", "RFC1920", "1.1.6")]
    [Description("Fly a custom object consisting of carpet, chair, lantern, lock, and small sign.")]
    // Thanks to Colon Blow for his fine work on GyroCopter, upon which this was originally based.
    class FlyingCarpet : RustPlugin
    {
        #region Load
        static LayerMask layerMask;
        BaseEntity newCarpet;

        static Dictionary<ulong, PlayerCarpetData> loadplayer = new Dictionary<ulong, PlayerCarpetData>();
        static List<ulong> pilotslist = new List<ulong>();

        // SignArtist plugin
        [PluginReference]
        Plugin SignArtist;

        public class PlayerCarpetData
        {
            public BasePlayer player;
            public int carpetcount;
        }

        void Init()
        {
            LoadVariables();
            layerMask = (1 << 29);
            layerMask |= (1 << 18);
            layerMask = ~layerMask;

            AddCovalenceCommand("fc", "cmdCarpetBuild");
            AddCovalenceCommand("fcc", "cmdCarpetCount");
            AddCovalenceCommand("fcd", "cmdCarpetDestroy");
            AddCovalenceCommand("fcg", "cmdCarpetGiveChat");
            AddCovalenceCommand("fchelp", "cmdCarpetHelp");

            permission.RegisterPermission("flyingcarpet.use", this);
            permission.RegisterPermission("flyingcarpet.vip", this);
            permission.RegisterPermission("flyingcarpet.admin", this);
            permission.RegisterPermission("flyingcarpet.unlimited", this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["helptext1"] = "Flying Carpet instructions:",
                ["helptext2"] = "  type /fc to spawn a Flying Carpet",
                ["helptext3"] = "  type /fcd to destroy your flyingcarpet.",
                ["helptext4"] = "  type /fcc to show a count of your carpets",
                ["notauthorized"] = "You don't have permission to do that !!",
                ["notfound"] = "Could not locate a carpet.  You must be within {0} meters for this!!",
                ["notflyingcarpet"] = "You are not piloting a flying carpet !!",
                ["maxcarpets"] = "You have reached the maximum allowed carpets",
                ["landingcarpet"] = "Carpet landing sequence started !!",
                ["risingcarpet"] = "Carpet takeoff sequence started !!",
                ["carpetlocked"] = "You must unlock the Carpet first !!",
                ["carpetspawned"] = "Flying Carpet spawned!  Don't forget to lock it !!",
                ["carpetdestroyed"] = "Flying Carpet destroyed !!",
                ["carpetfuel"] = "You will need fuel to fly.  Do not start without fuel !!",
                ["carpetnofuel"] = "You have been granted unlimited fly time, no fuel required !!",
                ["nostartseat"] = "You cannot light this lantern until seated !!",
                ["nofuel"] = "You're out of fuel !!",
                ["noplayer"] = "Unable to find player {0}!",
                ["gaveplayer"] = "Gave carpet to player {0}!",
                ["lowfuel"] = "You're low on fuel !!",
                ["nocarpets"] = "You have no Carpets",
                ["currcarpets"] = "Current Carpets : {0}",
                ["giveusage"] = "You need to supply a valid SteamId."
            }, this);
        }

        bool isAllowed(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        private bool HasPermission(ConsoleSystem.Arg arg, string permname) => (arg.Connection.player as BasePlayer) == null ? true : permission.UserHasPermission((arg.Connection.player as BasePlayer).UserIDString, permname);

        private static HashSet<BasePlayer> FindPlayers(string nameOrIdOrIp)
        {
            var players = new HashSet<BasePlayer>();
            if (string.IsNullOrEmpty(nameOrIdOrIp)) return players;
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.UserIDString.Equals(nameOrIdOrIp))
                    players.Add(activePlayer);
                else if (!string.IsNullOrEmpty(activePlayer.displayName) && activePlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                    players.Add(activePlayer);
                else if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress.Equals(nameOrIdOrIp))
                    players.Add(activePlayer);
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (sleepingPlayer.UserIDString.Equals(nameOrIdOrIp))
                    players.Add(sleepingPlayer);
                else if (!string.IsNullOrEmpty(sleepingPlayer.displayName) && sleepingPlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                    players.Add(sleepingPlayer);
            }
            return players;
        }
        #endregion

        #region Configuration
        bool UseMaxCarpetChecks = true;
        bool playemptysound = true;
        public int maxcarpets = 1;
        public int vipmaxcarpets = 2;

        static float MinAltitude = 5f;
        static float MinDistance = 10f;

        static ulong rugSkinID = 871503616;
        static ulong chairSkinID = 943293895;

        static float NormalSpeed = 12f;
        static float SprintSpeed = 25f;
        static bool requirefuel = true;
        static bool doublefuel = false;
        static bool nameOnSign = true;
        static bool allowLantern = false;
        static bool allowRepaint = false;

        //bool Changed = false;

        protected override void LoadDefaultConfig()
        {
#if DEBUG
            Puts("Creating a new config file...");
#endif
            Config.Clear();
            LoadVariables();
        }

        private void LoadConfigVariables()
        {
            CheckCfgFloat("Minimum Flight Altitude : ", ref MinAltitude);
            CheckCfgFloat("Minimum Distance for FCD: ", ref MinDistance);
            CheckCfgFloat("Speed - Normal Flight Speed is : ", ref NormalSpeed);
            CheckCfgFloat("Speed - Sprint Flight Speed is : ", ref SprintSpeed);

            CheckCfg("Deploy - Enable limited FlyingCarpets per person : ", ref UseMaxCarpetChecks);
            CheckCfg("Deploy - Limit of Carpets players can build : ", ref maxcarpets);
            CheckCfg("Deploy - Limit of Carpets VIP players can build : ", ref vipmaxcarpets);
            CheckCfg("Require Fuel to Operate : ", ref requirefuel);
            CheckCfg("Play low fuel sound : ", ref playemptysound);
            CheckCfg("Double Fuel Consumption: ", ref doublefuel);
            CheckCfg("RugSkinID : ", ref rugSkinID);
            CheckCfg("ChairSkinID : ", ref chairSkinID);
            CheckCfg("Owner Name on Sign: ", ref nameOnSign);
            CheckCfg("Allow repainting of sign: ", ref allowRepaint);
            CheckCfg("Allow lantern use while not seated: ", ref allowLantern);
        }

        private void LoadVariables()
        {
            LoadConfigVariables();
            SaveConfig();
        }

        private void CheckCfg<T>(string Key, ref T var)
        {
            if(Config[Key] is T)
            {
                var = (T)Config[Key];
            }
            else
            {
                Config[Key] = var;
            }
        }

        private void CheckCfgFloat(string Key, ref float var)
        {
            if(Config[Key] != null)
            {
                var = Convert.ToSingle(Config[Key]);
            }
            else
            {
                Config[Key] = var;
            }
        }

        object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if(data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                //Changed = true;
            }

            object value;
            if(!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                //Changed = true;
            }
            return value;
        }
        #endregion

        #region Message
        private string _(string msgId, BasePlayer player, params object[] args)
        {
            var msg = lang.GetMessage(msgId, this, player?.UserIDString);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        private void PrintMsgL(BasePlayer player, string msgId, params object[] args)
        {
            if(player == null) return;
            PrintMsg(player, _(msgId, player, args));
        }

        private void PrintMsg(BasePlayer player, string msg)
        {
            if(player == null) return;
            SendReply(player, $"{msg}");
        }
        #endregion

        #region Chat Commands
        [Command("fc"), Permission("flyingcarpet.use")]
        void cmdCarpetBuild(IPlayer iplayer, string command, string[] args)
        {
            bool vip = false;
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingcarpet.use")) { PrintMsgL(player, "notauthorized"); return; }
            if(iplayer.HasPermission("flyingcarpet.vip"))
            {
                vip = true;
            }
            if(CarpetLimitReached(player, vip)) { PrintMsgL(player, "maxcarpets"); return; }
            AddCarpet(player, player.transform.position);
        }

        [Command("fcg"), Permission("flyingcarpet.admin")]
        void cmdCarpetGiveChat(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(args.Length == 0)
            {
                PrintMsgL(player, "giveusage");
                return;
            }
            bool vip = false;
            string pname = args[0] == null ? null : args[0];

            if(!iplayer.HasPermission("flyingcarpet.admin")) { PrintMsgL(player, "notauthorized"); return; }
            if(pname == null) { PrintMsgL(player, "noplayer", "NAME_OR_ID"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if(Bplayer == null)
            {
                PrintMsgL(player, "noplayer", pname);
                return;
            }

            var Iplayer = Bplayer.IPlayer;
            if(Iplayer.HasPermission("flyingcarpet.vip"))
            {
                vip = true;
            }
            if(CarpetLimitReached(Bplayer, vip)) { PrintMsgL(player, "maxcarpets"); return; }
            AddCarpet(Bplayer, Bplayer.transform.position);
            PrintMsgL(player, "gaveplayer", pname);
        }

        [ConsoleCommand("fcgive")]
        void cmdCarpetGive(ConsoleSystem.Arg arg)
        {
            if(arg.IsRcon)
            {
                if(arg.Args == null)
                {
                    Puts("You need to supply a valid SteamId.");
                    return;
                }
            }
            else if(!HasPermission(arg, "flyingcarpet.admin"))
            {
                SendReply(arg, _("notauthorized", arg.Connection.player as BasePlayer));
                return;
            }
            else if(arg.Args == null)
            {
                SendReply(arg, _("giveusage", arg.Connection.player as BasePlayer));
                return;
            }

            bool vip = false;
            string pname = arg.GetString(0);

            if(pname.Length < 1) { Puts("Player name or id cannot be null"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if(Bplayer == null) { Puts($"Unable to find player '{pname}'"); return; }

            var Iplayer = Bplayer.IPlayer;
            if(Iplayer.HasPermission("flyingcarpet.vip")) { vip = true; }
            if(CarpetLimitReached(Bplayer, vip))
            {
                Puts($"Player '{pname}' has reached maxcarpets"); return;
            }
            AddCarpet(Bplayer, Bplayer.transform.position);
            Puts($"Gave carpet to '{Bplayer.displayName}'");
        }

        [Command("fcc"), Permission("flyingcarpet.use")]
        void cmdCarpetCount(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingcarpet.use")) { PrintMsgL(player, "notauthorized"); return; }
            if(!loadplayer.ContainsKey(player.userID))
            {
                PrintMsgL(player, "nocarpets");
                return;
            }
            string ccount = loadplayer[player.userID].carpetcount.ToString();
#if DEBUG
            Puts("CarpetCount: " + ccount);
#endif
            PrintMsgL(player, "currcarpets", ccount);
        }

        [Command("fcd"), Permission("flyingcarpet.use")]
        void cmdCarpetDestroy(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingcarpet.use")) { PrintMsgL(player, "notauthorized"); return; }

            string target = null;
            if(args.Length > 0)
            {
                target = args[0];
            }
            if(iplayer.HasPermission("flyingcarpet.admin") && target != null)
            {
                if(target == "all")
                {
                    DestroyAllCarpets(player);
                    return;
                }
                var players = FindPlayers(target);
                if (players.Count <= 0)
                {
                    PrintMsgL(player, "PlayerNotFound", target);
                    return;
                }
                if (players.Count > 1)
                {
                    PrintMsgL(player, "MultiplePlayers", target, string.Join(", ", players.Select(p => p.displayName).ToArray()));
                    return;
                }
                var targetPlayer = players.First();
                RemoveCarpet(targetPlayer);
                DestroyRemoteCarpet(targetPlayer);
            }
            else
            {
                RemoveCarpet(player);
                DestroyLocalCarpet(player);
            }
        }

        [Command("fchelp"), Permission("flyingcarpet.use")]
        void cmdCarpetHelp(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("flyingcarpet.use")) { PrintMsgL(player, "notauthorized"); return; }
            PrintMsgL(player, "helptext1");
            PrintMsgL(player, "helptext2");
            PrintMsgL(player, "helptext3");
            PrintMsgL(player, "helptext4");
        }
        #endregion

        #region Hooks
        // This is how we take off or land the carpet!
        object OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            bool rtrn = false; // Must match other plugins with this call to avoid conflicts. QuickSmelt uses false

            CarpetEntity activecarpet;

            try
            {
                activecarpet = player.GetMounted().GetComponentInParent<CarpetEntity>() ?? null;
                if(activecarpet == null) return null;
            }
            catch
            {
                if(oven.GetComponentInParent<CarpetEntity>() && !allowLantern)
                {
#if DEBUG
                    Puts("No player mounted on this carpet.");
#endif
                    PrintMsgL(player, "nostartseat"); return rtrn;
                }
#if DEBUG
                Puts("No carpet or not mounted.");
#endif
                return null;
            }

            if(activecarpet.carpetlock != null && activecarpet.carpetlock.IsLocked()) { PrintMsgL(player, "carpetlocked"); return rtrn; }
            if(!player.isMounted) return rtrn; // player offline, does not mean ismounted on carpet

            if(player.GetMounted() != activecarpet.entity)
            {
#if DEBUG
                Puts("OnOvenToggle: Player not mounted on carpet!");
#endif
                oven.StopCooking();
                return rtrn;
            }
#if DEBUG
            Puts("OnOvenToggle: Player cycled lantern!");
#endif
            if(oven.IsOn())
            {
                oven.StopCooking();
            }
            else
            {
                oven.StartCooking();
            }
            if(!activecarpet.FuelCheck())
            {
                if(activecarpet.needfuel)
                {
                    PrintMsgL(player, "nofuel");
                    PrintMsgL(player, "landingcarpet");
                    activecarpet.engineon = false;
                }
            }
            var ison = activecarpet.engineon;
            if(ison) { activecarpet.islanding = true; PrintMsgL(player, "landingcarpet"); return null; }
            if(!ison) { AddPlayerToPilotsList(player); activecarpet.engineon = true; return null; }

            return rtrn;
        }

        // Check for carpet lantern fuel
        void OnConsumeFuel(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            // Only work on lanterns
            if(oven.ShortPrefabName != "lantern.deployed") return;
            int dbl = doublefuel ? 4 : 2;

            BaseEntity lantern = oven as BaseEntity;
            // Only work on lanterns attached to a Carpet
            var activecarpet = lantern.GetComponentInParent<CarpetEntity>() ?? null;
            if(activecarpet == null) return;
#if DEBUG
            Puts("OnConsumeFuel: found a carpet lantern!");
#endif
            if(activecarpet.needfuel)
            {
#if DEBUG
                Puts("OnConsumeFuel: carpet requires fuel!");
#endif
            }
            else
            {
#if DEBUG
                Puts("OnConsumeFuel: carpet does not require fuel!");
#endif
                fuel.amount++; // Required to keep it from decrementing
                return;
            }
            BasePlayer player = activecarpet.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
            if(!player) return;
#if DEBUG
            Puts("OnConsumeFuel: checking fuel level...");
#endif
            // Before it drops to 1 (3 for doublefuel) AFTER this hook call is complete, warn them that the fuel is low (1) - ikr
            if(fuel.amount == dbl)
            {
#if DEBUG
                Puts("OnConsumeFuel: sending low fuel warning...");
#endif
                if(playemptysound)
                {
                    Effect.server.Run("assets/bundled/prefabs/fx/well/pump_down.prefab", player.transform.position);
                }
                PrintMsgL(player, "lowfuel");
            }

            if(doublefuel)
            {
                fuel.amount--;
            }

            if(fuel.amount == 0)
            {
#if DEBUG
                Puts("OnConsumeFuel: out of fuel.");
#endif
                PrintMsgL(player, "lowfuel");
                var ison = activecarpet.engineon;
                if(ison)
                {
                    activecarpet.islanding = true;
                    activecarpet.engineon = false;
                    PrintMsgL(player, "landingcarpet");
                    OnOvenToggle(oven, player);
                    return;
                }
            }
        }

        // To skip cycling our lantern (thanks, k11l0u)
        private object OnNightLanternToggle(BaseEntity entity, bool status)
        {
            // Only work on lanterns
            if(entity.ShortPrefabName != "lantern.deployed") return null;
#if DEBUG
            Puts("OnNightLanternToggle: Called on a lantern.  Checking for carpet...");
#endif

            // Only work on lanterns attached to a Carpet
            var activecarpet = entity.GetComponentInParent<CarpetEntity>() ?? null;
            if(activecarpet != null)
            {
#if DEBUG
                Puts("OnNightLanternToggle: Do not cycle this lantern!");
#endif
                return true;
            }
#if DEBUG
            Puts("OnNightLanternToggle: Not a carpet lantern.");
#endif
            return null;
        }
        #endregion

        #region Primary
        private void AddCarpet(BasePlayer player, Vector3 location)
        {
            if(player == null && location == null) return;
            if(location == null && player != null) location = player.transform.position;
            Vector3 spawnpos = new Vector3();

            // Set initial default for fuel requirement based on config
            bool needfuel = requirefuel;
            if(isAllowed(player, "flyingcarpet.unlimited"))
            {
                // User granted unlimited fly time without fuel
                needfuel = false;
#if DEBUG
                Puts("AddCarpet: Unlimited fuel granted!");
#endif
            }

            if(needfuel)
            {
                // Don't put them on the carpet since they need to fuel up first
                spawnpos = player.transform.position + -player.transform.forward * 2f + new Vector3(0, 1f, 0);
            }
            else
            {
                // Spawn at point of player
                spawnpos = new Vector3(location.x, location.y + 0.5f, location.z);
            }

            string staticprefab = "assets/bundled/prefabs/static/chair.static.prefab";
            newCarpet = GameManager.server.CreateEntity(staticprefab, spawnpos, new Quaternion(), true);
            newCarpet.name = "FlyingCarpet";
            var chairmount = newCarpet.GetComponent<BaseMountable>();
            chairmount.isMobile = true;
            newCarpet.enableSaving = false;
            newCarpet.OwnerID = player.userID;
            newCarpet.skinID = chairSkinID;
            newCarpet.Spawn();
            var carpet = newCarpet.gameObject.AddComponent<CarpetEntity>();
            carpet.needfuel = needfuel;
            // Unlock the tank if they need fuel.
            carpet.lantern1.SetFlag(BaseEntity.Flags.Locked, !needfuel);
            if(needfuel)
            {
#if DEBUG
                // We have to set this after the spawn.
                Puts("AddCarpet: Emptying the tank!");
#endif
                carpet.SetFuel(0);
            }

            // paint sign with username
            if(SignArtist && nameOnSign)
            {
                // This only works with version 1.1.7 on
                if(SignArtist.Version >= new VersionNumber(1, 1, 7))
                {
                    string message = player.displayName;
                    int fontsize = Convert.ToInt32(Math.Floor(150f / message.Length));
                    SignArtist.Call("signText", player, carpet.sign, message, fontsize, "00FF00", "000000");
                }
            }
            if(!allowRepaint)
            {
                carpet.sign.SetFlag(BaseEntity.Flags.Busy, true);
            }
            carpet.sign.SetFlag(BaseEntity.Flags.Locked, true);

            AddPlayerID(player.userID);

            if(chairmount != null && player != null)
            {
                PrintMsgL(player, "carpetspawned");
                if(carpet.needfuel)
                {
                    PrintMsgL(player, "carpetfuel");
                }
                else
                {
                    // Put them in the chair.  They will still need to unlock it.
                    PrintMsgL(player, "carpetnofuel");
                    chairmount.MountPlayer(player);
                }
                return;
            }
        }

        public bool PilotListContainsPlayer(BasePlayer player)
        {
            if(pilotslist.Contains(player.userID)) return true;
            return false;
        }

        void AddPlayerToPilotsList(BasePlayer player)
        {
            if(PilotListContainsPlayer(player)) return;
            pilotslist.Add(player.userID);
        }

        public void RemovePlayerFromPilotsList(BasePlayer player)
        {
            if(PilotListContainsPlayer(player))
            {
                pilotslist.Remove(player.userID);
                return;
            }
        }

        void DestroyLocalCarpet(BasePlayer player)
        {
            if(player == null) return;
            List<BaseEntity> carpetlist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(player.transform.position, MinDistance, carpetlist);
            bool foundcarpet = false;

            foreach(BaseEntity p in carpetlist)
            {
                var foundent = p.GetComponentInParent<CarpetEntity>() ?? null;
                if(foundent != null)
                {
                    foundcarpet = true;
                    if(foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "carpetdestroyed");
                }
            }
            if(!foundcarpet)
            {
                PrintMsgL(player, "notfound", MinDistance.ToString());
            }
        }

        void DestroyAllCarpets(BasePlayer player)
        {
            List<BaseEntity> carpetlist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(new Vector3(0,0,0), 3500f, carpetlist);
            bool foundcarpet = false;

            foreach(BaseEntity p in carpetlist)
            {
                var foundent = p.GetComponentInParent<CarpetEntity>() ?? null;
                if(foundent != null)
                {
                    foundcarpet = true;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "carpetdestroyed");
                }
            }
            if(!foundcarpet)
            {
                PrintMsgL(player, "notfound", MinDistance.ToString());
            }
        }

        void DestroyRemoteCarpet(BasePlayer player)
        {
            if(player == null) return;
            List<BaseEntity> carpetlist = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(new Vector3(0,0,0), 3500f, carpetlist);
            bool foundcarpet = false;

            foreach(BaseEntity p in carpetlist)
            {
                var foundent = p.GetComponentInParent<CarpetEntity>() ?? null;
                if(foundent != null)
                {
                    foundcarpet = true;
                    if(foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    PrintMsgL(player, "carpetdestroyed");
                }
            }
            if(!foundcarpet)
            {
                PrintMsgL(player, "notfound", MinDistance.ToString());
            }
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if(player == null || input == null) return;
            if(!player.isMounted) return;
            var activecarpet = player.GetMounted().GetComponentInParent<CarpetEntity>() ?? null;
            if(activecarpet == null) return;
            if(player.GetMounted() != activecarpet.entity) return;
            if(input != null)
            {
                activecarpet.CarpetInput(input, player);
            }
            return;
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if(entity == null || hitInfo == null) return;
            var iscarpet = entity.GetComponentInParent<CarpetEntity>() ?? null;
            if(iscarpet != null) hitInfo.damageTypes.ScaleAll(0);
            return;
        }

        object OnEntityGroundMissing(BaseEntity entity)
        {
            var iscarpet = entity.GetComponentInParent<CarpetEntity>() ?? null;
            if(iscarpet != null) return false;
            return null;
        }

        bool CarpetLimitReached(BasePlayer player, bool vip=false)
        {
            if(UseMaxCarpetChecks)
            {
                if(loadplayer.ContainsKey(player.userID))
                {
                    var currentcount = loadplayer[player.userID].carpetcount;
                    int maxallowed = maxcarpets;
                    if(vip)
                    {
                        maxallowed = vipmaxcarpets;
                    }
                    if(currentcount >= maxallowed) return true;
                }
            }
            return false;
        }

        object CanDismountEntity(BasePlayer player, BaseMountable entity)
        {
            if(player == null) return null;
            if(PilotListContainsPlayer(player)) return false;
            return null;
        }

        void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            var activecarpet = mountable.GetComponentInParent<CarpetEntity>() ?? null;
            if(activecarpet != null)
            {
#if DEBUG
                Puts("OnEntityMounted: player mounted copter!");
#endif
                if(mountable.GetComponent<BaseEntity>() != activecarpet.entity) return;
                activecarpet.lantern1.SetFlag(BaseEntity.Flags.On, false);
            }
        }

        void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            var activecarpet = mountable.GetComponentInParent<CarpetEntity>() ?? null;
            if(activecarpet != null)
            {
#if DEBUG
                Puts("OnEntityMounted: player dismounted copter!");
#endif
                if(mountable.GetComponent<BaseEntity>() != activecarpet.entity) return;
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if(container == null || player == null) return null;
            var iscarpet = container.GetComponentInParent<CarpetEntity>() ?? null;
            if(iscarpet != null)
            {
                if(iscarpet.carpetlock != null && iscarpet.carpetlock.IsLocked()) return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if(entity == null || player == null) return null;

            BaseEntity myent = entity as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if(myparent == "FlyingCarpet" || myent.name == "FlyingCarpet")
            {
#if DEBUG
                if(myent.name == "FlyingCarpet")
                {
                    Puts("CanPickupEntity: player trying to pickup the carpet!");
                }
                else if(myparent == "FlyingCarpet")
                {
                    string entity_name = myent.LookupPrefab().name;
                    Puts($"CanPickupEntity: player trying to remove {entity_name} from a carpet!");
                }
#endif
                PrintMsgL(player, "notauthorized");
                return false;
            }
            return null;
        }

        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if(baseLock == null || player == null) return null;

            BaseEntity myent = baseLock as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if(myparent == "FlyingCarpet")
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a carpet!");
#endif
                PrintMsgL(player, "notauthorized");
                return false;
            }
            return null;
        }

        void AddPlayerID(ulong ownerid)
        {
            if(!loadplayer.ContainsKey(ownerid))
            {
                loadplayer.Add(ownerid, new PlayerCarpetData
                {
                    carpetcount = 1,
                });
                return;
            }
            loadplayer[ownerid].carpetcount = loadplayer[ownerid].carpetcount + 1;
        }

        void RemovePlayerID(ulong ownerid)
        {
            if(loadplayer.ContainsKey(ownerid)) loadplayer[ownerid].carpetcount = loadplayer[ownerid].carpetcount - 1;
            return;
        }

        object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            RemovePlayerFromPilotsList(player);
            return null;
        }

        void RemoveCarpet(BasePlayer player)
        {
            RemovePlayerFromPilotsList(player);
            return;
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            RemoveCarpet(player);
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            RemoveCarpet(player);
        }

        void DestroyAll<T>()
        {
            var objects = GameObject.FindObjectsOfType(typeof(T));
            if(objects != null)
            {
                foreach(var gameObj in objects)
                {
                    GameObject.Destroy(gameObj);
                }
            }
        }

        void Unload()
        {
            DestroyAll<CarpetEntity>();
        }
        #endregion

        #region Carpet Antihack check
        static List<BasePlayer> carpetantihack = new List<BasePlayer>();

        object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if(player == null) return null;
            if(carpetantihack.Contains(player)) return false;
            return null;
        }
        #endregion

        #region Carpet Entity
        class CarpetEntity : BaseEntity
        {
            public BaseEntity entity;
            public BasePlayer player;
            public BaseEntity sitbox;
            public BaseEntity carpet1;
            public BaseEntity carpet2;
            public BaseEntity lantern1;
            public BaseEntity carpetlock;
            public BaseEntity lights1;
            public BaseEntity lights2;
            public BaseEntity sign;
            public BaseEntity box;

            public string entname = "FlyingCarpet";

            Quaternion entityrot;
            Vector3 entitypos;

            public bool moveforward;
            public bool movebackward;
            public bool moveup;
            public bool movedown;
            public bool rotright;
            public bool rotleft;
            public bool sprinting;
            public bool islanding;
            public bool mounted;

            public bool engineon;
            public bool hasFuel;
            public bool needfuel;

            public ulong skinid = 1;
            public ulong ownerid;
            //int count;
            float minaltitude;
            FlyingCarpet instance;
            public bool throttleup;
            float sprintspeed;
            float normalspeed;
            //bool isenabled = true;
            SphereCollider sphereCollider;

            string prefabcarpet = "assets/prefabs/deployable/rug/rug.deployed.prefab";
            string prefablamp = "assets/prefabs/deployable/lantern/lantern.deployed.prefab";
            string prefablights = "assets/prefabs/misc/xmas/christmas_lights/xmas.lightstring.deployed.prefab";
            string prefablock = "assets/prefabs/locks/keypad/lock.code.prefab";
            string prefabsign = "assets/prefabs/deployable/signs/sign.small.wood.prefab";
            string prefabbox = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";

            void Awake()
            {
                entity = GetComponentInParent<BaseEntity>();
                entityrot = Quaternion.identity;
                entitypos = entity.transform.position;
                minaltitude = MinAltitude;
                instance = new FlyingCarpet();
                ownerid = entity.OwnerID;
                gameObject.name = "FlyingCarpet";

                engineon = false;
                hasFuel = false;
                //needfuel = requirefuel;
                if(!needfuel)
                {
                    hasFuel = true;
                }
                moveforward = false;
                movebackward = false;
                moveup = false;
                movedown = false;
                rotright = false;
                rotleft = false;
                sprinting = false;
                islanding = false;
                mounted = false;
                throttleup = false;
                sprintspeed = SprintSpeed;
                normalspeed = NormalSpeed;
                //isenabled = false;
                skinid = rugSkinID;
                SpawnCarpet();
                lantern1.OwnerID = entity.OwnerID;
                sign.OwnerID = entity.OwnerID;

                sphereCollider = entity.gameObject.AddComponent<SphereCollider>();
                sphereCollider.gameObject.layer = (int)Layer.Reserved1;
                sphereCollider.isTrigger = true;
                sphereCollider.radius = 6f;
            }

            BaseEntity SpawnPart(string prefab, BaseEntity entitypart, bool setactive, int eulangx, int eulangy, int eulangz, float locposx, float locposy, float locposz, BaseEntity parent, ulong skinid)
            {
#if DEBUG
                Interface.Oxide.LogInfo($"SpawnPart: {prefab}, active:{setactive.ToString()}, angles:({eulangx.ToString()}, {eulangy.ToString()}, {eulangz.ToString()}), position:({locposx.ToString()}, {locposy.ToString()}, {locposz.ToString()}), parent:{parent.ShortPrefabName} skinid:{skinid.ToString()}");
#endif
                entitypart = GameManager.server.CreateEntity(prefab, entitypos, entityrot, setactive);
                entitypart.transform.localEulerAngles = new Vector3(eulangx, eulangy, eulangz);
                entitypart.transform.localPosition = new Vector3(locposx, locposy, locposz);

                entitypart.SetParent(parent, 0);
                entitypart.skinID = Convert.ToUInt64(skinid);
                entitypart?.Spawn();
                SpawnRefresh(entitypart);
                return entitypart;
            }

            void SpawnRefresh(BaseEntity entity)
            {
                var hasstab = entity.GetComponent<StabilityEntity>() ?? null;
                if(hasstab != null)
                {
                    hasstab.grounded = true;
                }
                var hasmount = entity.GetComponent<BaseMountable>() ?? null;
                if(hasmount != null)
                {
                    hasmount.isMobile = true;
                }
            }

            public void SetFuel(int amount = 0)
            {
                BaseOven lanternCont = lantern1 as BaseOven;
                ItemContainer container1 = lanternCont.inventory;

                if(amount == 0)
                {
                    while(container1.itemList.Count > 0)
                    {
                        var item = container1.itemList[0];
                        item.RemoveFromContainer();
                        item.Remove(0f);
                    }
                }
                else
                {
                    Item addfuel = ItemManager.CreateByItemID(-946369541, amount);
                    container1.itemList.Add(addfuel);
                    addfuel.parent = container1;
                    addfuel.MarkDirty();
                }
            }

            public void SpawnCarpet()
            {
                carpet1 = SpawnPart(prefabcarpet, carpet1, false, 0, 0, 0, 0f, 0.3f, 0f, entity, skinid);
                carpet1.SetFlag(BaseEntity.Flags.Busy, true, true);
                carpet1.SetFlag(BaseEntity.Flags.Locked, true);

                lantern1 = SpawnPart(prefablamp, lantern1, true, 0, 0, 0, 0f, 0.33f, 1f, entity, 1);
                lantern1.SetFlag(BaseEntity.Flags.On, false);
                carpetlock = SpawnPart(prefablock, carpetlock, true, 0, 90, 90, 0.5f, 0.3f, 0.7f, entity, 1);
                sign = SpawnPart(prefabsign, sign, true, -45, 0, 0, 0, 0.25f, 1.75f, entity, 1);
//                box  = SpawnPart(prefabbox, box, true, 0, 0, 0, 0f, 0.33f, -1f, carpet1, 1);

                lights1 = SpawnPart(prefablights, lights1, true, 0, 90, 0, 0.8f, 0.31f, 0.1f, entity, 1);
                lights1.SetFlag(BaseEntity.Flags.Busy, true);
                lights2 = SpawnPart(prefablights, lights2, true, 0, 90, 0, -0.9f, 0.31f, 0.1f, entity, 1);
                lights2.SetFlag(BaseEntity.Flags.Busy, true);

                if(needfuel)
                {
                    // Empty tank
                    SetFuel(0);
                }
                else
                {
                    // Cannot be looted
                    lantern1.SetFlag(BaseEntity.Flags.Locked, true);
                    // Add some fuel (1 lgf) so it lights up anyway.  It should always stay at 1.
                    SetFuel(1);
                }
            }

            private void OnTriggerEnter(Collider col)
            {
                var target = col.GetComponentInParent<BasePlayer>();
                if(target != null)
                {
                    carpetantihack.Add(target);
                }
            }

            private void OnTriggerExit(Collider col)
            {
                var target = col.GetComponentInParent<BasePlayer>();
                if(target != null)
                {
                    carpetantihack.Remove(target);
                }
            }

            BasePlayer GetPilot()
            {
                player = entity.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
                return player;
            }

            public void CarpetInput(InputState input, BasePlayer player)
            {
                if(input == null || player == null) return;
                if(input.WasJustPressed(BUTTON.FORWARD)) moveforward = true;
                if(input.WasJustReleased(BUTTON.FORWARD)) moveforward = false;
                if(input.WasJustPressed(BUTTON.BACKWARD)) movebackward = true;
                if(input.WasJustReleased(BUTTON.BACKWARD)) movebackward = false;
                if(input.WasJustPressed(BUTTON.RIGHT)) rotright = true;
                if(input.WasJustReleased(BUTTON.RIGHT)) rotright = false;
                if(input.WasJustPressed(BUTTON.LEFT)) rotleft = true;
                if(input.WasJustReleased(BUTTON.LEFT)) rotleft = false;
                if(input.IsDown(BUTTON.SPRINT)) throttleup = true;
                if(input.WasJustReleased(BUTTON.SPRINT)) throttleup = false;
                if(input.WasJustPressed(BUTTON.JUMP)) moveup = true;
                if(input.WasJustReleased(BUTTON.JUMP)) moveup = false;
                if(input.WasJustPressed(BUTTON.DUCK)) movedown = true;
                if(input.WasJustReleased(BUTTON.DUCK)) movedown = false;
            }

            public bool FuelCheck()
            {
                if(!needfuel)
                {
                    return true;
                }
                BaseOven lantern = lantern1 as BaseOven;
                Item slot = lantern.inventory.GetSlot(0);
                if(slot == null)
                {
                    islanding = true;
                    hasFuel = false;
                    return false;
                }
                else
                {
                    hasFuel = true;
                    return true;
                }
            }

            void FixedUpdate()
            {
                if(engineon)
                {
                    if(!GetPilot()) islanding = true;
                    var currentspeed = normalspeed;
                    if(throttleup) { currentspeed = sprintspeed; }
                    RaycastHit hit;

                    // This is a little weird.  Fortunately, some of the hooks determine fuel status...
                    if(!hasFuel)
                    {
                        if(needfuel)
                        {
                            islanding = false;
                            engineon = false;
                            return;
                        }
                    }
                    if(islanding)
                    {
                        if(!Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 3.5f, layerMask))
                        {
                            // Drop fast
                            entity.transform.localPosition += (transform.up * -15f * Time.deltaTime);
                        }
                        else
                        {
                            // Slow down
                            entity.transform.localPosition += (transform.up * -5f) * Time.deltaTime;
                        }

                        if(Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 1f, layerMask))
                        {
                            islanding = false;
                            engineon = false;
                            if(pilotslist.Contains(player.userID))
                            {
                                pilotslist.Remove(player.userID);
                            }
                        }
                        ResetMovement();
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }

                    if(Physics.Raycast(new Ray(entity.transform.position, Vector3.down), minaltitude, layerMask))
                    {
                        entity.transform.localPosition += transform.up * minaltitude * Time.deltaTime;
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }

                    float rotspeed = 1f;
                    if(throttleup) rotspeed += 1;
                    if(rotright) entity.transform.eulerAngles += new Vector3(0, rotspeed, 0);
                    else if(rotleft) entity.transform.eulerAngles += new Vector3(0, -rotspeed, 0);

                    if(moveforward) entity.transform.localPosition += ((transform.forward * currentspeed) * Time.deltaTime);
                    else if(movebackward) entity.transform.localPosition = entity.transform.localPosition - ((transform.forward * currentspeed) * Time.deltaTime);

                    if(moveup) entity.transform.localPosition += ((transform.up * currentspeed) * Time.deltaTime);
                    else if(movedown) entity.transform.localPosition += ((transform.up * -currentspeed) * Time.deltaTime);

                    ServerMgr.Instance.StartCoroutine(RefreshTrain());
                }
            }

            private IEnumerator RefreshTrain()
            {
                entity.transform.hasChanged = true;
                for(int i = 0; i < entity.children.Count; i++)
                {
                    entity.children[i].transform.hasChanged = true;
                    entity.children[i].SendNetworkUpdateImmediate();
                    entity.children[i].UpdateNetworkGroup();
                }
                entity.SendNetworkUpdateImmediate();
                entity.UpdateNetworkGroup();
                yield return new WaitForEndOfFrame();
            }

            void ResetMovement()
            {
                moveforward = false;
                movebackward = false;
                moveup = false;
                movedown = false;
                rotright = false;
                rotleft = false;
                throttleup = false;
            }

            public void OnDestroy()
            {
                if(loadplayer.ContainsKey(ownerid)) loadplayer[ownerid].carpetcount = loadplayer[ownerid].carpetcount - 1;
                if(entity != null) { entity.Invoke("KillMessage", 0.1f); }
            }
        }
        #endregion
    }
}

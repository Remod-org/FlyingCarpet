#region License (MIT)
/*
Copyright 2021 RFC1920 <desolationoutpostpve@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software
is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion License Information (MIT)
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rust;
using System.Linq;
using Oxide.Core.Libraries.Covalence;
using System.Globalization;
using Oxide.Core.Plugins;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("FlyingCarpet", "RFC1920", "1.2.2")]
    [Description("Fly a custom object consisting of carpet, chair, lantern, lock, and small sign.")]
    // Thanks to Colon Blow for his fine work on GyroCopter, upon which this was originally based.
    class FlyingCarpet : RustPlugin
    {
        #region vars
        ConfigData configData;
        private static LayerMask layerMask;
        private static LayerMask buildingMask;

        private static Dictionary<ulong, PlayerCarpetData> loadplayer = new Dictionary<ulong, PlayerCarpetData>();
        private static List<ulong> pilotslist = new List<ulong>();

        public List<string> monNames = new List<string>();
        public SortedDictionary<string, Vector3> monPos  = new SortedDictionary<string, Vector3>();
        public SortedDictionary<string, Vector3> monSize = new SortedDictionary<string, Vector3>();

        private const string FCGUI = "fcgui.top";
        private const string FCGUM = "fcgui.menu";
        public static FlyingCarpet Instance = null;

        [PluginReference]
        Plugin PathFinding, SignArtist, GridAPI;

        public class PlayerCarpetData
        {
            public BasePlayer player;
            public int carpetcount;
        }

        void OnServerInitialized()
        {
            FindMonuments();
        }

        void Init()
        {
            Instance = this;

            LoadConfigVariables();
            layerMask = (1 << 29);
            layerMask |= (1 << 18);
            layerMask = ~layerMask;
            //buildingMask = LayerMask.GetMask("Construction", "Tree", "Rock", "Deployed", "World");
            buildingMask = LayerMask.GetMask("Construction", "Prevent Building", "Deployed", "World", "Terrain", "Tree", "Invisible", "Default");

            AddCovalenceCommand("fc", "cmdCarpetBuild");
            AddCovalenceCommand("fcc", "cmdCarpetCount");
            AddCovalenceCommand("fcd", "cmdCarpetDestroy");
            AddCovalenceCommand("fcg", "cmdCarpetGiveChat");
            AddCovalenceCommand("fchelp", "cmdCarpetHelp");
            AddCovalenceCommand("fcnav", "cmdCarpetNav");

            permission.RegisterPermission("flyingcarpet.use", this);
            permission.RegisterPermission("flyingcarpet.vip", this);
            permission.RegisterPermission("flyingcarpet.admin", this);
            permission.RegisterPermission("flyingcarpet.unlimited", this);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["helptext1"] = "Flying Carpet instructions:",
                ["helptext2"] = "  type /fc to spawn a Flying Carpet",
                ["helptext3"] = "  type /fcd to destroy your flyingcarpet.",
                ["helptext4"] = "  type /fcc to show a count of your carpets",
                ["notunnel"] = "Access to spawn in tunnels has been blocked !!",
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
                ["nolock"] = "You cannot place a lock on carpet storage.  Lock the carpet instead.",
                ["nofuel"] = "You're out of fuel !!",
                ["noplayer"] = "Unable to find player {0}!",
                ["gaveplayer"] = "Gave carpet to player {0}!",
                ["lowfuel"] = "You're low on fuel !!",
                ["flyingcarpet"] = "Flying Carpet",
                ["menu"] = "Press RELOAD for menu",
                ["close"] = "Close",
                ["cancel"] = "Cancel",
                ["heading"] = "Headed to {0}",
                ["arrived"] = "Arrived at {0}",
                ["nocarpets"] = "You have no Carpets",
                ["currcarpets"] = "Current Carpets : {0}",
                ["giveusage"] = "You need to supply a valid SteamId."
            }, this);
        }

        private bool isAllowed(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);
        private bool HasPermission(ConsoleSystem.Arg arg, string permname) => (arg.Connection.player as BasePlayer) == null ? true : permission.UserHasPermission((arg.Connection.player as BasePlayer).UserIDString, permname);

        private static HashSet<BasePlayer> FindPlayers(string nameOrIdOrIp)
        {
            var players = new HashSet<BasePlayer>();
            if (string.IsNullOrEmpty(nameOrIdOrIp)) return players;
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.UserIDString.Equals(nameOrIdOrIp))
                {
                    players.Add(activePlayer);
                }
                else if (!string.IsNullOrEmpty(activePlayer.displayName) && activePlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                {
                    players.Add(activePlayer);
                }
                else if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress.Equals(nameOrIdOrIp))
                {
                    players.Add(activePlayer);
                }
            }
            foreach (var sleepingPlayer in BasePlayer.sleepingPlayerList)
            {
                if (sleepingPlayer.UserIDString.Equals(nameOrIdOrIp))
                {
                    players.Add(sleepingPlayer);
                }
                else if (!string.IsNullOrEmpty(sleepingPlayer.displayName) && sleepingPlayer.displayName.Contains(nameOrIdOrIp, CompareOptions.IgnoreCase))
                {
                    players.Add(sleepingPlayer);
                }
            }
            return players;
        }
        #endregion

        #region Configuration
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData
            {
                Version = Version
            };
            SaveConfig(config);
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
            configData.Version = Version;
            SaveConfig(configData);
        }
        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            //public bool AllowDamage = true;
            public bool BlockInTunnel = true;
            public bool AllowLantern = false;
            public bool AllowRepaint = true;
            public bool UseMaxCarpetChecks = true;
            public bool DoubleFuel = false;
            public bool NameOnSign = true;
            public bool PlayEmptySound = false;
            public bool RequireFuel = true;
            public bool debug = false;
            public bool debugMovement = false;

            public int MaxCarpets = 1;
            public int VIPMaxCarpets = 2;
            //public float InitialHealth = 500;
            public float MinDistance = 10;
            public float MinAltitude = 5;
            public float CruiseAltitude = 35;
            public float NormalSpeed = 12;
            public float SprintSpeed = 25;

            //public ulong ChairSkinID = 943293895;
            public ulong BoxSkinID = 1330214613;
            public ulong RugSkinID = 870448575;

            public VersionNumber Version;
        }
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region Chat Commands
        [Command("fc"), Permission("flyingcarpet.use")]
        void cmdCarpetBuild(IPlayer iplayer, string command, string[] args)
        {
            bool vip = false;
            var player = iplayer.Object as BasePlayer;
            if (!iplayer.HasPermission("flyingcarpet.use")) { Message(iplayer, "notauthorized"); return; }
            if (args.Length == 1)
            {
                if (args[0] == "guiclose")
                {
                    CuiHelper.DestroyUi(player, FCGUM);
                    return;
                }
            }
            if (iplayer.HasPermission("flyingcarpet.vip"))
            {
                vip = true;
            }
            if (CarpetLimitReached(player, vip)) { Message(iplayer, "maxcarpets"); return; }
            if (configData.BlockInTunnel && player.transform.position.y < -70)
            {
                Message(iplayer, "notunnel");
                return;
            }
            AddCarpet(player, player.transform.position);
        }

        [Command("fcg"), Permission("flyingcarpet.admin")]
        void cmdCarpetGiveChat(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (args.Length == 0)
            {
                Message(iplayer, "giveusage");
                return;
            }
            bool vip = false;
            string pname = args[0] == null ? null : args[0];

            if (!iplayer.HasPermission("flyingcarpet.admin")) { Message(iplayer, "notauthorized"); return; }
            if (pname == null) { Message(iplayer, "noplayer", "NAME_OR_ID"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if (Bplayer == null)
            {
                Message(iplayer, "noplayer", pname);
                return;
            }

            var Iplayer = Bplayer.IPlayer;
            if (Iplayer.HasPermission("flyingcarpet.vip"))
            {
                vip = true;
            }
            if (CarpetLimitReached(Bplayer, vip)) { Message(iplayer, "maxcarpets"); return; }
            AddCarpet(Bplayer, Bplayer.transform.position);
            Message(iplayer, "gaveplayer", pname);
        }

        [ConsoleCommand("fcgive")]
        void cmdCarpetGive(ConsoleSystem.Arg arg)
        {
            if (arg.IsRcon)
            {
                if (arg.Args == null)
                {
                    Puts("You need to supply a valid SteamId.");
                    return;
                }
            }
            else if (!HasPermission(arg, "flyingcarpet.admin"))
            {
                SendReply(arg, Lang("notauthorized", null, arg.Connection.player as BasePlayer));
                return;
            }
            else if (arg.Args == null)
            {
                SendReply(arg, Lang("giveusage", null, arg.Connection.player as BasePlayer));
                return;
            }

            bool vip = false;
            string pname = arg.GetString(0);

            if (pname.Length < 1) { Puts("Player name or id cannot be null"); return; }

            BasePlayer Bplayer = BasePlayer.Find(pname);
            if (Bplayer == null) { Puts($"Unable to find player '{pname}'"); return; }

            var Iplayer = Bplayer.IPlayer;
            if (Iplayer.HasPermission("flyingcarpet.vip")) { vip = true; }
            if (CarpetLimitReached(Bplayer, vip))
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
            if (!iplayer.HasPermission("flyingcarpet.use")) { Message(iplayer, "notauthorized"); return; }
            if (!loadplayer.ContainsKey(player.userID))
            {
                Message(iplayer, "nocarpets");
                return;
            }
            string ccount = loadplayer[player.userID].carpetcount.ToString();
            DoLog("CarpetCount: " + ccount);
            Message(iplayer, "currcarpets", ccount);
        }

        [Command("fcd"), Permission("flyingcarpet.use")]
        void cmdCarpetDestroy(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (!iplayer.HasPermission("flyingcarpet.use")) { Message(iplayer, "notauthorized"); return; }

            string target = null;
            if (args.Length > 0)
            {
                target = args[0];
            }
            if (iplayer.HasPermission("flyingcarpet.admin") && target != null)
            {
                if (target == "all")
                {
                    DestroyAllCarpets(player);
                    return;
                }
                var players = FindPlayers(target);
                if (players.Count <= 0)
                {
                    Message(iplayer, "PlayerNotFound", target);
                    return;
                }
                if (players.Count > 1)
                {
                    Message(iplayer, "MultiplePlayers", target, string.Join(", ", players.Select(p => p.displayName).ToArray()));
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
            if (!iplayer.HasPermission("flyingcarpet.use")) { Message(iplayer, "notauthorized"); return; }
            Message(iplayer, "helptext1");
            Message(iplayer, "helptext2");
            Message(iplayer, "helptext3");
            Message(iplayer, "helptext4");
        }

        [Command("fcnav"), Permission("flyingcarpet.use")]
        void cmdCarpetNav(IPlayer iplayer, string command, string[] args)
        {
            string debug = string.Join(",", args); Puts($"{debug}");

            var player = iplayer.Object as BasePlayer;
            var iscarpet = player.GetMounted().GetComponentInParent<CarpetEntity>() ?? null;
            if (args.Length > 0 && args[0] == "navclose")
            {
                CuiHelper.DestroyUi(player, FCGUM);
                return;
            }
            else if (args.Length > 0 && args[0] == "navcancel")
            {
                CuiHelper.DestroyUi(player, FCGUM);
                iscarpet.nav.enabled = false;
                iscarpet.nav.paused = false;
                iscarpet.autopilot = false;
                return;
            }

			Vector3 target = new Vector3();
			string monname = null;
            for (int i = 1; i < args.Length; i++)
            {
                monname += args[i] + " ";
			}
			monname = monname.Trim();
            Puts($"Flying from {player.transform.position.ToString()} to {target.ToString()}");

            if (iscarpet != null)
			{
                iscarpet.nav.currentMonument = monname;
                iscarpet.nav.enabled = true;
                iscarpet.nav.paused = false;
                iscarpet.lantern1.SetFlag(BaseEntity.Flags.On, true);
                iscarpet.engineon = true;
                CuiHelper.DestroyUi(player, FCGUM);
                ShowTopGUI(player, Lang("heading", null, monname));
                player.SendConsoleCommand("ddraw.text", 90, Color.green, monPos[monname], $"<size=20>{monname}</size>");
			}
		}
        #endregion

        #region GUI
        private void ShowTopGUI(BasePlayer player, string target = "")
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, FCGUI);
            if (target == "") target = Lang("menu");

            CuiElementContainer container = UI.Container(FCGUI, UI.Color("222222", 0.9f), "0.4 0.95", "0.6 1", false, "Overlay");
            UI.Label(ref container, FCGUI, UI.Color("#ffffff", 1f), $"Flying Carpet: {target}", 12, "0.1 0", "0.9 1");

            CuiHelper.AddUi(player, container);
        }

        private void ShowMenuGUI(BasePlayer player, string mode = null, string extra = null)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, FCGUM);

            CuiElementContainer container = UI.Container(FCGUM, UI.Color("222222", 0.9f), "0.3 0.3", "0.7 0.7", true, "Overlay");
            UI.Label(ref container, FCGUM, UI.Color("#ffffff", 1f), "Flying Carpet Menu", 16, "0 0.92", "0.9 0.99");
            UI.Button(ref container, FCGUM, UI.Color("#d85540", 1f), Lang("close"), 12, "0.92 0.93", "0.99 0.99", "fcnav navclose", UI.Color("#ffffff", 1));

            int row = 0;
            int col = 0;
            float[] posb = new float[4];
            foreach (KeyValuePair<string, Vector3> mons in monPos)
            {
                if (row > 10)
                {
                    row = 0;
                    col++;
                }
                posb = GetButtonPositionP(row, col);

                UI.Button(ref container, FCGUM, UI.Color("#d85540", 1f), mons.Key, 8, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", $"fcnav {player.userID} {mons.Key}", UI.Color("#ffffff", 1));
                row++;
            }
            row++;
            UI.Button(ref container, FCGUM, UI.Color("#ff0000", 1f), Lang("cancel"), 8, $"{posb[0]} {posb[1]}", $"{posb[0] + ((posb[2] - posb[0]) / 2)} {posb[3]}", "fcnav navcancel", UI.Color("#ffffff", 1));

            CuiHelper.AddUi(player, container);
        }

        private int RowNumber(int max, int count) => Mathf.FloorToInt(count / max);
        private float[] GetButtonPosition(int rowNumber, int columnNumber)
        {
            float offsetX = 0.05f + (0.096f * columnNumber);
            float offsetY = (0.80f - (rowNumber * 0.064f));

            return new float[] { offsetX, offsetY, offsetX + 0.196f, offsetY + 0.03f };
        }
        private float[] GetButtonPositionP(int rowNumber, int columnNumber)
        {
            float offsetX = 0.05f + (0.186f * columnNumber);
            float offsetY = (0.85f - (rowNumber * 0.074f));

            return new float[] { offsetX, offsetY, offsetX + 0.256f, offsetY + 0.03f };
        }
        #endregion

        #region our_hooks
        private object OnCarpetNavChange(BasePlayer pl, string newdirection)
        {
            ShowTopGUI(pl, Lang("heading", null, newdirection));
            return null;
        }

        private object OnCarpetNavArrived(BasePlayer pl, string newdirection)
        {
            ShowTopGUI(pl, Lang("arrived", null, newdirection));
            return null;
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
                if (activecarpet == null) return null;
            }
            catch
            {
                if (oven.GetComponentInParent<CarpetEntity>() && !configData.AllowLantern)
                {
                    DoLog("No player mounted on this carpet.");
                    Message(player.IPlayer, "nostartseat"); return rtrn;
                }
                DoLog("No carpet or not mounted.");
                return null;
            }

            if (activecarpet.carpetlock != null && activecarpet.carpetlock.IsLocked()) { Message(player.IPlayer, "carpetlocked"); return rtrn; }
            if (!player.isMounted) return rtrn; // player offline, does not mean ismounted on carpet

            if (player.GetMounted() != activecarpet.entity)
            {
                DoLog("OnOvenToggle: Player not mounted on carpet!");
                oven.StopCooking();
                return rtrn;
            }
            DoLog("OnOvenToggle: Player cycled lantern!");
            if (oven.IsOn())
            {
                oven.StopCooking();
            }
            else
            {
                oven.StartCooking();
            }
            if (!activecarpet.FuelCheck())
            {
                if (activecarpet.needfuel)
                {
                    Message(player.IPlayer, "nofuel");
                    Message(player.IPlayer, "landingcarpet");
                    activecarpet.engineon = false;
                }
            }
            var ison = activecarpet.engineon;
            if (ison) { activecarpet.islanding = true; Message(player.IPlayer, "landingcarpet"); return null; }
            if (!ison) { AddPlayerToPilotsList(player); activecarpet.engineon = true; return null; }

            return rtrn;
        }

        // Check for carpet lantern fuel
        void OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            // Only work on lanterns
            if (oven.ShortPrefabName != "lantern.deployed") return;
            int dbl = configData.DoubleFuel ? 4 : 2;

            BaseEntity lantern = oven as BaseEntity;
            // Only work on lanterns attached to a Carpet
            var activecarpet = lantern.GetComponentInParent<CarpetEntity>() ?? null;
            if (activecarpet == null) return;
            DoLog("OnConsumeFuel: found a carpet lantern!");

            if (activecarpet.needfuel)
            {
                DoLog("OnConsumeFuel: carpet requires fuel!");
            }
            else
            {
                DoLog("OnConsumeFuel: carpet does not require fuel!");
                fuel.amount++; // Required to keep it from decrementing
                return;
            }
            BasePlayer player = activecarpet.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
            if (!player) return;
            DoLog("OnConsumeFuel: checking fuel level...");
            // Before it drops to 1 (3 for doublefuel) AFTER this hook call is complete, warn them that the fuel is low (1) - ikr
            if (fuel.amount == dbl)
            {
                DoLog("OnConsumeFuel: sending low fuel warning...");
                if (configData.PlayEmptySound)
                {
                    Effect.server.Run("assets/bundled/prefabs/fx/well/pump_down.prefab", player.transform.position);
                }
                Message(player.IPlayer, "lowfuel");
            }

            if (configData.DoubleFuel)
            {
                fuel.amount--;
            }

            if (fuel.amount == 0)
            {
                DoLog("OnConsumeFuel: out of fuel.");
                Message(player.IPlayer, "lowfuel");
                var ison = activecarpet.engineon;
                if (ison)
                {
                    activecarpet.islanding = true;
                    activecarpet.engineon = false;
                    Message(player.IPlayer, "landingcarpet");
                    OnOvenToggle(oven, player);
                    return;
                }
            }
        }

        // To skip cycling our lantern (thanks, k11l0u)
        private object OnNightLanternToggle(BaseEntity entity, bool status)
        {
            // Only work on lanterns
            if (entity.ShortPrefabName != "lantern.deployed") return null;
            DoLog("OnNightLanternToggle: Called on a lantern.  Checking for carpet...");

            // Only work on lanterns attached to a Carpet
            var activecarpet = entity.GetComponentInParent<CarpetEntity>() ?? null;
            if (activecarpet != null)
            {
                DoLog("OnNightLanternToggle: Do not cycle this lantern!");
                return true;
            }
            DoLog("OnNightLanternToggle: Not a carpet lantern.");
            return null;
        }
        #endregion

        #region Primary
        private void DoLog(string message, bool ismovement = false)
        {
            if (ismovement && !configData.debugMovement) return;
            if (configData.debugMovement | configData.debug) Interface.Oxide.LogInfo(message);
        }

        private void AddCarpet(BasePlayer player, Vector3 location)
        {
            if (player == null && location == null) return;
            if (location == null && player != null) location = player.transform.position;
            Vector3 spawnpos = new Vector3();

            // Set initial default for fuel requirement based on config
            bool needfuel = configData.RequireFuel;
            if (isAllowed(player, "flyingcarpet.unlimited"))
            {
                // User granted unlimited fly time without fuel
                needfuel = false;
            }

            if (needfuel)
            {
                // Don't put them on the carpet since they need to fuel up first
                spawnpos = player.transform.position + -player.transform.forward * 2f + new Vector3(0, 1f, 0);
            }
            else
            {
                // Spawn at point of player
                spawnpos = new Vector3(location.x, location.y + 0.5f, location.z);
            }

            string staticprefab = "assets/bundled/prefabs/static/chair.invisible.static.prefab";
            var newCarpet = GameManager.server.CreateEntity(staticprefab, spawnpos, new Quaternion(), true);
            newCarpet.name = "FlyingCarpet";
            var chairmount = newCarpet.GetComponent<BaseMountable>();
            chairmount.isMobile = true;
            newCarpet.enableSaving = false;
            newCarpet.OwnerID = player.userID;
            //newCarpet.skinID = Convert.ToUInt64(configData.ChairSkinID);
            newCarpet.Spawn();
            var carpet = newCarpet.gameObject.AddComponent<CarpetEntity>();
            carpet.needfuel = needfuel;
            // Unlock the tank if they need fuel.
            carpet.lantern1.SetFlag(BaseEntity.Flags.Locked, !needfuel);
            if (needfuel)
            {
                // We have to set this after the spawn.
                carpet.SetFuel(0);
            }

            // paint sign with username
            if (SignArtist && configData.NameOnSign)
            {
                // This only works with version 1.1.7 on
                if (SignArtist.Version >= new VersionNumber(1, 1, 7))
                {
                    string message = player.displayName;
                    int fontsize = Convert.ToInt32(Math.Floor(150f / message.Length));
                    SignArtist?.Call("API_SignText", player, carpet.sign, message, fontsize, "00FF00", "000000");
                }
            }
            if (!configData.AllowRepaint)
            {
                carpet.sign.SetFlag(BaseEntity.Flags.Busy, true);
            }
            carpet.sign.SetFlag(BaseEntity.Flags.Locked, true);

            AddPlayerID(player.userID);

            if (chairmount != null && player != null)
            {
                Message(player.IPlayer, "carpetspawned");
                if (carpet.needfuel)
                {
                    Message(player.IPlayer, "carpetfuel");
                }
                else
                {
                    // Put them in the chair.  They will still need to unlock it.
                    Message(player.IPlayer, "carpetnofuel");
                    chairmount.MountPlayer(player);
                }
                return;
            }
        }

        public bool PilotListContainsPlayer(BasePlayer player)
        {
            if (pilotslist.Contains(player.userID)) return true;
            return false;
        }

        void AddPlayerToPilotsList(BasePlayer player)
        {
            if (PilotListContainsPlayer(player)) return;
            pilotslist.Add(player.userID);
        }

        public void RemovePlayerFromPilotsList(BasePlayer player)
        {
            if (PilotListContainsPlayer(player))
            {
                pilotslist.Remove(player.userID);
                return;
            }
        }

        void DestroyLocalCarpet(BasePlayer player)
        {
            if (player == null) return;
            List<BaseEntity> carpetlist = new List<BaseEntity>();
            Vis.Entities(player.transform.position, configData.MinDistance, carpetlist);
            bool foundcarpet = false;

            foreach (BaseEntity p in carpetlist)
            {
                var foundent = p.GetComponentInParent<CarpetEntity>() ?? null;
                if (foundent != null)
                {
                    foundcarpet = true;
                    if (foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    Message(player.IPlayer, "carpetdestroyed");
                }
            }
            if (!foundcarpet)
            {
                Message(player.IPlayer, "notfound", configData.MinDistance.ToString());
            }
        }

        void DestroyAllCarpets(BasePlayer player)
        {
            List<BaseEntity> carpetlist = new List<BaseEntity>();
            Vis.Entities(new Vector3(0,0,0), 3500f, carpetlist);
            bool foundcarpet = false;

            foreach (BaseEntity p in carpetlist)
            {
                var foundent = p.GetComponentInParent<CarpetEntity>() ?? null;
                if (foundent != null)
                {
                    foundcarpet = true;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    Message(player.IPlayer, "carpetdestroyed");
                }
            }
            if (!foundcarpet)
            {
                Message(player.IPlayer, "notfound", configData.MinDistance.ToString());
            }
        }

        void DestroyRemoteCarpet(BasePlayer player)
        {
            if (player == null) return;
            List<BaseEntity> carpetlist = new List<BaseEntity>();
            Vis.Entities(new Vector3(0,0,0), 3500f, carpetlist);
            bool foundcarpet = false;

            foreach (BaseEntity p in carpetlist)
            {
                var foundent = p.GetComponentInParent<CarpetEntity>() ?? null;
                if (foundent != null)
                {
                    foundcarpet = true;
                    if (foundent.ownerid != player.userID) return;
                    foundent.entity.Kill(BaseNetworkable.DestroyMode.Gib);
                    Message(player.IPlayer, "carpetdestroyed");
                }
            }
            if (!foundcarpet)
            {
                Message(player.IPlayer, "notfound", configData.MinDistance.ToString());
            }
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!player.isMounted) return;

            //if (input.current.buttons > 1 && configData.debugMovement)
            //    Puts($"OnPlayerInput: {input.current.buttons}");

            var activecarpet = player.GetMounted().GetComponentInParent<CarpetEntity>() ?? null;
            if (activecarpet == null) return;
            if (player.GetMounted() != activecarpet.entity) return;
            if (input != null)
            {
                activecarpet.CarpetInput(input, player);
            }
            return;
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo == null) return;
            var iscarpet = entity.GetComponentInParent<CarpetEntity>() ?? null;
            if (iscarpet != null) hitInfo.damageTypes.ScaleAll(0);
            return;
        }

        object OnEntityGroundMissing(BaseEntity entity)
        {
            var iscarpet = entity.GetComponentInParent<CarpetEntity>() ?? null;
            if (iscarpet != null) return false;
            return null;
        }

        bool CarpetLimitReached(BasePlayer player, bool vip=false)
        {
            if (configData.UseMaxCarpetChecks)
            {
                if (loadplayer.ContainsKey(player.userID))
                {
                    var currentcount = loadplayer[player.userID].carpetcount;
                    int maxallowed = configData.MaxCarpets;
                    if (vip)
                    {
                        maxallowed = configData.VIPMaxCarpets;
                    }
                    if (currentcount >= maxallowed) return true;
                }
            }
            return false;
        }

        object CanDismountEntity(BasePlayer player, BaseMountable entity)
        {
            if (player == null) return null;
            if (PilotListContainsPlayer(player)) return false;
            return null;
        }

        void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            var activecarpet = mountable.GetComponentInParent<CarpetEntity>() ?? null;
            if (activecarpet != null)
            {
                DoLog("OnEntityMounted: player mounted copter!");
                if (mountable.GetComponent<BaseEntity>() != activecarpet.entity) return;
                activecarpet.lantern1.SetFlag(BaseEntity.Flags.On, false);
                ShowTopGUI(player);
            }
        }

        void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            var activecarpet = mountable.GetComponentInParent<CarpetEntity>() ?? null;
            if (activecarpet != null)
            {
                DoLog("OnEntityMounted: player dismounted copter!");
                CuiHelper.DestroyUi(player, FCGUI);
                if (mountable.GetComponent<BaseEntity>() != activecarpet.entity) return;
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || player == null) return null;
            var iscarpet = container.GetComponentInParent<CarpetEntity>() ?? null;
            if (iscarpet != null)
            {
                if (iscarpet.carpetlock != null && iscarpet.carpetlock.IsLocked()) return false;
            }
            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if (entity == null || player == null) return null;

            BaseEntity myent = entity as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if (myparent == "FlyingCarpet" || myent.name == "FlyingCarpet")
            {
                if (configData.debug)
                {
                    if (myent.name == "FlyingCarpet")
                    {
                        Puts("CanPickupEntity: player trying to pickup the carpet!");
                    }
                    else if (myparent == "FlyingCarpet")
                    {
                        string entity_name = myent.LookupPrefab().name;
                        Puts($"CanPickupEntity: player trying to remove {entity_name} from a carpet!");
                    }
                }
                Message(player.IPlayer, "notauthorized");
                return false;
            }
            return null;
        }

        // Check for our coffin, block adding another lock
        private object CanDeployItem(BasePlayer player, Deployer deployer, uint entityId)
        {
            if (entityId == 0 || player == null) return null;
            string myparent = null;
            try
            {
                var myent = BaseNetworkable.serverEntities.Find(entityId);
                myparent = myent.GetParentEntity().name;
                Puts(myparent);
            }
            catch {}

            if (myparent == "FlyingCarpet")
            {
#if DEBUG
                Puts($"Player {player.displayName} trying to place an item(lock?) on carpet storage!");
#endif
                Message(player.IPlayer, "nolock");
                return true;
            }

            return null;
        }
        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if (baseLock == null || player == null) return null;

            BaseEntity myent = baseLock as BaseEntity;
            string myparent = null;
            try
            {
                myparent = myent.GetParentEntity().name;
            }
            catch {}

            if (myparent == "FlyingCarpet")
            {
                DoLog("CanPickupLock: player trying to remove lock from a carpet!");
                Message(player.IPlayer, "notauthorized");
                return false;
            }
            return null;
        }

        void AddPlayerID(ulong ownerid)
        {
            if (!loadplayer.ContainsKey(ownerid))
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
            if (loadplayer.ContainsKey(ownerid)) loadplayer[ownerid].carpetcount = loadplayer[ownerid].carpetcount - 1;
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
            if (objects != null)
            {
                foreach (var gameObj in objects)
                {
                    GameObject.Destroy(gameObj);
                }
            }
        }

        void Unload()
        {
            DestroyAll<CarpetEntity>();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, FCGUI);
                CuiHelper.DestroyUi(player, FCGUM);
            }
        }

        void FindMonuments()
        {
            Vector3 extents = Vector3.zero;
            string name = null;
            bool ishapis =  ConVar.Server.level.Contains("Hapis");

            foreach (MonumentInfo monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                if (monument.name.Contains("power_sub")) continue;// || monument.name.Contains("cave")) continue;
                name = null;

                if (monument.name == "OilrigAI")
                {
                    name = "Small Oilrig";
                }
                else if (monument.name == "OilrigAI2")
                {
                    name = "Large Oilrig";
                }
                else
                {
                    if (ishapis)
                    {
                        var elem = Regex.Matches(monument.name, @"\w{4,}|\d{1,}");
                        foreach (Match e in elem)
                        {
                            if (e.Value.Equals("MONUMENT")) continue;
                            if (e.Value.Contains("Label")) continue;
                            name += e.Value + " ";
                        }
                        name = name.Trim();
                    }
                    else
                    {
                        name = Regex.Match(monument.name, @"\w{6}\/(.+\/)(.+)\.(.+)").Groups[2].Value.Replace("_", " ").Replace(" 1", "").Titleize();
                    }
                }
                if (monPos.ContainsKey(name)) continue;

                extents = monument.Bounds.extents;

                if (extents.z < 1)
                {
                    extents.z = 100f;
                }
                monNames.Add(name);
                monPos.Add(name, monument.transform.position);
                monSize.Add(name, extents);

//                Puts($"Monument {name} @ {monument.transform.position.ToString()} size: {extents.z.ToString()}");
            }
            monPos.OrderBy(x => x.Key);
            monSize.OrderBy(x => x.Key);
        }
        #endregion

        #region Carpet Antihack check
        static List<BasePlayer> carpetantihack = new List<BasePlayer>();

        object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if (player == null) return null;
            if (carpetantihack.Contains(player)) return false;
            return null;
        }
        #endregion

        #region Carpet Classes
        class CarpetNav : MonoBehaviour
        {
            public CarpetEntity carpet;
            public BasePlayer player;
            public int buildingMask = LayerMask.GetMask("Construction", "Prevent Building", "Deployed", "World", "Terrain", "Tree", "Invisible", "Default");

            public uint carpetid;
            public ulong ownerid;
            public InputState input;

            public string currentMonument;
            public Vector3 current;
            public Quaternion rotation;
            public static Vector3 direction;
            public Vector3 target = Vector3.zero;
            public static Vector3 last = Vector3.zero;
            public static GameObject slop = new GameObject();

            public int whichPoint;
            public int totalPoints;

            public static bool grounded = true;
            public static bool started = false;
            public bool paused;

            void Awake()
            {
                Instance.DoLog($"Awake()");
                carpet = GetComponent<CarpetEntity>();
                player = carpet.player;
                enabled = false;
                paused = false;
            }

            void Update()
            {
                if (!carpet.engineon) return;
                MoveToMonument();
            }

            void MoveToMonument()
            {
                if (carpet == null) return;
                if (string.IsNullOrEmpty(currentMonument)) return;

                current = carpet.transform.position;
                target = new Vector3(Instance.monPos[currentMonument].x, GetHeight(Instance.monPos[currentMonument]), Instance.monPos[currentMonument].z);

                direction = (target - current).normalized;
                var lookrotation = Quaternion.LookRotation(direction);

                var monsize = Instance.monSize[currentMonument].z;
                if (Vector3.Distance(current, target) <= monsize)
                {
                    Instance.DoLog($"Within {monsize.ToString()}m of {currentMonument.ToString()}", true);
                    carpet.lantern1.SetFlag(BaseEntity.Flags.On, false);
                    carpet.islanding = true;
                    enabled = false;
                    paused = false;
                    carpet.autopilot = false;

                    carpet.transform.rotation = Quaternion.identity; // Land flat
                    Instance.OnCarpetNavArrived(carpet.player, currentMonument);
                    return;
                }
                carpet.transform.LookAt(target);
                DoMoveCarpet(direction);
            }

            void DoMoveCarpet(Vector3 direction)
            {
                if (paused) return;
                InputMessage message = new InputMessage() { buttons = 0 };

                bool toolow = TooLow(current);
                bool above = DangerAbove(current);
                bool frontcrash = DangerFront(current);
                float terrainHeight = TerrainMeta.HeightMap.GetHeight(current);

                if (above)
                {
                    // Move right to try to get around this crap
                    Instance.DoLog($"Moving down and right to try to avoid...", true);
                    message.buttons = 48;
                }
                if (current.y < target.y || (current.y - terrainHeight < 1.5f) || toolow || frontcrash)
                {
                    if (!above)
                    {
                        // Move UP
                        Instance.DoLog($"Moving UP {current.y}", true);
                        message.buttons = 32;
                    }
                }
                else if (current.y > (terrainHeight + Instance.configData.CruiseAltitude * 2) + 5 && !frontcrash)
                {
                    if (!above)
                    {
                        Instance.DoLog($"Moving DOWN {current.y}", true);
                        message.buttons = 64;
                    }
                }

                if (!toolow)
                {
                    if (!DangerRight(current) && frontcrash && !above)
                    {
                        Instance.DoLog($"Moving BACK and RIGHT to avoid frontal crash", true);
                        message.buttons = 48;
                    }
                    else if (!DangerLeft(current) && frontcrash && !above)
                    {
                        Instance.DoLog($"Moving BACK and LEFT to avoid frontal crash", true);
                        message.buttons = 40;
                    }
                }

                if (!frontcrash && !toolow && !above)
                {
                    // Move Forward Fast
                    message.buttons += 130;
                }

                InputState input = new InputState() { current = message };

                carpet.CarpetInput(input, player);

                last = carpet.transform.position;
            }

            float GetHeight(Vector3 tgt)
            {
                float terrainHeight = TerrainMeta.HeightMap.GetHeight(tgt);
                float targetHeight = terrainHeight + Instance.configData.CruiseAltitude;

                RaycastHit hitinfo;
                if (Physics.Raycast(current, Vector3.down, out hitinfo, 100f, LayerMask.GetMask("Water")))
                {
                    if (TerrainMeta.WaterMap.GetHeight(hitinfo.point) < terrainHeight)
                    {
                        targetHeight = TerrainMeta.WaterMap.GetHeight(tgt) + Instance.configData.CruiseAltitude;
                    }
                }

                return targetHeight;
            }

            #region crash_avoidance
            bool DangerLeft(Vector3 tgt)
            {
                RaycastHit hitinfo;
                if (Physics.Raycast(current, carpet.transform.TransformDirection(Vector3.left), out hitinfo, 4f, buildingMask))
                {
                    if (hitinfo.GetEntity() != carpet)
                    {
                        if (hitinfo.distance < 2) return false;
                        string hit = null;
                        try
                        {
                            hit = $" with {hitinfo.GetEntity().ShortPrefabName}";
                            var d = Math.Round(hitinfo.distance, 2).ToString();
                            Instance.DoLog($"CRASH LEFT{hit} distance {d}!", true);
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
                return false;
            }

            bool DangerRight(Vector3 tgt)
            {
                RaycastHit hitinfo;
                if (Physics.Raycast(current, carpet.transform.TransformDirection(Vector3.right), out hitinfo, 4f, buildingMask))
                {
                    if (hitinfo.GetEntity() != carpet)
                    {
                        if (hitinfo.distance < 2) return false;
                        string hit = null;
                        try
                        {
                            hit = $" with {hitinfo.GetEntity().ShortPrefabName}";
                            var d = Math.Round(hitinfo.distance, 2).ToString();
                            Instance.DoLog($"CRASH RIGHT{hit} distance {d}!", true);
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
                return false;
            }

            bool DangerAbove(Vector3 tgt)
            {
                // In case we get stuck under a building component, esp at OilRigs
                RaycastHit hitinfo;
                if (Physics.Raycast(current + carpet.transform.up, Vector3.up, out hitinfo, 2f, buildingMask))
                {
                    if (hitinfo.GetEntity() != carpet)
                    {
                        Instance.DoLog($"CRASH ABOVE!", true);
                        return true;
                    }
                }
                return false;
            }

            bool DangerFront(Vector3 tgt)
            {
                RaycastHit hitinfo;
                if (Physics.Raycast(current, carpet.transform.TransformDirection(Vector3.forward), out hitinfo, 10f, buildingMask))
                {
                    if (hitinfo.GetEntity() != carpet)
                    {
                        if (hitinfo.distance < 2) return false;
                        string hit = null;
                        try
                        {
                            hit = $" with {hitinfo.GetEntity().ShortPrefabName}";
                            var d = Math.Round(hitinfo.distance, 2).ToString();
                            Instance.DoLog($"FRONTAL CRASH{hit} distance {d}m!", true);
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
                return false;
            }

            bool TooLow(Vector3 tgt)
            {
                RaycastHit hitinfo;
                int groundLayer = LayerMask.GetMask("Construction", "Terrain", "World", "Water");
                if (Physics.Raycast(current, Vector3.down, out hitinfo, 10f, groundLayer) || Physics.Raycast(current, Vector3.up, out hitinfo, 10f, groundLayer))
                {
                    if (hitinfo.GetEntity() != carpet)
                    {
                        Instance.DoLog($"TOO LOW!", true);
                        return true;
                    }
                }
                return false;
            }
            #endregion
        }

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

            public CarpetNav nav;
            public string entname = "FlyingCarpet";
            float fMagnitude = 0.75f;

            public Quaternion entityrot;
            Vector3 entitypos;

            public bool autopilot;
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
            float minaltitude;
            float cruisealtitude;
            FlyingCarpet instance;
            public bool throttleup;
            public bool showmenu;
            float sprintspeed;
            float normalspeed;
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
                minaltitude = Instance.configData.MinAltitude;
                cruisealtitude = Instance.configData.CruiseAltitude;
                instance = new FlyingCarpet();
                ownerid = entity.OwnerID;
                gameObject.name = "FlyingCarpet";

                engineon = false;
                hasFuel = false;
                //needfuel = requirefuel;
                if (!needfuel)
                {
                    hasFuel = true;
                }
                autopilot = false;
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
                showmenu = false;
                sprintspeed = Instance.configData.SprintSpeed;
                normalspeed = Instance.configData.NormalSpeed;
                skinid = Instance.configData.RugSkinID;
                SpawnCarpet();
                lantern1.OwnerID = entity.OwnerID;
                sign.OwnerID = entity.OwnerID;

                sphereCollider = entity.gameObject.AddComponent<SphereCollider>();
                sphereCollider.gameObject.layer = (int)Layer.Reserved1;
                sphereCollider.isTrigger = true;
                sphereCollider.radius = 6f;

                nav = entity.gameObject.AddComponent<CarpetNav>();
            }

            private BaseEntity SpawnPart(string prefab, BaseEntity entitypart, bool setactive, int eulangx, int eulangy, int eulangz, float locposx, float locposy, float locposz, BaseEntity parent, ulong skinid)
            {
                if (Instance.configData.debug)
                {
                    Interface.Oxide.LogInfo($"SpawnPart: {prefab}, active:{setactive.ToString()}, angles:({eulangx.ToString()}, {eulangy.ToString()}, {eulangz.ToString()}), position:({locposx.ToString()}, {locposy.ToString()}, {locposz.ToString()}), parent:{parent.ShortPrefabName} skinid:{skinid.ToString()}");
                }
                entitypart = GameManager.server.CreateEntity(prefab, entitypos, entityrot, setactive);
                //Instance.Puts($"Setting skinid of {prefab} to {skinid.ToString()}");
                entitypart.skinID = Convert.ToUInt64(skinid);
                entitypart?.Spawn();
                SpawnRefresh(entitypart);

                entitypart.transform.localEulerAngles = new Vector3(eulangx, eulangy, eulangz);
                entitypart.transform.localPosition = new Vector3(locposx, locposy, locposz);
                entitypart.SetParent(parent, 0);

                return entitypart;
            }

            void SpawnRefresh(BaseEntity entity)
            {
                var hasstab = entity.GetComponent<StabilityEntity>() ?? null;
                if (hasstab != null)
                {
                    hasstab.grounded = true;
                }
                var hasmount = entity.GetComponent<BaseMountable>() ?? null;
                if (hasmount != null)
                {
                    hasmount.isMobile = true;
                }
            }

            public void SetFuel(int amount = 0)
            {
                BaseOven lanternCont = lantern1 as BaseOven;
                ItemContainer container1 = lanternCont.inventory;

                if (amount == 0)
                {
                    while (container1.itemList.Count > 0)
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

                sitbox = SpawnPart(prefabbox, carpet1, false, 0, 90, 0, 0f, 0f, 0f, entity, Instance.configData.BoxSkinID);

                lantern1 = SpawnPart(prefablamp, lantern1, true, 0, 0, 0, 0f, 0.33f, 1f, entity, 1);
                lantern1.SetFlag(BaseEntity.Flags.On, false);
                carpetlock = SpawnPart(prefablock, carpetlock, true, 0, 90, 90, 0.5f, 0.3f, 0.7f, entity, 1);
                sign = SpawnPart(prefabsign, sign, true, -45, 0, 0, 0, 0.25f, 1.75f, entity, 1);

                lights1 = SpawnPart(prefablights, lights1, true, 0, 90, 0, 0.8f, 0.31f, 0.1f, entity, 1);
                lights1.SetFlag(BaseEntity.Flags.Busy, true);
                lights2 = SpawnPart(prefablights, lights2, true, 0, 90, 0, -0.9f, 0.31f, 0.1f, entity, 1);
                lights2.SetFlag(BaseEntity.Flags.Busy, true);

                if (needfuel)
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
                if (col.GetComponentInParent<BasePlayer>() != null)
                {
                    carpetantihack.Add(col.GetComponentInParent<BasePlayer>());
                }
            }

            private void OnTriggerExit(Collider col)
            {
                if (col.GetComponentInParent<BasePlayer>() != null)
                {
                    carpetantihack.Remove(col.GetComponentInParent<BasePlayer>());
                }
            }

            BasePlayer GetPilot()
            {
                player = entity.GetComponent<BaseMountable>().GetMounted() as BasePlayer;
                return player;
            }

            public void CarpetInput(InputState input, BasePlayer player)
            {
                autopilot = false;
                if (input == null) return;
                if (player == null)
                {
                    player = this.player;
                    autopilot = true;
                }
                if (autopilot)
                {
                    ResetMovement();
                    if (input.IsDown(BUTTON.FORWARD)) moveforward = true;
                    if (input.IsDown(BUTTON.BACKWARD)) movebackward = true;
                    if (input.IsDown(BUTTON.SPRINT)) throttleup = false;
                    if (input.IsDown(BUTTON.JUMP)) moveup = true;
                    if (input.IsDown(BUTTON.DUCK)) movedown = true;
                    if (input.IsDown(BUTTON.LEFT)) rotleft = true;
                    if (input.IsDown(BUTTON.RIGHT)) rotright = true;
                }
                else
                {
                    if (input.WasJustPressed(BUTTON.FORWARD)) moveforward = true;
                    if (input.WasJustReleased(BUTTON.FORWARD)) moveforward = false;
                    if (input.WasJustPressed(BUTTON.BACKWARD)) movebackward = true;
                    if (input.WasJustReleased(BUTTON.BACKWARD)) movebackward = false;
                    if (input.WasJustPressed(BUTTON.RIGHT)) rotright = true;
                    if (input.WasJustReleased(BUTTON.RIGHT)) rotright = false;
                    if (input.WasJustPressed(BUTTON.LEFT)) rotleft = true;
                    if (input.WasJustReleased(BUTTON.LEFT)) rotleft = false;
                    if (input.IsDown(BUTTON.SPRINT)) throttleup = true;
                    if (input.WasJustReleased(BUTTON.SPRINT)) throttleup = false;
                    if (input.WasJustPressed(BUTTON.JUMP)) moveup = true;
                    if (input.WasJustReleased(BUTTON.JUMP)) moveup = false;
                    if (input.WasJustPressed(BUTTON.DUCK)) movedown = true;
                    if (input.WasJustReleased(BUTTON.DUCK)) movedown = false;
                    if (input.WasJustPressed(BUTTON.RELOAD)) showmenu = true;
                    if (input.WasJustReleased(BUTTON.RELOAD)) showmenu = false;
                }
            }

            public bool FuelCheck()
            {
                if (!needfuel)
                {
                    return true;
                }
                BaseOven lantern = lantern1 as BaseOven;
                Item slot = lantern.inventory.GetSlot(0);
                if (slot == null)
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

            void Update()
            {
                if (showmenu)
                {
                    nav.paused = true;
                    Instance.ShowMenuGUI(GetPilot());
                }
                if (engineon)
                {
                    if (!GetPilot()) islanding = true;
                    var currentspeed = normalspeed;
                    if (throttleup) { currentspeed = sprintspeed; }
                    RaycastHit hit;

                    // This is a little weird.  Fortunately, some of the hooks determine fuel status...
                    if (!hasFuel)
                    {
                        if (needfuel)
                        {
                            islanding = false;
                            engineon = false;
                            return;
                        }
                    }
                    if (islanding)
                    {
                        if (!Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 3.5f, layerMask))
                        {
                            // Drop fast
                            entity.transform.localPosition += (transform.up * -15f * Time.deltaTime);
                        }
                        else
                        {
                            // Slow down
                            entity.transform.localPosition += (transform.up * -5f) * Time.deltaTime;
                        }

                        if (Physics.Raycast(new Ray(entity.transform.position, Vector3.down), out hit, 1f, layerMask))
                        {
                            islanding = false;
                            engineon = false;
                            if (pilotslist.Contains(player.userID))
                            {
                                pilotslist.Remove(player.userID);
                            }
                        }
                        ResetMovement();
                        ServerMgr.Instance.StartCoroutine(RefreshTrain());
                        return;
                    }

                    if (!autopilot)
                    {
                        // Maintain minimum height
                        if (Physics.Raycast(entity.transform.position, entity.transform.TransformDirection(Vector3.down), out hit, minaltitude, layerMask))
                        {
                            entity.transform.localPosition += transform.up * minaltitude * Time.deltaTime * 2;
                            ServerMgr.Instance.StartCoroutine(RefreshTrain());
                            return;
                        }
                        // Disallow flying forward into buildings, etc.
                        if (Physics.Raycast(entity.transform.position, entity.transform.TransformDirection(Vector3.forward), out hit, 10f, buildingMask))
                        {
                            entity.transform.localPosition += transform.forward * -5f * Time.deltaTime;
                            moveforward = false;

                            var d = Math.Round(hit.distance, 2).ToString();
                            Instance.DoLog($"FRONTAL CRASH (distance {d}m)!", true);
                        }
                        // Disallow flying backward into buildings, etc.
                        else if (Physics.Raycast(new Ray(entity.transform.position, Vector3.forward * -1f), out hit, 10f, buildingMask))
                        {
                            entity.transform.localPosition += transform.forward * 5f * Time.deltaTime;
                            movebackward = false;
                        }
                    }

                    float rotspeed = 0.1f;
                    if (throttleup) rotspeed += 0.25f;
                    if (rotright) entity.transform.eulerAngles += new Vector3(0, rotspeed, 0);
                    else if (rotleft) entity.transform.eulerAngles += new Vector3(0, -rotspeed, 0);

                    if (moveforward) entity.transform.localPosition += ((transform.forward * currentspeed) * Time.deltaTime);
                    else if (movebackward) entity.transform.localPosition = entity.transform.localPosition - ((transform.forward * currentspeed) * Time.deltaTime);

                    if (moveup) entity.transform.localPosition += ((transform.up * currentspeed) * Time.deltaTime);
                    else if (movedown) entity.transform.localPosition += ((transform.up * -currentspeed) * Time.deltaTime);

                    ServerMgr.Instance.StartCoroutine(RefreshTrain());
                }
            }

            private IEnumerator RefreshTrain()
            {
                entity.transform.hasChanged = true;
                for (int i = 0; i < entity.children.Count; i++)
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
                if (loadplayer.ContainsKey(ownerid)) loadplayer[ownerid].carpetcount = loadplayer[ownerid].carpetcount - 1;
                if (entity != null) { entity.Invoke("KillMessage", 0.1f); }
            }
        }

        public static class UI
        {
            public static CuiElementContainer Container(string panel, string color, string min, string max, bool useCursor = false, string parent = "Overlay")
            {
                CuiElementContainer container = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = {AnchorMin = min, AnchorMax = max},
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
                return container;
            }
            public static void Panel(ref CuiElementContainer container, string panel, string color, string min, string max, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    CursorEnabled = cursor
                },
                panel);
            }
            public static void Label(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = min, AnchorMax = max }
                },
                panel);

            }
            public static void Button(ref CuiElementContainer container, string panel, string color, string text, int size, string min, string max, string command, string textcolor, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = min, AnchorMax = max },
                    Text = { Text = text, FontSize = size, Align = align, Color = textcolor }
                },
                panel);
            }
            public static void Input(ref CuiElementContainer container, string panel, string color, string text, int size, string command, string min, string max)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = TextAnchor.MiddleLeft,
                            CharsLimit = 30,
                            Color = color,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text
                        },
                        new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max },
                        new CuiNeedsCursorComponent()
                    }
                });
            }
            public static void Icon(ref CuiElementContainer container, string panel, string color, string imageurl, string min, string max)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent
                        {
                            Url = imageurl,
                            Sprite = "assets/content/textures/generic/fulltransparent.tga",
                            Color = color
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = min,
                            AnchorMax = max
                        }
                    }
                });
            }
            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                {
                    hexColor = hexColor.Substring(1);
                }
                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);
                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
        #endregion
    }
}

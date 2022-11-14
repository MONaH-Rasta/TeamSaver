using System;
using System.Collections.Generic;
using System.ComponentModel;

using Newtonsoft.Json;
using Oxide.Core;
using ProtoBuf;
using UnityEngine;

using PlayerTeam = RelationshipManager.PlayerTeam;
using Pool = Facepunch.Pool;

namespace Oxide.Plugins
{
    [Info("TeamSaver", "MON@H", "1.0.0")]
    [Description("Saves and restores teams")]
    public class TeamSaver : RustPlugin
    {
        #region Fields

        private PluginConfig _pluginConfig;
        private StoredData _storedData;

        private string[] _protoPath;

        private readonly Hash<ulong, ulong> _invitedTeam = new Hash<ulong, ulong>();

        #endregion Fields

        #region Classes

        public class PluginConfig
        {
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Wipe Teams on Map Wipe")]
            public bool MapWipeTeams { get; set; }
        }

        [ProtoContract]
        private class StoredData
        {
            [ProtoMember(1, IsRequired = false)]
            public readonly List<StoredTeam> Teams = new List<StoredTeam>();

            public void SaveTeams(string[] path)
            {
                Teams.Clear();

                foreach (KeyValuePair<ulong, PlayerTeam> team in RelationshipManager.ServerInstance.teams)
                {
                    Teams.Add(StoredTeam.SaveTeam(team.Value));
                }

                ProtoStorage.Save(this, path);

                for (int index = 0; index < Teams.Count; index++)
                {
                    StoredTeam team = Teams[index];
                    Pool.Free(ref team);
                }

                Teams.Clear();
            }
        }

        [ProtoContract]
        public class StoredTeam : Pool.IPooled
        {
            [ProtoMember(1, IsRequired = false)]
            public ulong TeamID { get; set; }

            [ProtoMember(2, IsRequired = false)]
            public ulong TeamLeader { get; set; }

            [ProtoMember(3, IsRequired = false)]
            public List<ulong> Members;

            [ProtoMember(4, IsRequired = false)]
            public List<ulong> Invites;

            public static StoredTeam SaveTeam(PlayerTeam team)
            {
                StoredTeam save = Pool.Get<StoredTeam>();
                save.TeamID = team.teamID;
                save.TeamLeader = team.teamLeader;
                save.Members = save.Members ?? Pool.GetList<ulong>();
                save.Invites = save.Invites ?? Pool.GetList<ulong>();
                save.Members.AddRange(team.members);
                save.Invites.AddRange(team.invites);
                return save;
            }

            public void EnterPool()
            {
                if (Members != null)
                {
                    Pool.FreeList(ref Members);
                }

                if (Invites != null)
                {
                    Pool.FreeList(ref Invites);
                }
            }

            public void LeavePool()
            {
                TeamID = 0;
                TeamLeader = 0;
                Members = Pool.GetList<ulong>();
                Invites = Pool.GetList<ulong>();
            }
        }

        private void LoadData() => _storedData = ProtoStorage.Load<StoredData>(_protoPath) ?? new StoredData();
        private void SaveData() => ProtoStorage.Save(_storedData, _protoPath);

        #endregion Classes

        #region Initialization

        private void Init() => HooksUnsubscribe();

        protected override void LoadDefaultConfig() { }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }

        public PluginConfig AdditionalConfig(PluginConfig config)
        {
            return config;
        }

        private void OnServerInitialized()
        {
            if (IsTeamsDisabled())
            {
                PrintWarning("Teams are disabled on this server. To enable it, set server variable \"maxteamsize\" to > 0 (default 8)");
                return;
            }

            _protoPath = new[] { Name };
            LoadData();

            if (_storedData.Teams.Count != 0)
            {
                for (int index = _storedData.Teams.Count - 1; index >= 0; index--)
                {
                    StoredTeam team = _storedData.Teams[index];
                    RestoreTeam(team);
                    Pool.Free(ref team);
                }
            }

            OnServerSave();
            HooksSubscribe();
        }

        private void OnServerSave() => _storedData?.SaveTeams(_protoPath);

        private void OnNewSave()
        {
            if (_pluginConfig.MapWipeTeams)
            {
                _storedData = new StoredData();
                SaveData();
            }
        }

        private void Unload() => OnServerSave();

        #endregion Initialization

        #region Oxide Hooks

        private void OnPlayerConnected(BasePlayer player) => SendPendingInvite(player.userID);

        #endregion Oxide Hooks

        #region Core Methods

        public void RestoreTeam(StoredTeam storedTeam)
        {
            PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(storedTeam.TeamID) ?? CreateTeam(storedTeam.TeamID);
            team.teamLeader = storedTeam.TeamLeader;

            if (storedTeam.Invites != null)
            {
                team.invites.Clear();
                team.invites.AddRange(storedTeam.Invites);
            }

            if (storedTeam.Members != null)
            {
                team.members.Clear();
                team.members.AddRange(storedTeam.Members);
            }

            team.MarkDirty();

            for (int index = 0; index < team.members.Count; index++)
            {
                ulong memberId = team.members[index];
                RelationshipManager.ServerInstance.playerToTeam[memberId] = team;
                BasePlayer player = FindPlayer(memberId);
                if (player.IsValid())
                {
                    player.currentTeam = team.teamID;
                    player.SendNetworkUpdate();
                }
            }

            for (int index = 0; index < team.invites.Count; index++)
            {
                ulong inviteId = team.invites[index];
                _invitedTeam[inviteId] = team.teamID;
                SendPendingInvite(inviteId);
            }

            Interface.CallHook("OnTeamRestored", team);
        }

        public void SendPendingInvite(ulong inviteId)
        {
            ulong invitedTeam = _invitedTeam[inviteId];
            if (invitedTeam == 0)
            {
                return;
            }

            PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(invitedTeam);
            if (team == null)
            {
                return;
            }

            BasePlayer invitedPlayer = FindPlayer(inviteId);
            if (invitedPlayer.IsValid())
            {
                string leaderName = FindPlayer(team.teamLeader)?.displayName ?? covalence.Players.FindPlayerById(team.teamLeader.ToString())?.Name ?? "Unknown";
                invitedPlayer.ClientRPCPlayer(null, invitedPlayer, "CLIENT_PendingInvite", leaderName, team.teamLeader, team.teamID);
                _invitedTeam.Remove(inviteId);
            }
        }

        #endregion Core Methods

        #region Helpers

        public void HooksUnsubscribe()
        {
            Unsubscribe(nameof(OnPlayerConnected));
            Unsubscribe(nameof(OnServerSave));
        }

        public void HooksSubscribe()
        {
            Subscribe(nameof(OnPlayerConnected));
            Subscribe(nameof(OnServerSave));
        }

        public PlayerTeam CreateTeam(ulong teamId)
        {
            PlayerTeam team = Pool.Get<PlayerTeam>();
            team.teamID = teamId;
            team.teamStartTime = Time.realtimeSinceStartup;

            RelationshipManager instance = RelationshipManager.ServerInstance;
            instance.teams[teamId] = team;
            if (instance.lastTeamIndex <= teamId)
            {
                instance.lastTeamIndex = teamId + 1;
            }

            return team;
        }

        public BasePlayer FindPlayer(ulong userID) => BasePlayer.FindByID(userID) ?? BasePlayer.FindSleeping(userID);
        public bool IsTeamsDisabled() => RelationshipManager.maxTeamSize == 0;
        public void Log(string text) => LogToFile("log", $"{DateTime.Now.ToString("HH:mm:ss")} {text}", this);

        #endregion Helpers
    }
}
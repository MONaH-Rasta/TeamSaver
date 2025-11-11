using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Oxide.Core;
using ProtoBuf;

using PlayerTeam = RelationshipManager.PlayerTeam;

namespace Oxide.Plugins
{
    [Info("TeamSaver", "MON@H", "1.0.2")]
    [Description("Saves and restores teams")]
    public class TeamSaver : RustPlugin
    {
        #region Fields

        private PluginConfig _pluginConfig;
        private StoredData _storedData;

        private string[] _protoPath;

        private readonly Dictionary<ulong, ulong> _invitedTeam = new();

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
            public readonly List<StoredTeam> Teams = new();

            public void SaveTeams(string[] path)
            {
                Teams.Clear();

                foreach (KeyValuePair<ulong, PlayerTeam> team in RelationshipManager.ServerInstance.teams)
                {
                    Teams.Add(StoredTeam.SaveTeam(team.Value));
                }

                ProtoStorage.Save(this, path);
                Teams.Clear();
            }
        }

        [ProtoContract]
        public class StoredTeam
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
                StoredTeam storedTeam = new()
                {
                    TeamID = team.teamID,
                    TeamLeader = team.teamLeader
                };
                storedTeam.Members ??= new List<ulong>();
                storedTeam.Invites ??= new List<ulong>();
                storedTeam.Members.AddRange(team.members);
                storedTeam.Invites.AddRange(team.invites);
                return storedTeam;
            }
        }

        private void LoadData() => _storedData = ProtoStorage.Load<StoredData>(_protoPath) ?? new();
        private void SaveData() => ProtoStorage.Save(_storedData, _protoPath);

        #endregion Classes

        #region Initialization

        private void Init()
        {
            HooksUnsubscribe();
            _protoPath = new[] { Name };
        }

        private void OnServerInitialized()
        {
            if (IsTeamsDisabled())
            {
                PrintWarning("Teams are disabled on this server. To enable it, set server variable \"maxteamsize\" to > 0 (default 8)");
                return;
            }

            LoadData();

            if (_storedData.Teams.Count != 0)
            {
                for (int index = _storedData.Teams.Count - 1; index >= 0; index--)
                {
                    StoredTeam team = _storedData.Teams[index];
                    RestoreTeam(team);
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
                _storedData = new();
                SaveData();
            }
        }

        private void Unload() => OnServerSave();

        #endregion Initialization

        #region Configuration

        protected override void LoadDefaultConfig() { }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }

        public static PluginConfig AdditionalConfig(PluginConfig config) => config;

        #endregion Configuration

        #region Oxide Hooks

        private void OnPlayerConnected(BasePlayer player) => SendPendingInvite(player.userID);

        #endregion Oxide Hooks

        #region Core Methods

        public void RestoreTeam(StoredTeam storedTeam)
        {
            PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(storedTeam.TeamID) ?? RelationshipManager.ServerInstance.CreateTeam();
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

            Log($"{team.teamID} team was restored");
            Interface.CallHook("OnTeamRestored", team);
        }

        public void SendPendingInvite(ulong inviteId)
        {
            if (!_invitedTeam.TryGetValue(inviteId, out ulong invitedTeam))
            {
                return;
            }

            if (RelationshipManager.ServerInstance.FindTeam(invitedTeam) is not { } team)
            {
                return;
            }

            if (FindPlayer(inviteId) is { } invitedPlayer && invitedPlayer.IsValid())
            {
                string leaderName = FindPlayer(team.teamLeader)?.displayName ?? covalence.Players.FindPlayerById(team.teamLeader.ToString())?.Name ?? "Unknown";
                invitedPlayer.ClientRPC(RpcTarget.Player("CLIENT_PendingInvite", invitedPlayer), leaderName, team.teamLeader, team.teamID);

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

        public BasePlayer FindPlayer(ulong userID) => BasePlayer.FindByID(userID) ?? BasePlayer.FindSleeping(userID);
        public bool IsTeamsDisabled() => RelationshipManager.maxTeamSize == 0;
        public void Log(string text) => LogToFile("log", $"{DateTime.Now:HH:mm:ss} {text}", this);

        #endregion Helpers
    }
}
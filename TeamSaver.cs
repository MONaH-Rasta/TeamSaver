using System;

using Oxide.Core;
using System.Collections.Generic;
using ProtoBuf;
using static ProtoBuf.PlayerTeam;
using PlayerTeam = RelationshipManager.PlayerTeam;
using Pool = Facepunch.Pool;

namespace Oxide.Plugins
{
    [Info("TeamSaver", "MON@H", "1.0.0")]
    [Description("Saves and restores teams")]

    class TeamSaver : RustPlugin
    {
        private string[] _protoPath;
        
        #region Initialization
        private void Init()
        {
            HooksUnsubscribe();

            if (RelationshipManager.maxTeamSize < 1)
            {
                PrintWarning("Teams are disabled on this server. To enable it, set server variable \"maxteamsize\" to > 0 (default 8)");
                return;
            }

            _protoPath = new[] { Name };

            LoadData();
        }

        private void OnServerInitialized()
        {
            int saved = 0;

            List<ulong> playerTeams = Pool.GetList<ulong>();

            foreach (PlayerTeam playerTeam in RelationshipManager.ServerInstance.teams.Values)
            {
                playerTeams.Add(playerTeam.teamID);

                if (!_storedData.TeamsData.ContainsKey(playerTeam.teamID))
                {
                    StoredTeamSave(playerTeam);
                    saved++;
                }
            }

            if (saved > 0)
            {
                Log($"{saved} teams are saved to data file");
            }

            int restored = 0;

            foreach (KeyValuePair<ulong, StoredTeam> storedTeam in _storedData.TeamsData)
            {
                if (!playerTeams.Contains(storedTeam.Key))
                {
                    StoredTeamRestore(storedTeam.Value);
                    restored++;
                }
            }

            if (restored > 0)
            {
                Log($"{restored} teams restored from data file");
            }

            Pool.FreeList(ref playerTeams);

            HooksSubscribe();
        }

        private void OnNewSave() => ClearData();

        #endregion Initialization

        #region Stored Data

        private StoredData _storedData;

        [ProtoContract]
        private class StoredData
        {
            [ProtoMember(1, IsRequired = true)]
            public readonly Hash<ulong, StoredTeam> TeamsData = new Hash<ulong, StoredTeam>();
        }

        [ProtoContract]
        public class StoredTeam
        {
            [ProtoMember(1, IsRequired = false)]
            public ulong TeamID { get; set; }
            
            [ProtoMember(2, IsRequired = false)]
            public ulong TeamLeader { get; set; }
            
            [ProtoMember(3, IsRequired = false)]
            public readonly List<ulong> Members = new List<ulong>();
            
            [ProtoMember(4, IsRequired = false)]
            public readonly List<ulong> Invites = new List<ulong>();

            public void SetMembers(List<ulong> members)
            {
                Members.Clear();
                foreach (ulong playerID in members)
                {
                    if (!Members.Contains(playerID))
                    {
                        Members.Add(playerID);
                    }
                }
            }

            public bool MemberAdd(ulong playerID)
            {
                if (Members.Contains(playerID))
                {
                    return false;
                }

                Members.Add(playerID);
                return true;
            }

            public bool MemberRemove(ulong playerID)
            {
                if (!Members.Contains(playerID))
                {
                    return false;
                }

                Members.Remove(playerID);
                return true;
            }

            public void SetInvites(List<ulong> invites)
            {
                Invites.Clear();
                foreach (ulong playerID in invites)
                {
                    if (!Invites.Contains(playerID))
                    {
                        Invites.Add(playerID);
                    }
                }
            }

            public bool InviteAdd(ulong playerID)
            {
                if (Invites.Contains(playerID))
                {
                    return false;
                }

                Invites.Add(playerID);
                return true;
            }

            public bool InviteRemove(ulong playerID)
            {
                if (!Invites.Contains(playerID))
                {
                    return false;
                }

                Invites.Remove(playerID);
                return true;
            }
        }

        private void LoadData() => ProtoStorage.Load<StoredData>(_protoPath);

        private void ClearData()
        {
            PrintWarning("Creating a new data file");
            _storedData = new StoredData();
            SaveData();
        }

        private void SaveData() => ProtoStorage.Save(_storedData, _protoPath);

        #endregion Stored Data

        #region Oxide Hooks

        private void OnTeamCreated(BasePlayer player, PlayerTeam team) => StoredTeamSave(team);
        private void OnTeamUpdated(ulong currentTeam, PlayerTeam playerTeam, BasePlayer player) => StoredTeamSave(playerTeam);
        private void OnTeamDisbanded(PlayerTeam team)
        {
            Log($"{team.teamID} Team Disbanded");
            _storedData.TeamsData.Remove(team.teamID);
            SaveData();
        }

        private void OnTeamInvite(BasePlayer inviter, BasePlayer target)
        {
            Log($"{inviter.currentTeam} {inviter.displayName} invited {target.displayName} to his team");
            StoredTeam storedTeam = _storedData.TeamsData[inviter.currentTeam];
            if (storedTeam == null)
            {
                PrintError($"OnTeamPromote: StoredTeam not found! {inviter.currentTeam}");
                return;
            }
            if (storedTeam.InviteAdd(target.userID))
            {
                SaveData();
            }
        }

        private void OnTeamRejectInvite(BasePlayer rejector, PlayerTeam team)
        {
            Log($"{team.teamID} OnTeamRejectInvite works! {rejector}");
            StoredTeam storedTeam = _storedData.TeamsData[team.teamID];
            if (storedTeam == null)
            {
                PrintError($"{team.teamID} OnTeamPromote: StoredTeam not found!");
                return;
            }
            if (storedTeam.InviteRemove(rejector.userID))
            {
                SaveData();
            }
        }

        private void OnTeamPromote(PlayerTeam team, BasePlayer newLeader)
        {
            Log($"{team.teamID} OnTeamPromote works! {newLeader}");
            StoredTeam storedTeam = _storedData.TeamsData[team.teamID];
            if (storedTeam == null)
            {
                PrintError($"{team.teamID} OnTeamPromote: StoredTeam not found!");
                return;
            }
            storedTeam.TeamLeader = newLeader.userID;
            SaveData();
        }

        private void OnTeamLeave(PlayerTeam team, BasePlayer player)
        {
            Log($"{team.teamID} OnTeamLeave works! {player}");
            StoredTeam storedTeam = _storedData.TeamsData[team.teamID];
            if (storedTeam == null)
            {
                PrintError($"{team.teamID} OnTeamLeave: StoredTeam not found!");
                return;
            }
            if (storedTeam.MemberRemove(player.userID))
            {
                SaveData();
            }
        }

        private void OnTeamKick(PlayerTeam team, BasePlayer player, ulong target)
        {
            Log($"{team.teamID} OnTeamKick works! {player} {target}");
            StoredTeam storedTeam = _storedData.TeamsData[team.teamID];
            if (storedTeam == null)
            {
                PrintError($"{team.teamID} OnTeamKick: StoredTeam not found!");
                return;
            }
            if (storedTeam.MemberRemove(player.userID))
            {
                SaveData();
            }
        }

        private void OnTeamAcceptInvite(PlayerTeam team, BasePlayer player)
        {
            Log($"{team.teamID} OnTeamAcceptInvite works! {player}");
            StoredTeam storedTeam = _storedData.TeamsData[team.teamID];
            if (storedTeam == null)
            {
                PrintError($"{team.teamID} OnTeamKick: StoredTeam not found!");
                return;
            }
            if (storedTeam.InviteRemove(player.userID) | storedTeam.MemberAdd(player.userID))
            {
                SaveData();
            }
        }

        #endregion Oxide Hooks

        #region Core

        public void StoredTeamSave(PlayerTeam team)
        {
            Log($"{team.teamID} StoredTeamSave()");
            StoredTeam storedTeam = _storedData.TeamsData[team.teamID];
            if (storedTeam == null)
            {
                storedTeam = new StoredTeam();
                storedTeam.TeamID = team.teamID;
                _storedData.TeamsData[team.teamID] = storedTeam;
                SaveData();
            }
            storedTeam.TeamLeader = team.teamLeader;
            storedTeam.SetMembers(team.members);
            storedTeam.SetInvites(team.invites);
            SaveData();
        }

        public void StoredTeamRestore(StoredTeam storedTeam)
        {
            BasePlayer teamLeader = FindPlayer(storedTeam.TeamLeader);
            if (!teamLeader.IsValid())
            {
                PrintError($"Can't find player {storedTeam.TeamLeader} teamLeader of a team {storedTeam.TeamID}");
                return;
            }

            PlayerTeam playerTeam = RelationshipManager.ServerInstance.CreateTeam();
            playerTeam.teamLeader = storedTeam.TeamLeader;
            playerTeam.invites = storedTeam.Invites;
            playerTeam.members = storedTeam.Members;
            playerTeam.MarkDirty();

            foreach (ulong playerID in storedTeam.Members)
            {
                RelationshipManager.ServerInstance.playerToTeam[playerID] = playerTeam;
                BasePlayer player = FindPlayer(playerID);
                if (!player.IsValid())
                {
                    PrintError($"Can't find player {playerID} while restoring team members of a team {playerTeam.teamID}");
                    continue;
                }
                player.currentTeam = playerTeam.teamID;
                player.SendNetworkUpdate();
            }

            foreach (ulong invitedPlayerID in storedTeam.Invites)
            {
                BasePlayer player = FindPlayer(invitedPlayerID);
                if (!player.IsValid())
                {
                    PrintError($"Can't find player {invitedPlayerID} while restoring invites of a team {playerTeam.teamID}");
                    continue;
                }
                player.ClientRPCPlayer(null, player, "CLIENT_PendingInvite", teamLeader.displayName, playerTeam.teamLeader, playerTeam.teamID);
            }

            Log($"Saved team {storedTeam.TeamID} restored to new team {playerTeam.teamID}");
            _storedData.TeamsData.Remove(storedTeam.TeamID);
            StoredTeamSave(playerTeam);
        }

        #endregion Core

        #region Helpers

        private void HooksUnsubscribe()
        {
            Unsubscribe(nameof(OnTeamAcceptInvite));
            Unsubscribe(nameof(OnTeamCreated));
            Unsubscribe(nameof(OnTeamDisbanded));
            Unsubscribe(nameof(OnTeamInvite));
            Unsubscribe(nameof(OnTeamKick));
            Unsubscribe(nameof(OnTeamLeave));
            Unsubscribe(nameof(OnTeamPromote));
            Unsubscribe(nameof(OnTeamRejectInvite));
            Unsubscribe(nameof(OnTeamUpdated));
        }

        private void HooksSubscribe()
        {
            Subscribe(nameof(OnTeamAcceptInvite));
            Subscribe(nameof(OnTeamCreated));
            Subscribe(nameof(OnTeamDisbanded));
            Subscribe(nameof(OnTeamInvite));
            Subscribe(nameof(OnTeamKick));
            Subscribe(nameof(OnTeamLeave));
            Subscribe(nameof(OnTeamPromote));
            Subscribe(nameof(OnTeamRejectInvite));
            Subscribe(nameof(OnTeamUpdated));
        }

        private BasePlayer FindPlayer(ulong userID)
        {
            BasePlayer player = BasePlayer.FindByID(userID);
            if (player == null)
            {
                player = BasePlayer.FindAwakeOrSleeping(userID.ToString());
            }

            return player;
        }

        private void Log(string text)
        {
            LogToFile("log", $"{DateTime.Now.ToString("HH:mm:ss")} {text}", this);
        }

        #endregion Helpers
    }
}
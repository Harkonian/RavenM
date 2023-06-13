using BepInEx;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RavenM.Lobby
{
    public static class Notifications
    {
        public const string LobbyClosed = "Lobby closed by host.";

        public const string PlayerKicked = "You were kicked from the lobby! You can no longer join this lobby.";

        public const string JoiningError = "Unknown error joining lobby. (Does it still exist?)";

        public const string PluginMismatch = "You cannot join this lobby because you and the host are using different versions of RavenM.";

        public const string HotjoinDisabled = "This lobby has already started a match and has disabled mid-game joining or is playing a gamemode that does not support it.";

        public const string UserCancelled = "Cancelled loading server mods. Leaving lobby.";

        public const string HotJoinDisabled = "This lobby has already started a match and has disabled mid-game joining or is playing a gamemode that does not support it.";
    }

    public class LobbyGUI
    {
        private static class ColorBank
        {
            public static Color DebugColor = Color.magenta; // If we see magenta anywhere something has gone wrong.
            public static Color NotLoaded = Color.red;
            public static Color FullyLoaded = Color.green;
            public static Color WarningColor = new Color(1.0f, .75f, 0.0f);

            public static string ConvertToOpenTag(Color color)
            {
                return ConvertToOpenTag($"#{ColorUtility.ToHtmlStringRGB(color)}");
            }

            // Takes in strings that match a color name (red, yellow, etc) or takes a set of three hex codes prepended by a # (#FFFFFF, #777777)
            public static string ConvertToOpenTag(string color)
            {
                return $"<color={color}>";
            }

            public const string CloseTag = "</color>";

            public static string CreateColoredLabelString(string text, Color color)
            {
                return CreateColoredLabelString(text, $"#{ColorUtility.ToHtmlStringRGB(color)}");
            }

            // Takes in strings that match a color name (red, yellow, etc) or takes a set of three hex codes prepended by a # (#FFFFFF, #777777)
            public static string CreateColoredLabelString(string text, string color)
            {
                return $"{ColorBank.ConvertToOpenTag(color)}{text}{ColorBank.CloseTag}";
            }

            static ColorBank()
            {
                FullyLoaded *= .75f;
            }
        }

        public static Texture2D LobbyBackground = null;
        public static Texture2D ProgressTexture = null;
        public string LobbyMemberCapString = string.Empty;
        public string DirectJoinLobbyString = string.Empty;
        public CSteamID SelectedLobbyID = CSteamID.Nil;

        // Either the Listing the user has selected in the Browse menu or the settings the host is messing with.
        public ServerSettings CurrentServerSettings = null;

        // Catch all text that can be set to indicate certain errors to the user.
        public string NotificationText = string.Empty;

        // If set to non-nill represents a user the host is attempting to kick and is being asked if they are sure they want to kick them.
        public CSteamID KickPrompt = CSteamID.Nil;

        public static string BackButtonLabel = ColorBank.CreateColoredLabelString("BACK", "#888888");

        private enum MenuState
        {
            Main,
            Host,
            Join,
            DirectConnect,
            Browse,
            ViewLobbyDetails,
            SubscriptionWarning
        }

        private Stack<MenuState> GUIStack = new Stack<MenuState>();

        private struct TeamDisplayData
        {
            // E or R or X in an error
            public string TeamDisplayString;
            public Color Color;
            public TeamDisplayData(string teamDisplayString, Color color)
            {
                TeamDisplayString = teamDisplayString;
                Color = color;
            }
        }

        private static List<TeamDisplayData> teamData = new List<TeamDisplayData> {
            new TeamDisplayData("X", ColorBank.DebugColor),
            new TeamDisplayData("E", Color.blue),
            new TeamDisplayData("R", Color.red)
        };

        private static TeamDisplayData GetFromTeamIndex(int teamIndex) 
        {
            if (teamIndex < -1 || teamIndex >= teamData.Count - 1)
                teamIndex = -1;

            return teamData[teamIndex + 1];
        }

        static LobbyGUI()
        {
            LobbyBackground = CreateSolidTexture(Color.black);
            ProgressTexture = CreateSolidTexture(Color.green);
        }

        public LobbyGUI()
        {
            GUIStack.Push(MenuState.Main);
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.black);
            tex.Apply();
            return tex;
        }

        private delegate void HorizontalContentDelegate();

        private void CreateHorizontalGUISection ( HorizontalContentDelegate content )
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            content.Invoke();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void CreateLabelSection(string label)
        {
            CreateHorizontalGUISection(() => GUILayout.Label(label));
        }

        private void CreateLabelSection(string label, Color textColor)
        {
            CreateLabelSection(ColorBank.CreateColoredLabelString(label, textColor));
        }

        private void DrawMainMenu(LobbySystem system)
        {
            if (CurrentServerSettings != null)
                CurrentServerSettings = null;

            CreateLabelSection("RavenM");

            GUILayout.Space(15f);

            if (GUILayout.Button("HOST"))
                GUIStack.Push(MenuState.Host);

            GUILayout.Space(5f);

            if (GUILayout.Button("JOIN"))
                GUIStack.Push(MenuState.Join);
        }

        private void DrawHostMenu(LobbySystem system)
        {
            if (CurrentServerSettings == null)
            {
                CurrentServerSettings = new ServerSettings();
                LobbyMemberCapString = CurrentServerSettings.LobbyMemberCap.ToString();
            }

            CreateLabelSection("HOST");

            GUILayout.Space(5f);

            CreateHorizontalGUISection(() =>
            {
                // TODO: Consider coloring this red when values are not valid (< min or > max) rather than just altering the field's
                // data to force it to be valid like we do currently.
                GUILayout.Label($"MEMBER LIMIT: ");
                LobbyMemberCapString = GUILayout.TextField(LobbyMemberCapString, 3);
            });

            // if the field is empty don't ding that against the user right away.
            if (!LobbyMemberCapString.IsNullOrWhiteSpace())
            {
                uint parsedVal;

                // Ensure we are working with a valid uint.
                // Specifically only changing this when above the limit because we don't want the user's text to be reset if they
                // clear the text field and then insert 1 as the start of 18 or 100 or similar.
                if (!uint.TryParse(LobbyMemberCapString, out parsedVal) || parsedVal > ServerSettings.LobbyMemberMax)
                {
                    LobbyMemberCapString = CurrentServerSettings.LobbyMemberCap.ToString();
                }
                else
                {
                    CurrentServerSettings.LobbyMemberCap = parsedVal;
                }
            }

            CurrentServerSettings.FriendsOnlyLobby = GUILayout.Toggle(CurrentServerSettings.FriendsOnlyLobby, "FRIENDS ONLY");
            CurrentServerSettings.IncludeInBrowseList = GUILayout.Toggle(CurrentServerSettings.IncludeInBrowseList, "SHOW ON LOBBY\nLIST");
            CurrentServerSettings.MidgameJoin = GUILayout.Toggle(CurrentServerSettings.MidgameJoin, "JOINABLE\nMIDGAME");
            CurrentServerSettings.NameTagsEnabled = GUILayout.Toggle(CurrentServerSettings.NameTagsEnabled, "NAMETAGS");
            CurrentServerSettings.TeamOnlyNameTags = GUILayout.Toggle(CurrentServerSettings.TeamOnlyNameTags, "FOR TEAM ONLY");

            GUILayout.Space(10f);

            if (GUILayout.Button("START"))
            {
                // No friends?
                if (LobbyMemberCapString.IsNullOrWhiteSpace())
                    CurrentServerSettings.LobbyMemberCap = 2;

                system.HostLobby(CurrentServerSettings);
            }

            GUILayout.Space(3f);

            if (GUILayout.Button(BackButtonLabel))
                GUIStack.Pop();
        }

        private void DrawJoinMenu(LobbySystem system)
        {
            CreateLabelSection("JOIN");

            GUILayout.Space(10f);

            if (GUILayout.Button("BROWSE"))
            {
                system.RefreshOpenLobbies();
                GUIStack.Push(MenuState.Browse);
            }

            GUILayout.Space(5f);

            if (GUILayout.Button("DIRECT CONNECT"))
                GUIStack.Push(MenuState.DirectConnect);

            GUILayout.Space(3f);

            if (GUILayout.Button(BackButtonLabel))
                GUIStack.Pop();
        }

        private void DrawDirectConnectMenu(LobbySystem system)
        {
            CreateLabelSection("DIRECT CONNECT");

            GUILayout.Space(10f);

            CreateLabelSection("LOBBY ID");

            DirectJoinLobbyString = GUILayout.TextField(DirectJoinLobbyString);

            GUILayout.Space(10f);

            DrawSubscriptionMatchToggle(system);

            GUILayout.Space(15f);

            if (GUILayout.Button("START"))
            {
                if (uint.TryParse(DirectJoinLobbyString, out uint lobbyId))
                {
                    system.AttemptToJoinLobby(lobbyId);
                }
            }

            GUILayout.Space(3f);

            if (GUILayout.Button(BackButtonLabel))
                GUIStack.Pop();
        }

        private void DrawBrowseMenu(LobbySystem system)
        {
            CreateLabelSection("BROWSE");

            GUILayout.Space(10f);

            if (GUILayout.Button("REFRESH"))
            {
                system.RefreshOpenLobbies();
            }

            GUILayout.Space(10f);

            CreateLabelSection($"LOBBIES - ({system.OpenLobbies.Count()})");

            foreach (var lobbyKVP in system.OpenLobbies)
            {
                var lobbyID = lobbyKVP.Key;
                var serverSettings = lobbyKVP.Value;

                bool hasData = false;
                string lobbyName = ColorBank.CreateColoredLabelString("Loading...", "#777777");
                if (serverSettings != null)
                {
                    var ownerId = new CSteamID(serverSettings.OwnerID);
                    hasData = !SteamFriends.RequestUserInformation(ownerId, true);
                    if (hasData)
                    {
                        lobbyName = SteamFriends.GetFriendPersonaName(ownerId);
                        if (lobbyName.Length > 10)
                        {
                            lobbyName = lobbyName.Substring(0, 10) + "...";
                        }

                        // TODO: LOBBY-COUNTS Does this really need to be queried every frame?
                        lobbyName += $" - ({SteamMatchmaking.GetNumLobbyMembers(lobbyID)}/{SteamMatchmaking.GetLobbyMemberLimit(lobbyID)})";
                    }
                }

                if (GUILayout.Button($"{lobbyName}") && hasData)
                {
                    SelectedLobbyID = lobbyID;
                    CurrentServerSettings = serverSettings;
                    GUIStack.Push(MenuState.ViewLobbyDetails);
                }
            }

            GUILayout.Space(3f);

            if (GUILayout.Button(BackButtonLabel))
                GUIStack.Pop();
        }

        private void DrawViewLobbyDetailsMenu(LobbySystem system)
        {
            if (CurrentServerSettings == null)
            {
                Plugin.logger.LogError("Managed to view a lobby via browse without first having fully loaded the lobby listing. Attempting to retrieve data now.");
                if (SteamLobbyDataTransfer.ImportFromLobbyData(SelectedLobbyID, out CurrentServerSettings))
                {
                    if (CurrentServerSettings == null)
                    {
                        string errMessage = $"Could not retrieve lobby listing data for lobby {SelectedLobbyID}. Returning to browse.";
                        NotificationText = errMessage;
                        Plugin.logger.LogError(errMessage);
                        GUIStack.Pop();
                    }
                }
            }

            CSteamID ownerID = new CSteamID(CurrentServerSettings.OwnerID);

            string name = SteamFriends.GetFriendPersonaName(ownerID);

            CreateLabelSection($"{name}'s");
            CreateLabelSection("LOBBY");

            GUILayout.Space(10f);

            if (GUILayout.Button("REFRESH"))
            {
                SteamMatchmaking.RequestLobbyData(SelectedLobbyID);
            }

            GUILayout.Space(10f);

            // TODO: LOBBY-COUNTS Same as above, should we cache this and only query every few seconds rather than every frame?
            GUILayout.Label($"MEMBERS: {SteamMatchmaking.GetNumLobbyMembers(SelectedLobbyID)}/{SteamMatchmaking.GetLobbyMemberLimit(SelectedLobbyID)}");

            var modList = SteamMatchmaking.GetLobbyData(SelectedLobbyID, "mods");
            var modCount = modList != string.Empty ? modList.Split(',').Length : 0;
            GUILayout.Label($"MODS: {modCount}");
            MatchSettings matchSettings = null;

            if (!SteamLobbyDataTransfer.ImportFromLobbyData(SelectedLobbyID, out matchSettings) || Plugin.BuildGUID != CurrentServerSettings.BuildID)
            {
                GUILayout.Label(ColorBank.CreateColoredLabelString("This lobby is running on a different version of RavenM!", Color.red));
            }
            else
            {
                GUILayout.Label($"BOTS: {matchSettings.BotNumberText}");
                GUILayout.Label($"MAP: {matchSettings.SelectedMapName}");

                var status = system.MatchHasStarted() ? ColorBank.CreateColoredLabelString("In-game", ColorBank.FullyLoaded) : "Configuring";
                GUILayout.Label($"STATUS: {status}");

                GUILayout.Space(10f);

                if (Plugin.BuildGUID != CurrentServerSettings.BuildID)
                {
                    GUILayout.Label(ColorBank.CreateColoredLabelString("This lobby is running on a different version of RavenM!", Color.red));
                }
                else
                {
                    if (GUILayout.Button("JOIN"))
                    {
                        system.AttemptToJoinLobby(SelectedLobbyID);
                    }

                    DrawSubscriptionMatchToggle(system);
                }

                GUILayout.Space(3f);
            }

            if (GUILayout.Button(BackButtonLabel))
                GUIStack.Pop();
        }

        private void DrawSubscriptionMatchToggle(LobbySystem system)
        {
            bool tempServerSubscriptionCheck = GUILayout.Toggle(system.MatchSubscriptionsToServer, "Force Workshop Subs");

            if (tempServerSubscriptionCheck != system.MatchSubscriptionsToServer)
            {
                if (tempServerSubscriptionCheck)
                    GUIStack.Push(MenuState.SubscriptionWarning);
                else
                    system.MatchSubscriptionsToServer = tempServerSubscriptionCheck;
            }
        }
        
        private Rect CreateCenteredRect(float width, float height)
        {
            return new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        }

        private void DrawSubscriptionWarningMenu(LobbySystem system)
        {
            // TODO: This is really janky to close out the area that is normally displayed for other menu states.
            GUILayout.EndVertical();
            GUILayout.EndArea();

            // Shamelessly copy pasted from the notification display.
            // TODO: Lose the duplication of code between this and the notification display.
            var lobbyStyle = new GUIStyle(GUI.skin.box);
            lobbyStyle.normal.background = LobbyGUI.LobbyBackground;

            float width = Mathf.Max(Screen.width / 4f, 500f);
            float height = Mathf.Max(Screen.height / 3f, 500f);
            GUILayout.BeginArea(CreateCenteredRect(width, height), string.Empty);
            GUILayout.BeginVertical(lobbyStyle);

            CreateLabelSection("Warning", ColorBank.WarningColor);

            GUILayout.Space(7f);

            CreateLabelSection("Enabling this option will change what workshop item subscriptions you have to line up with the server you join. This option is designed to allow you to sync up to a friend's workshop subscriptions after which there should be less loading when joining in to lobbies they host (until they change their subscriptions anyway). Once you have joined a server there is no way to automatically revert your subscriptions after leaving. If all you want is to match up with a server until you leave that server please click cancel here and do not enable this option.");
            CreateLabelSection("Are you sure you want to enable Server Workshop Subscription Matching?", ColorBank.WarningColor);

            GUILayout.Space(15f);

            CreateHorizontalGUISection(() =>
            {
                if (GUILayout.Button("Cancel"))
                    this.GUIStack.Pop();
                if (GUILayout.Button(ColorBank.CreateColoredLabelString("Confirm", ColorBank.WarningColor)))
                {
                    system.MatchSubscriptionsToServer = true;
                    this.GUIStack.Pop();
                }
            });

            // Purposefully not ending the area or vertical here so that the closing statements for the area we closed at the top of this function has something to close.
            // GUILayout.EndVertical();
            // GUILayout.EndArea();
        }

        public void DrawLobbyGui (LobbySystem system)
        {
            if (GameManager.instance == null || (GameManager.IsIngame() && LoadoutUi.instance != null && LoadoutUi.HasBeenClosed()))
                return;

            var menu_page = (int)typeof(MainMenu).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MainMenu.instance);

            if (menu_page != MainMenu.PAGE_INSTANT_ACTION)
                return;

            var lobbyStyle = new GUIStyle(GUI.skin.box);
            lobbyStyle.normal.background = LobbyGUI.LobbyBackground;

            if (GameManager.IsInMainMenu() && NotificationText != string.Empty)
            {
                GUILayout.BeginArea(new Rect((Screen.width - 250f) / 2f, (Screen.height - 200f) / 2f, 250f, 200f), string.Empty);
                GUILayout.BeginVertical(lobbyStyle);

                CreateLabelSection("RavenM Message:", Color.red);

                GUILayout.Space(7f);

                CreateLabelSection(NotificationText);

                GUILayout.Space(15f);

                CreateHorizontalGUISection(() =>
                {
                    if (GUILayout.Button("OK"))
                        NotificationText = string.Empty;
                });

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }


            DebugLoggingCache.ExportToLog(new DebugLoggingCache.SimpleMessageHolder($"In Lobby : {system.InLobby}"));

            if (!system.InLobby && GameManager.IsInMainMenu())
            {
                GUILayout.BeginArea(new Rect(10f, 10f, 150f, 10000f), string.Empty);
                GUILayout.BeginVertical(lobbyStyle);

                // TODO: Defualt case?
                switch (GUIStack.Peek())
                {
                    case MenuState.Main:
                        DrawMainMenu(system);
                        break;
                    case MenuState.Host:
                        DrawHostMenu(system);
                        break;
                    case MenuState.Join:
                        DrawJoinMenu(system);
                        break;
                    case MenuState.DirectConnect:
                        DrawDirectConnectMenu(system);
                        break;
                    case MenuState.Browse:
                        DrawBrowseMenu(system);
                        break;
                    case MenuState.ViewLobbyDetails:
                        DrawViewLobbyDetailsMenu(system);
                        break;
                    case MenuState.SubscriptionWarning:
                        DrawSubscriptionWarningMenu(system);
                        break;
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }

            DebugLoggingCache.ExportToLog(new DebugLoggingCache.SimpleMessageHolder($"LobbyDataReady : {system.LobbyDataReady}"));

            if (system.InLobby && system.LobbyDataReady)
            {
                GUILayout.BeginArea(new Rect(10f, 10f, 150f, 10000f), string.Empty);
                GUILayout.BeginVertical(lobbyStyle);

                var members = system.GetLobbyMembers();
                int len = members.Count;

                if (GameManager.IsInMainMenu() && GUILayout.Button(ColorBank.CreateColoredLabelString("LEAVE", Color.red)))
                {
                    system.BeginLeavingLobby();
                }

                CreateLabelSection($"LOBBY - {len}/{SteamMatchmaking.GetLobbyMemberLimit(system.LobbyID)}");

                GUILayout.Space(5f);

                CreateLabelSection(system.LobbyID.GetAccountID().ToString());

                if (GameManager.IsInMainMenu() && GUILayout.Button("COPY ID"))
                {
                    GUIUtility.systemCopyBuffer = system.LobbyID.GetAccountID().ToString();
                }

                GUILayout.Space(15f);

                CreateLabelSection("MEMBERS:");

                for (int i = 0; i < len; i++)
                {
                    var memberId = members[i];
                    CreateMemberDisplay(system, memberId);
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }

            DebugLoggingCache.ExportToLog(new DebugLoggingCache.SimpleMessageHolder($"ModsToDownload : {system.ModsToDownload.Count}"));

            if (system.ModsToDownload.Count > 0)
            {
                GUILayout.BeginArea(new Rect(160f, 10f, 150f, 10000f), string.Empty);
                GUILayout.BeginVertical(lobbyStyle);

                int hasDownloaded = system.ServerMods.Count - system.ModsToDownload.Count;

                CreateLabelSection("DOWNLOADING");
                CreateLabelSection("MODS:");

                GUILayout.Space(5f);

                CreateLabelSection($"{hasDownloaded}/{system.ServerMods.Count}");

                if (SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(system.ModsToDownload[0].m_PublishedFileId), out ulong punBytesDownloaded, out ulong punBytesTotal))
                {
                    GUILayout.Space(5f);

                    CreateLabelSection($"{punBytesDownloaded / 1024}KB/{punBytesTotal / 1024}KB");

                    GUILayout.Space(5f);

                    GUIStyle progressStyle = new GUIStyle();
                    progressStyle.normal.background = LobbyGUI.ProgressTexture;

                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(progressStyle);
                    GUILayout.Box(LobbyGUI.ProgressTexture);
                    GUILayout.EndVertical();
                    GUILayout.Space((float)(punBytesTotal - punBytesDownloaded) / punBytesTotal * 150f);
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(15f);

                if (GUILayout.Button(ColorBank.CreateColoredLabelString("CANCEL", Color.red)))
                {
                    if (system.InLobby)
                    {
                        system.BeginLeavingLobby(Notifications.UserCancelled);
                    }
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        private void CreateMemberDisplay(LobbySystem system, CSteamID memberId)
        {
            string name = SteamFriends.GetFriendPersonaName(memberId);

            LobbyMemberData memberData = null;
            if (!SteamLobbyDataTransfer.ImportFromMemberData(system.LobbyID, memberId, out memberData))
            {
                Plugin.logger.LogInfo($"Failed to import lobby member data for {name}");
                memberData = null;
            }

            int teamIndex = -1; // Error Team
            Color readyColor = ColorBank.DebugColor;

            if (memberData != null)
            {
                teamIndex = memberData.Team;
                if (memberData.ServerModsNeeded > 0)
                {
                    float downloadPercent = .75f * (float)(LobbySystem.instance.ServerMods.Count - memberData.ServerModsNeeded) / LobbySystem.instance.ServerMods.Count;
                    readyColor = Color.Lerp(ColorBank.NotLoaded, ColorBank.FullyLoaded, downloadPercent);
                }
                else if ((GameManager.IsInMainMenu() && memberData.Loaded) || memberData.Ready)
                {
                    readyColor = ColorBank.FullyLoaded;
                }
                else
                {
                    readyColor = ColorBank.NotLoaded;
                }
            }

            if (memberId != KickPrompt)
            {
                TeamDisplayData teamData = GetFromTeamIndex(teamIndex);
                string teamColorString = ColorBank.CreateColoredLabelString(teamData.TeamDisplayString, teamData.Color);
                GUILayout.BeginHorizontal();
                GUILayout.Box(teamColorString);
                GUILayout.FlexibleSpace();
                GUILayout.Box(ColorBank.CreateColoredLabelString(name, readyColor));
                GUILayout.FlexibleSpace();
                GUILayout.Box(teamColorString);
                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.Repaint
                    && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)
                    && Input.GetMouseButtonUp(1)
                    && system.IsLobbyOwner
                    && memberId != SteamUser.GetSteamID())
                {
                    KickPrompt = memberId;
                }
            }
            else
            {
                if (GUILayout.Button(ColorBank.CreateColoredLabelString($"KICK {name}", Color.red)))
                {
                    system.KickUser(memberId);
                }

                if (Event.current.type == EventType.Repaint
                    && !GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)
                    && Input.GetMouseButtonDown(0) || Input.GetMouseButton(1))
                    KickPrompt = CSteamID.Nil;
            }
        }
    }
}

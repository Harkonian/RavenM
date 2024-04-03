using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Steamworks;
using UnityEngine;

namespace RavenM.Lobby;

public static class Notifications
{
    public const string LobbyClosed = "Lobby closed by host.";

    public const string PlayerKicked = "You were kicked from the lobby! You can no longer join this lobby.";

    public const string JoiningError = "Unknown error joining lobby. (Does it still exist?)";

    public const string PluginMismatch = "You cannot join this lobby because you and the host are using different versions of RavenM.";

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
        public static Color WarningColor = new(1.0f, .75f, 0.0f);

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

    

    // TODO: Data from "ServerSettings" from previous work. This definitely should not be here.
    private const uint LobbyMemberMax = 250; // Steam lobby max. Is there no way to get this from the Steam apis themselves rather than magic numbering this?


    public static Texture2D LobbyBackground = null;
    public static Texture2D ProgressTexture = null;
    public string LobbyMemberCapString = string.Empty;
    public string DirectJoinLobbyString = string.Empty;
    public CSteamID SelectedLobbyID = CSteamID.Nil;

    // Either the listing the user has selected in the Browse menu or the settings the host is messing with.
    public FixedServerSettings CurrentServerListing = null;

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
        ViewLobbyDetails
    }

    private readonly Stack<MenuState> GUIStack = new();

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

    private static readonly List<TeamDisplayData> teamData = new()
    {
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
        Texture2D tex = new(1, 1);
        tex.SetPixel(0, 0, color);
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
        if (CurrentServerListing != null)
            CurrentServerListing = null; // We're on the main menu, there is no current listing we are looking at, whether it was ours or someone else's

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
        if (CurrentServerListing == null) 
        {
            CurrentServerListing = new FixedServerSettings();
            LobbyMemberCapString = CurrentServerListing.LobbyMemberCap.ToString();
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
        if (!string.IsNullOrWhiteSpace(LobbyMemberCapString))
        {
            // Ensure we are working with a valid uint.
            // Specifically only changing this when above the limit because we don't want the user's text to be reset if they
            // clear the text field and then insert 1 as the start of 18 or 100 or similar.
            if (!uint.TryParse(LobbyMemberCapString, out uint parsedVal) || parsedVal > FixedServerSettings.LobbyMemberMax)
            {
                LobbyMemberCapString = CurrentServerListing.LobbyMemberCap.ToString();
            }
            else
            {
                CurrentServerListing.LobbyMemberCap = parsedVal;
            }
        }

        // TODO: We're doing this same pattern a lot here. Sure it's simple but probably still best to make a helper for it.
        CurrentServerListing.FriendsOnlyLobby = GUILayout.Toggle(CurrentServerListing.FriendsOnlyLobby, "FRIENDS ONLY");
        CurrentServerListing.IncludeInBrowseList = GUILayout.Toggle(CurrentServerListing.IncludeInBrowseList, "SHOW ON LOBBY\nLIST");
        CurrentServerListing.MidgameJoin = GUILayout.Toggle(CurrentServerListing.MidgameJoin, "JOINABLE\nMIDGAME");

        GUILayout.Space(7f);
        CreateLabelSection("NAMETAGS");
        CurrentServerListing.NameTagsEnabled = GUILayout.Toggle(CurrentServerListing.NameTagsEnabled, "ENABLED");
        CurrentServerListing.TeamOnlyNameTags = GUILayout.Toggle(CurrentServerListing.TeamOnlyNameTags, "FOR TEAM ONLY");

        GUILayout.Space(10f);

        if (GUILayout.Button("START"))
        {
            // No friends?
            if (string.IsNullOrWhiteSpace(LobbyMemberCapString))
                CurrentServerListing.LobbyMemberCap = FixedServerSettings.LobbyMemberMin;

            system.HostLobby(CurrentServerListing);
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

        foreach (var keyValuePair in system.OpenLobbies)
        {
            CSteamID lobbyID = keyValuePair.Key;
            FixedServerSettings serverSettings = keyValuePair.Value;


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
                CurrentServerListing = serverSettings;
                GUIStack.Push(MenuState.ViewLobbyDetails);
            }
        }

        GUILayout.Space(3f);

        if (GUILayout.Button(BackButtonLabel))
            GUIStack.Pop();
    }

    private void DrawViewLobbyDetailsMenu(LobbySystem system)
    {
        if (CurrentServerListing == null)
        {
            Plugin.logger.LogError("Managed to view a lobby via browse without first having fully loaded the lobby listing. Attempting to retrieve data now.");
            if (!SteamLobbyDataTransfer.ImportFromLobbyData(SelectedLobbyID, out CurrentServerListing))
            {
                string errMessage = $"Could not retrieve lobby listing data for lobby {SelectedLobbyID}. Returning to browse.";
                NotificationText = errMessage;
                Plugin.logger.LogError(errMessage);
                GUIStack.Pop();
            }
        }

        CSteamID ownerID = new(CurrentServerListing.OwnerID);

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

        GUILayout.Space(10f);

        if (!SteamLobbyDataTransfer.ImportFromLobbyData(SelectedLobbyID, out MatchListingInfo matchListing) || Plugin.BuildGUID != CurrentServerListing.BuildID)
        {
            GUILayout.Label(ColorBank.CreateColoredLabelString("This lobby is running on a different version of RavenM!", Color.red));
        }                
        else
        {
            GUILayout.Label($"BOTS: {matchListing.BotNumberText}");
            GUILayout.Label($"MAP: {matchListing.SelectedMapName}");

            var status = matchListing.MatchStarted ? ColorBank.CreateColoredLabelString("In-game", ColorBank.FullyLoaded) : "Configuring";
            GUILayout.Label($"STATUS: {status}");

            if (GUILayout.Button("JOIN"))
            {
                system.AttemptToJoinLobby(SelectedLobbyID);
            }
        }

        GUILayout.Space(3f);

        if (GUILayout.Button(BackButtonLabel))
            GUIStack.Pop();
    }
    
    private Rect CreateCenteredRect(float width, float height)
    {
        return new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
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

        if (GameManager.IsInMainMenu() && system.IntentionToStart)
        {
            // TODO: Refactor the three notification/warning/confirmation menus.
            GUILayout.BeginArea(new Rect((Screen.width - 250f) / 2f, (Screen.height - 200f) / 2f, 250f, 200f), string.Empty);
            GUILayout.BeginVertical(lobbyStyle);

            CreateLabelSection("RavenM WARNING:", Color.red);
            GUILayout.Space(7f);

            CreateLabelSection("Starting the match before all members have loaded is experimental and may cause inconsistencies.Are you sure?");
            GUILayout.Space(15f);

            CreateHorizontalGUISection(() =>
            {
                if (GUILayout.Button(ColorBank.CreateColoredLabelString("CONTINUE", ColorBank.FullyLoaded)))
                {
                    system.HasCommittedToStart = true;
                    system.IntentionToStart = false;
                    InstantActionMaps.instance.StartGame();
                }
                if (GUILayout.Button("ABORT"))
                {
                    system.IntentionToStart = false;
                }
            });

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

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
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }        

        // Plugin.logger.LogInfo($"In Lobby : {system.InLobby}");
        // Plugin.logger.LogInfo($"LobbyDataReady : {system.LobbyDataReady}");

        if (system.InLobby && system.LobbyDataReady)
        {
            if (!IngameNetManager.instance.IsClient)
            {
                // TODO: These magic numbers feel odd They may work as an okay baseline but some amount of scaling based on screen size would probably do better.
                if (ChatManager.instance.SelectedChatPosition == 1) // Position to the right
                {
                    ChatManager.instance.CreateChatArea(true, 300f, 400f, 570f, Screen.width - 310f);
                }
                else
                {
                    ChatManager.instance.CreateChatArea(true, 300f, 400f, 570f);
                }
            }

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

            if (!system.IsLobbyOwner)
            {
                GUILayout.Space(5f);
                if (GUILayout.Button(ColorBank.CreateColoredLabelString("MATCH STEAM SUBSCRIBTIONS", Color.yellow)))
                {
                    system.MatchSteamModSubscriptions();
                }

                GUILayout.Space(5f);
                GUILayout.Space(15f);
            }


            if (GUILayout.Button(ColorBank.CreateColoredLabelString("TEST", Color.cyan)))
            {
                TestFunction(system);
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


        if (system.ModsToDownload.Count > 0)
        {
            Plugin.logger.LogInfo($"ModsToDownload : {system.ModsToDownload.Count}");
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

                GUIStyle progressStyle = new();
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
        string team = SteamMatchmaking.GetLobbyMemberData(system.LobbyID, memberId, "team");

        string modsDownloaded = SteamMatchmaking.GetLobbyMemberData(system.LobbyID, memberId, "modsDownloaded");
        // Can't use ServerMods.Count for the lobby owner.
        string totalMods = SteamMatchmaking.GetLobbyData(system.LobbyID, "mods").Split(',').Length.ToString();
        var readyColor = (GameManager.IsInMainMenu() ? SteamMatchmaking.GetLobbyMemberData(system.LobbyID, memberId, "loaded") == "yes" 
                                                        : SteamMatchmaking.GetLobbyMemberData(system.LobbyID, memberId, "ready") == "yes") 
                                                        ? "green" : "red";

        if (memberId != KickPrompt)
        {
            GUILayout.BeginHorizontal();
            if (SteamMatchmaking.GetLobbyMemberData(system.LobbyID, memberId, "loaded") == "yes")
            {
                GUILayout.Box(team);
            }
            else
            {
                GUILayout.Box($"({modsDownloaded}/{totalMods})");
            }

            GUILayout.FlexibleSpace();
            GUILayout.Box($"<color={readyColor}>{name}</color>");
            GUILayout.FlexibleSpace();
            GUILayout.Box(team);
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
            {
                KickPrompt = CSteamID.Nil;
            }
        }
    }

    private void TestFunction(LobbySystem lobbySystem)
    {
        LoggingHelper.LogMarker();
        SteamLobbyDataTransfer.ImportFromLobbyData(lobbySystem.LobbyID, out MatchSettings temp);
        if (temp == null)
        {
            return;
        }

        string result = "Imported From Steam:\n";
        DataTransfer.GenericDataTransfer.ExportTo(temp, (key, value) => result += $"{key} = {value}\n");
        LoggingHelper.LogMarker(result);

        string weaponString = "";
        foreach (int weaponIndex in temp.Eagle.WeaponIndices)
        {
            if (weaponIndex < lobbySystem.Cache.Weapons.Count)
            {
                var weapon = lobbySystem.Cache.Weapons[weaponIndex];
                weaponString += $"{weapon.name}\n";
            }
            else
            {
                LoggingHelper.LogMarker($"Attempting to add weapon {weaponIndex} but only have {lobbySystem.Cache.Weapons.Count} in the cache");
            }
        }

        LoggingHelper.LogMarker(weaponString);


    }
}

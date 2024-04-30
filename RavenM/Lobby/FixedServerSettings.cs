using System;

namespace RavenM.Lobby;

// Various settings the host sets when first setting up the lobby that rarely change thereafter.
public class FixedServerSettings
{
    public const uint LobbyMemberMin = 2;
    public const uint LobbyMemberMax = 250; //Steam lobby max. There does not appear to be a constant for this in the Steam APIs so here's a magic number.

    private uint lobbyMemberCap = LobbyMemberMax;

    // Friends only is a steam lobby setting which does exactly what it says on the tin and also hides the server from the browse list regardless of the other setting.
    public bool FriendsOnlyLobby { get; set; } = false;

    // A value indicating if this lobby should appear in the browse UI. True will let it show up, false will not.
    public bool IncludeInBrowseList { get; set; } = true;

    public bool MidgameJoin { get; set; } = false;

    public uint LobbyMemberCap
    {
        get
        {
            return lobbyMemberCap;
        }
        set
        {
            lobbyMemberCap = Math.Min(Math.Max(value, LobbyMemberMin), LobbyMemberMax);
        }
    }

    public ulong OwnerID { get; set; }

    public bool NameTagsEnabled { get; set; } = true;

    public bool TeamOnlyNameTags { get; set; } = false;

    public int ModCount { get; set; } = 0;

    /// <summary>
    /// Represents what version of RavenM a given lobby is running. No default value as we want to confirm this field was actually set by a host.
    /// Hosts must set this as part of hosting.
    /// </summary>
    public string BuildID { get; set; }

    public FixedServerSettings() { }
}
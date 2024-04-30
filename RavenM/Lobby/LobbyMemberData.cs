namespace RavenM.Lobby;

public class LobbyMemberData
{
    // Represents the user as being done loading all data while in a lobby on the instant action maps menu.
    public bool Loaded { get; set; } = false;

    // Represents if the user has fully loaded into a started lobby's map, // TODO: We should probably just combine these two into one enum value
    public bool Ready { get; set; } = false;

    public int Team { get; set; } = -1;

    // Cross check this with data from the lobby's FixedServerSettings.
    public int ServerModsDownloaded { get; set; } = 0;
}

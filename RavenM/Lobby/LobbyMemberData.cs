namespace RavenM.Lobby;

public class LobbyMemberData
{
    // Represents the user as being done loading all data while in a lobby on the instant action maps menu.
    public bool Loaded { get; set; } = false;

    // Represents if the user has fully loaded into a started lobby's map, // TODO (False when Loaded is true, for some reason).
    public bool Ready { get; set; } = false;

    public int Team { get; set; } = 0;

    public int ServerModsNeeded { get; set; } = 0;
}

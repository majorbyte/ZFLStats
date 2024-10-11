using System.Xml;

namespace BloodBowl3;

public class Replay
{
    public required FileInfo File { get; set; }

    public required string ClientVersion { get; set; }

    public required string HomeCoach { get; set; }

    public required string AwayCoach { get; set; }

    public required Team HomeTeam { get; set; }

    public required Team AwayTeam { get; set; }

    public required string CompetitionName { get; set; }

    public required XmlElement ReplayRoot { get; set; }

    public Player GetPlayer(int id)
    {
        return HomeTeam.Players.TryGetValue(id, out var p) ? p : AwayTeam.Players[id];
    }
}
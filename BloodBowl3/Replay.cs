using System.Xml;

namespace BloodBowl3;

public class Replay(FileInfo file, string clientVersion, XmlElement root)
{
    public FileInfo File => file;

    public string ClientVersion => clientVersion;

    public required string HomeCoach { get; set; }

    public required string AwayCoach { get; set; }

    public required Team HomeTeam { get; set; }

    public required Team AwayTeam { get; set; }

    public required string CompetitionName { get; set; }

    public XmlElement ReplayRoot => root;

    public Player GetPlayer (int id) => this.HomeTeam.Players.TryGetValue(id, out var p) ? p : this.AwayTeam.Players[id];
}
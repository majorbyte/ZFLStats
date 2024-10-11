namespace BloodBowl3;

public class Team(string name, string coach)
{
    public string Name { get; set; } = name;

    public string Coach { get; set; } = coach;

    public Dictionary<int, Player> Players { get; set; } = new();
}
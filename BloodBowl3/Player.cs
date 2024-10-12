using System.Diagnostics;

namespace BloodBowl3;

[DebuggerDisplay("Player({Id}, {Name})")]
public class Player(int team, int id, string name, string lobbyId) : IComparable<Player>
{
    public int Team { get; } = team;

    public int Id { get; } = id;
    public string LobbyId { get; } = lobbyId;

    public string Name { get; } = name;

    public int FirstXp = -1;

    public int LastXp = -1;

    public int CompareTo(Player? other)
    {
        return other != null ? Id.CompareTo(other.Id) : 1;
    }

    public override bool Equals(object? obj)
    {
        if (obj is Player other)
        {
            return CompareTo(other) == 0;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
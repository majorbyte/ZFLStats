using System.Diagnostics;

namespace BloodBowl3;

[DebuggerDisplay("Player({Id}, {Name})")]
public class Player(int team, int id, string name, string? lobbyId) : IComparable<Player>
{
    public int Team => team;

    public int Id => id;

    public string Name => name;

    public string? LobbyId => lobbyId;

    public int FirstXP = -1;

    public int LastXP = -1;

    public int CompareTo(Player? other)
    {
        return other != null ? this.Id.CompareTo(other.Id) : 1;
    }

    public override bool Equals(object? obj)
    {
        if (obj is Player other)
        {
            return this.CompareTo(other) == 0;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return this.Id.GetHashCode();
    }
}
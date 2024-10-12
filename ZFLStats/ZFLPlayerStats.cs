namespace ZFLStats;

public class ZFLPlayerStats(int id, string lobbyId, string name)
{
    public int PlayerId {get;set;} = id;
    public string LobbyId {get;set;} = lobbyId;
    public string Name {get;set;} = name;
    public int TouchdownsScored { get; set; }

    public int CasInflicted { get; set; }

    public int CasSustained { get; set; }

    public int PassCompletions { get; set; }

    public int FoulsInflicted { get; set; }

    public int FoulsSustained { get; set; }

    public int SppEarned { get; set; }

    public int Sacks { get; set; }

    public int Kills { get; set; }

    public int SurfsInflicted { get; set; }

    public int SurfsSustained { get; set; }

    public int Expulsions { get; set; }

    public int DodgeTurnovers { get; set; }

    public int DubskullsRolled { get; set; }

    public int ArmorRollsSustained { get; set; }

    internal bool Mvp { get; set; }

    internal int ExpectedSPP => TouchdownsScored * 3 + CasInflicted * 2 + PassCompletions + (Mvp ? 4 : 0);

    public void PrintToConsole(int indent)
    {
        Print(indent, nameof(TouchdownsScored), TouchdownsScored);
        Print(indent, nameof(CasInflicted), CasInflicted);
        Print(indent, nameof(CasSustained), CasSustained);
        Print(indent, nameof(PassCompletions), PassCompletions);
        Print(indent, nameof(FoulsInflicted), FoulsInflicted);
        Print(indent, nameof(FoulsSustained), FoulsSustained);
        Print(indent, nameof(SppEarned), SppEarned);
        Print(indent, nameof(Sacks), Sacks);
        Print(indent, nameof(Kills), Kills);
        Print(indent, nameof(SurfsInflicted), SurfsInflicted);
        Print(indent, nameof(SurfsSustained), SurfsSustained);
        Print(indent, nameof(Expulsions), Expulsions);
        Print(indent, nameof(DodgeTurnovers), DodgeTurnovers);
        Print(indent, nameof(DubskullsRolled), DubskullsRolled);
        Print(indent, nameof(ArmorRollsSustained), ArmorRollsSustained);
    }

    private static void Print(int indent, string text, int value)
    {
        if (value <= 0)
            return;
        Console.Write(new string(' ', indent));
        Console.WriteLine($"{text}: {value}");
    }
}
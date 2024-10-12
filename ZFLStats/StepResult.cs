namespace ZFLStats;

using BloodBowl3;

public class StepResult
{

    public int LastBlockingPlayerId {get;set;}= -1;
    public int LastDefendingPlayerId {get;set;}= -1;
    public int LastDeadPlayerId{get;set;} = -1;

    public int PassingPlayer {get;set;}= -1;
    public int CatchingPlayer {get;set;}= -1;

    public int MovingPlayer {get;set;}= -1;

    public int BallCarrier {get;set;}= -1;

    public int TargetId{get;set;} = -1;

    public bool CatchSuccess{get;set;} = false;

    public CasualtyOutcome? LastCas {get;set;} = null;
    public BlockOutcome? LastBlockOutcome {get;set;}= null;
}
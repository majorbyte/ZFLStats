namespace ZFLStats;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using BloodBowl3;

internal class StepState
{
    public int LastBlockingPlayerId {get;set;}= -1;
    public int LastDefendingPlayerId {get;set;}= -1;
    public int LastDeadPlayerId{get;set;} = -1;
    public int PassingPlayer {get;set;}= -1;
    public int CatchingPlayer {get;set;}= -1;
    public int MovingPlayer {get;set;}= -1;
    public int BallCarrier {get;set;}= -1;
    public int ActiveGamer { get; set; } = -1;
    public int TargetId{get;set;} = -1;
    public bool CatchSuccess{get;set;} = false;
    public CasualtyOutcome? LastCas {get;set;} = null;
    public BlockOutcome? LastBlockOutcome {get;set;}= null;
}
internal class ZFLStatsAnalyzer(Replay replay)
{
    private readonly Dictionary<int, ZFLPlayerStats> stats = new ();

    public IEnumerable<ZFLPlayerStats> HomeTeamStats => replay.HomeTeam.Players.Keys.OrderBy(id => id).Select(this.GetStatsFor);

    public IEnumerable<ZFLPlayerStats> VisitingTeamStats => replay.AwayTeam.Players.Keys.OrderBy(id => id).Select(this.GetStatsFor);

    public int HomeFanAttendance => replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/HomeRoll/Dice/Die/Value")!.InnerText.ParseInt();

    public int VisitingFanAttendance => replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/AwayRoll/Dice/Die/Value")!.InnerText.ParseInt();

    public Task AnalyzeAsync()
    {
        return Task.Run(this.Analyze);
    }

    private void Analyze()
    {
        InitPlayerStats();
        StepState stepState = new();
        
        foreach (var replayStep in replay.ReplayRoot.SelectNodes("ReplayStep")!.Cast<XmlElement>())
        {
            //var turnover = false;

            foreach (var node in replayStep.ChildNodes.Cast<XmlElement>())
            {
                if (node.LocalName == "EventEndTurn")
                {
                    //turnover = node["Reason"]!.InnerText == "2";
                    stepState.ActiveGamer = node["NextPlayingGamer"]?.InnerText.ParseInt() ?? 0;
                }
                else if (node.LocalName == "EventExecuteSequence")
                {
                    ParseEventExecuteSequence(node, stepState);
                }
                else if (node.LocalName == "EventTouchdown")
                {
                    var playerId = node["PlayerId"]!.InnerText.ParseInt();
                    this.GetStatsFor(playerId).TouchdownsScored += 1;
                }
            }

            if (replayStep.SelectSingleNode("BoardState/Ball") is not XmlElement ballNode) continue;
            if (ballNode["Carrier"] is { } carrierNode)
            {
                var newCarrier = carrierNode.InnerText.ParseInt();
                if (newCarrier == stepState.BallCarrier) continue;
                stepState.BallCarrier = newCarrier;
                Debug.WriteLine($"* New ball carrier {newCarrier}!");
            }
            else if (stepState.BallCarrier != -1)
            {
                stepState.BallCarrier = -1;
                Debug.WriteLine("* Ball is loose!");
            }
        }

        foreach (var playerData in replay.ReplayRoot.SelectNodes("//PlayerState")!.Cast<XmlElement>())
        {
            if (playerData["ExperienceGained"]?.InnerText.ParseInt() is not ({ } xp and > 0)) continue;
            
            var id = playerData["Id"]!.InnerText.ParseInt();
            var s = this.GetStatsFor(id);
            if (s.SppEarned + 4 == xp)
                s.Mvp = true;
            s.SppEarned = xp;
            //                    Console.WriteLine($"Player {id} ({replay.GetPlayer(id).Name}) gained {xp} xp");
        }
    }

    private void ParseEventExecuteSequence(XmlNode node, StepState stepState)
    {
        foreach (var stepResult in node.SelectNodes("Sequence/StepResult")!.Cast<XmlElement>())
        {
            var stepName = stepResult["Step"]!["Name"]!.InnerText.FromBase64();
            var stepMsgData = stepResult["Step"]!["MessageData"]!.InnerText.FromBase64().FromBase64();
            var step = new XmlDocument();
            step.LoadXml(stepMsgData);
            var stepType = (StepType)step.DocumentElement!["StepType"]!.InnerText.ParseInt();
            var playerId = step.DocumentElement["PlayerId"]?.InnerText.ParseInt() ?? -1;
            stepState.TargetId = step.DocumentElement["TargetId"]?.InnerText.ParseInt() ?? -1;
            Debug.WriteLine($"{stepName}: {stepType}, player {playerId}, target {stepState.TargetId}");
            UpdateStepState(stepState, stepType, playerId);

            stepState.LastDeadPlayerId = -1;
            stepState.LastCas = null;
            stepState.CatchSuccess = false;

            foreach (var results in stepResult.SelectNodes("Results/StringMessage")!.Cast<XmlElement>())
            {
                var resultsName = results["Name"]!.InnerText.FromBase64();
                var resultsMsgData = results["MessageData"]!.InnerText.FromBase64().FromBase64();
                var result = new XmlDocument();
                result.LoadXml(resultsMsgData);

                switch (resultsName)
                {
                    case "ResultSkillUsage":
                        ParseResultSkillUsage(result);
                        break;
                    case "ResultMoveOutcome":
                        ParseResultMoveOutcome(stepState, result);
                        Debug.WriteLine("ResultMoveOutcome");
                        break;
                    case "ResultRoll":
                        ParseResultRoll(result, stepState);
                        break;
                    case "QuestionBlockDice":
                        ParseQuestionBlockDice(result,stepState);
                        break;
                    case "ResultBlockRoll":
                        var dieValue = result.DocumentElement!.SelectSingleNode("Die/Value")!.InnerText.ParseInt();
                        Debug.WriteLine($">> Block die {dieValue}");
                        break;
                    case "ResultPlayerRemoval":
                        ParseResultPlayerRemoval(result,stepState);
                        break;
                    case "ResultBlockOutcome":
                        ParseResultBlockOutcome(result, stepState);
                        break;
                    case "ResultInjuryRoll":
                        var injury = (InjuryOutcome)result.DocumentElement!["Outcome"]!.InnerText.ParseInt();
                        Debug.WriteLine($">> Injury outcome {injury}");
                        break;
                    case "ResultCasualtyRoll":
                        var casualty = (CasualtyOutcome)result.DocumentElement!["Outcome"]!.InnerText.ParseInt();
                        stepState.LastCas = casualty;
                        Debug.WriteLine($">> Casualty outcome {casualty}");
                        break;
                    case "ResultRaisedDead":
                        var zombieId = result.DocumentElement!["RaisedPlayerId"]!.InnerText.ParseInt();
                        Debug.WriteLine($">> Raising {stepState.LastDeadPlayerId} as {zombieId}");
                        break;
                    case "ResultPlayerSentOff":
                    {
                        var sentOffId = result.DocumentElement!["PlayerId"]!.InnerText.ParseInt();
                        this.GetStatsFor(sentOffId).Expulsions += 1;
                        Debug.WriteLine($">> Sending {sentOffId} off the pitch");
                    }
                        break;
                    default:
                        Debug.WriteLine(resultsName);
                        break;
                }
            }

            if (stepType != StepType.Catch || stepState.PassingPlayer < 0 || stepState.CatchingPlayer < 0 || !stepState.CatchSuccess)
                continue;
            this.GetStatsFor(stepState.PassingPlayer).PassCompletions += 1;
            stepState.PassingPlayer = -1;
            stepState.CatchingPlayer = -1;
        }
    }


    private void UpdateStepState(StepState stepState, StepType stepType, int playerId)
    {
        switch (stepType)
        {
            case StepType.Kickoff:
                stepState.LastBlockingPlayerId = -1;
                stepState.LastDefendingPlayerId = -1;
                stepState.PassingPlayer = -1;
                stepState.CatchingPlayer = -1;
                stepState.BallCarrier = -1;
                break;
            case StepType.Activation:
                stepState.LastBlockingPlayerId = -1;
                stepState.LastDefendingPlayerId = -1;
                stepState.PassingPlayer = -1;
                stepState.CatchingPlayer = -1;
                break;
            case StepType.Move:
                stepState.LastBlockingPlayerId = -1;
                stepState.LastDefendingPlayerId = -1;
                stepState.PassingPlayer = -1;
                stepState.CatchingPlayer = -1;
                stepState.MovingPlayer = playerId;
                break;
            case StepType.Damage:
                break;
            case StepType.Block:
                stepState.LastBlockingPlayerId = playerId;
                stepState.LastDefendingPlayerId = stepState.TargetId;
                stepState.PassingPlayer = -1;
                stepState.CatchingPlayer = -1;
                break;
            case StepType.Pass:
                stepState.PassingPlayer = playerId;
                stepState.CatchingPlayer = stepState.TargetId;
                break;
            case StepType.Catch:
                break;
            case StepType.Foul:
                stepState.LastBlockingPlayerId = -1;
                stepState.LastDefendingPlayerId = -1;
                stepState.PassingPlayer = -1;
                stepState.CatchingPlayer = -1;
                this.GetStatsFor(playerId).FoulsInflicted += 1;
                this.GetStatsFor(stepState.TargetId).FoulsSustained += 1;
                break;
            case StepType.Referee:
                break;
        }        
    }

    private void ParseResultSkillUsage(XmlDocument doc)
    {
        var playerId = doc.DocumentElement!["PlayerId"]?.InnerText.ParseInt() ?? -1;

        var skill = (Skill)doc.DocumentElement.SelectSingleNode("Skill")!.InnerText.ParseInt();
        var used = doc.DocumentElement.SelectSingleNode("Used")!.InnerText == "1";
        if (used && skill == Skill.StripBall)
        {
            GetStatsFor(playerId).Sacks += 1;
        }
        Debug.WriteLine($"ResultSkillUsage {skill} used? {used}");
    }

    private void ParseResultMoveOutcome(StepState stepState, XmlDocument result)
    {
        if (result.DocumentElement!.SelectSingleNode("Rolls/RollSummary") is not XmlElement roll) return;
        
        var rollType = (RollType)roll["RollType"]!.InnerText.ParseInt();
        var outcome = roll["Outcome"]!.InnerText;
        if (rollType == RollType.Dodge && outcome == "0" && replay.GetPlayer(stepState.MovingPlayer).Team == stepState.ActiveGamer)
        {
            this.GetStatsFor(stepState.MovingPlayer).DodgeTurnovers += 1;
        }
    }

    private void ParseResultRoll(XmlDocument doc, StepState stepState)
    {
        var dice = doc.DocumentElement!.SelectNodes("Dice/Die")!.Cast<XmlElement>().ToArray();
        var dieType = (DieType)dice[0]["DieType"]!.InnerText.ParseInt();
        var values = dice.Select(d => d["Value"]!.InnerText.ParseInt()).ToArray();
        var failed = doc.DocumentElement["Outcome"]!.InnerText == "0";
        var rollType = (RollType)doc.DocumentElement["RollType"]!.InnerText.ParseInt();

        // Pass and catch reroll seem to be handled differently??
        if (failed && rollType == RollType.Pass)
        {
            stepState.PassingPlayer = -1;
            stepState.CatchingPlayer = -1;
        }

        if (!failed && rollType == RollType.Catch)
        {
            stepState.CatchSuccess = true;
        }

        if (rollType == RollType.Armor)
        {
            GetStatsFor(stepState.TargetId).ArmorRollsSustained += 1;
        }
        
        Debug.WriteLine($">> {rollType} {dieType} rolls: {string.Join(", ", values)}");
    }

    private void ParseQuestionBlockDice(XmlDocument doc, StepState stepState)
    {
        var dice = doc.DocumentElement!.SelectNodes("Dice/Die")!.Cast<XmlElement>().ToArray();
        var dieType = (DieType) dice[0]["DieType"]!.InnerText.ParseInt();
        Debug.Assert(dieType == DieType.Block);
        var values = dice.Select(d => d["Value"]!.InnerText.ParseInt()).ToArray();
        Debug.WriteLine($">> Picking block dice: {string.Join(", ", values)}");
        if (values.Length >= 2 && values.All(v => v == 0))
            GetStatsFor(stepState.LastBlockingPlayerId).DubskullsRolled += 1;
    }

    private void ParseResultPlayerRemoval(XmlDocument doc, StepState stepState)
    {
        var playerIdR = doc.DocumentElement!["PlayerId"]?.InnerText.ParseInt() ?? -1;
        var reason = doc.DocumentElement["Reason"]?.InnerXml.ParseInt() ?? -1;

        var situation = (PlayerSituation)doc.DocumentElement["Situation"]!.InnerText.ParseInt();
        if (situation == PlayerSituation.Reserve)
        {
            Debug.WriteLine($">> Surf by {stepState.LastBlockingPlayerId} on {stepState.LastDefendingPlayerId}");

            var isSurfed = stepState is { LastBlockingPlayerId: >= 0, LastDefendingPlayerId: >= 0 };
            GetStatsFor(stepState.LastBlockingPlayerId).SurfsInflicted += isSurfed ? 1 : 0;
            GetStatsFor(stepState.LastDefendingPlayerId).SurfsSustained += isSurfed ? 1 : 0;

            var isBallCarrier = stepState.BallCarrier >= 0 && stepState.BallCarrier == stepState.LastDefendingPlayerId;
            GetStatsFor(stepState.LastBlockingPlayerId).Sacks += isBallCarrier && isSurfed  ? 1 : 0;

            stepState.LastBlockingPlayerId = -1;
            stepState.LastDefendingPlayerId = -1;
            stepState.LastBlockOutcome = null;
            stepState.LastCas = null;
            return;
        }

        Debug.WriteLine($">> Removing {playerIdR}, situation {situation}, reason {reason}");

        var targetDied = stepState.LastCas == CasualtyOutcome.Dead; 
        if (situation == PlayerSituation.Injured && playerIdR == stepState.LastDefendingPlayerId)
        {
            var hasInflictedInjury = stepState.LastBlockingPlayerId >= 0;
            
            GetStatsFor(stepState.LastDefendingPlayerId).CasSustained += 1;

            GetStatsFor(stepState.LastBlockingPlayerId).CasInflicted += hasInflictedInjury ? 1 : 0;
            GetStatsFor(stepState.LastBlockingPlayerId).Kills += targetDied ? 1 : 0;
        }

        if (targetDied)
        {
            stepState.LastDeadPlayerId = playerIdR;
        }
        stepState.LastCas = null;
    }

    private void ParseResultBlockOutcome(XmlDocument doc, StepState stepState)
    {
        var defenderId = doc.DocumentElement!["DefenderId"]!.InnerText.ParseInt();

        if (stepState.BallCarrier < 0 || defenderId != stepState.BallCarrier) return;

        var attackerId = doc.DocumentElement["AttackerId"]!.InnerText.ParseInt();
        var outcome = (BlockOutcome)doc.DocumentElement["Outcome"]!.InnerText.ParseInt();
        stepState.LastBlockingPlayerId = attackerId;
        stepState.LastDefendingPlayerId = defenderId;
        stepState.LastBlockOutcome = outcome;

        switch (outcome)
        {
            case BlockOutcome.AttackerDown:
            case BlockOutcome.BothStanding:
            case BlockOutcome.Pushed:
                break;
            case BlockOutcome.BothDown:
            case BlockOutcome.BothWrestleDown:
            case BlockOutcome.DefenderDown:
            case BlockOutcome.DefenderPushedDown:
                GetStatsFor(attackerId).Sacks += 1;
                break;
            default:
                throw new ArgumentOutOfRangeException($"Block outcome: {outcome}");
        }

        Debug.WriteLine($">> Block by {attackerId} on {defenderId}, outcome {outcome}");
    }

    
    private void InitPlayerStats()
    {
        foreach(var player in replay.HomeTeam.Players) this.stats.TryAdd(player.Value.Id, new ZFLPlayerStats(player.Value.Id, player.Value.Name, player.Value.LobbyId));
        foreach(var player in replay.AwayTeam.Players) this.stats.TryAdd(player.Value.Id, new ZFLPlayerStats(player.Value.Id, player.Value.Name, player.Value.LobbyId));
    }
    private ZFLPlayerStats GetStatsFor(int playerId)
    {
        Debug.Assert(playerId >= 0);
        if (this.stats.TryGetValue(playerId, out var playerStats)) return playerStats;
        
        var p = replay.GetPlayer(playerId);
        playerStats = new ZFLPlayerStats(playerId, p.Name, p.LobbyId);
        this.stats.Add(playerId, playerStats);

        return playerStats;
    }
}

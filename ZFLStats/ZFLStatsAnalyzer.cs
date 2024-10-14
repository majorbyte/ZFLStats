﻿namespace ZFLStats;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using BloodBowl3;

internal class ZFLStatsAnalyzer(Replay replay)
{
    private readonly Dictionary<int, ZFLPlayerStats> stats = new ();

    public IEnumerable<ZFLPlayerStats> HomeTeamStats => replay.HomeTeam.Players.Keys.OrderBy(id => id).Select(this.GetStatsFor);

    public IEnumerable<ZFLPlayerStats> VisitingTeamStats => replay.VisitingTeam.Players.Keys.OrderBy(id => id).Select(this.GetStatsFor);

    public int HomeFanAttendance => replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/HomeRoll/Dice/Die/Value")!.InnerText.ParseInt();

    public int VisitingFanAttendance => replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/AwayRoll/Dice/Die/Value")!.InnerText.ParseInt();

    public Task AnalyzeAsync()
    {
        return Task.Run(this.Analyze);
    }

    public void Analyze()
    {
        foreach (var p in replay.HomeTeam.Players.Values.Concat(replay.VisitingTeam.Players.Values))
        {
            var stats = this.GetStatsFor(p.Id);
            stats.SppEarned = p.SppGained;
            stats.Mvp = p.Mvp;
        }

        int lastBlockingPlayerId = -1;
        int lastDefendingPlayerId = -1;
        BlockOutcome? lastBlockOutcome = null;

        int passingPlayer = -1;
        int catchingPlayer = -1;

        int movingPlayer = -1;

        int ballCarrier = -1;

        int activeGamer = -1;

        foreach (var replayStep in replay.ReplayRoot.SelectNodes("ReplayStep")!.Cast<XmlElement>())
        {
            var turnover = false;

            foreach (var node in replayStep.ChildNodes.Cast<XmlElement>())
            {
                if (node.LocalName == "EventEndTurn")
                {
                    turnover = node["Reason"]!.InnerText == "2";
                    activeGamer = node["NextPlayingGamer"]?.InnerText.ParseInt() ?? 0;
                }
                else if (node.LocalName == "EventExecuteSequence")
                {
                    foreach (var stepResult in node.SelectNodes("Sequence/StepResult")!.Cast<XmlElement>())
                    {
                        var stepName = stepResult["Step"]!["Name"]!.InnerText.FromBase64();
                        var stepMsgData = stepResult["Step"]!["MessageData"]!.InnerText.FromBase64().FromBase64();
                        var step = new XmlDocument();
                        step.LoadXml(stepMsgData);
                        var stepType = (StepType)step.DocumentElement!["StepType"]!.InnerText.ParseInt();
                        var playerId = step.DocumentElement["PlayerId"]?.InnerText.ParseInt() ?? -1;
                        var targetId = step.DocumentElement["TargetId"]?.InnerText.ParseInt() ?? -1;
                        Debug.WriteLine($"{stepName}: {stepType}, player {playerId}, target {targetId}");
                        switch (stepType)
                        {
                            case StepType.Kickoff:
                                lastBlockingPlayerId = -1;
                                lastDefendingPlayerId = -1;
                                passingPlayer = -1;
                                catchingPlayer = -1;
                                ballCarrier = -1;
                                break;
                            case StepType.Activation:
                                lastBlockingPlayerId = -1;
                                lastDefendingPlayerId = -1;
                                passingPlayer = -1;
                                catchingPlayer = -1;
                                break;
                            case StepType.Move:
                                lastBlockingPlayerId = -1;
                                lastDefendingPlayerId = -1;
                                passingPlayer = -1;
                                catchingPlayer = -1;
                                movingPlayer = playerId;
                                break;
                            case StepType.Damage:
                                break;
                            case StepType.Block:
                                lastBlockingPlayerId = playerId;
                                lastDefendingPlayerId = targetId;
                                passingPlayer = -1;
                                catchingPlayer = -1;
                                break;
                            case StepType.Pass:
                                passingPlayer = playerId;
                                catchingPlayer = targetId;
                                break;
                            case StepType.Catch:
                                break;
                            case StepType.Foul:
                                lastBlockingPlayerId = -1;
                                lastDefendingPlayerId = -1;
                                passingPlayer = -1;
                                catchingPlayer = -1;
                                this.GetStatsFor(playerId).FoulsInflicted += 1;
                                this.GetStatsFor(targetId).FoulsSustained += 1;
                                break;
                            case StepType.Referee:
                                break;
                            default:
                                break;
                        }

                        CasualtyOutcome? lastCas = null;
                        int lastDeadPlayerId = -1;

                        bool catchSuccess = false;

                        foreach (var results in stepResult.SelectNodes("Results/StringMessage")!.Cast<XmlElement>())
                        {
                            var resultsName = results["Name"]!.InnerText.FromBase64();
                            var resultsMsgData = results["MessageData"]!.InnerText.FromBase64().FromBase64();
                            var result = new XmlDocument();
                            result.LoadXml(resultsMsgData);
                            var playerIdR = result.DocumentElement!["PlayerId"]?.InnerText.ParseInt() ?? -1;
                            var reason = result.DocumentElement["Reason"]?.InnerXml.ParseInt() ?? -1;

                            switch (resultsName)
                            {
                                case "ResultSkillUsage":
                                    {
                                        var skill = (Skill)result.DocumentElement.SelectSingleNode("Skill")!.InnerText.ParseInt();
                                        var used = result.DocumentElement.SelectSingleNode("Used")!.InnerText == "1";
                                        if (used && skill == Skill.StripBall)
                                        {
                                            this.GetStatsFor(playerIdR).Sacks += 1;
                                        }

                                        Debug.WriteLine($"ResultSkillUsage {skill} used? {used}");
                                    }
                                    break;
                                case "ResultMoveOutcome":
                                    {
                                        if (result.DocumentElement.SelectSingleNode("Rolls/RollSummary") is XmlElement roll)
                                        {
                                            var rollType = (RollType)roll["RollType"]!.InnerText.ParseInt();
                                            var outcome = roll["Outcome"]!.InnerText;
                                            if (rollType == RollType.Dodge && outcome == "0" && replay.GetPlayer(movingPlayer).Team == activeGamer)
                                            {
                                                this.GetStatsFor(movingPlayer).DodgeTurnovers += 1;
                                            }
                                        }

                                        Debug.WriteLine("ResultMoveOutcome");
                                    }
                                    break;
                                case "ResultRoll":
                                    {
                                        var dice = result.DocumentElement.SelectNodes("Dice/Die")!.Cast<XmlElement>().ToArray();
                                        var dieType = (DieType)dice[0]["DieType"]!.InnerText.ParseInt();
                                        var values = dice.Select(d => d["Value"]!.InnerText.ParseInt()).ToArray();
                                        var failed = result.DocumentElement["Outcome"]!.InnerText == "0";
                                        var rollType = (RollType)result.DocumentElement["RollType"]!.InnerText.ParseInt();

                                        // Pass and catch reroll seem to be handled differently??
                                        if (failed && rollType == RollType.Pass)
                                        {
                                            passingPlayer = -1;
                                            catchingPlayer = -1;
                                        }

                                        if (!failed && rollType == RollType.Catch)
                                        {
                                            catchSuccess = true;
                                        }

                                        if (rollType == RollType.Armor)
                                        {
                                            this.GetStatsFor(targetId).ArmorRollsSustained += 1;
                                        }

                                        Debug.WriteLine($">> {rollType} {dieType} rolls: {string.Join(", ", values)}");
                                    }
                                    break;
                                case "QuestionBlockDice":
                                    {
                                        var dice = result.DocumentElement.SelectNodes("Dice/Die")!.Cast<XmlElement>().ToArray();
                                        var dieType = (DieType)dice[0]["DieType"]!.InnerText.ParseInt();
                                        Debug.Assert(dieType == DieType.Block);
                                        var values = dice.Select(d => d["Value"]!.InnerText.ParseInt()).ToArray();
                                        Debug.WriteLine($">> Picking block dice: {string.Join(", ", values)}");
                                        if (values.Length >= 2 && values.All(v => v == 0)) this.GetStatsFor(lastBlockingPlayerId).DubskullsRolled += 1;
                                    }
                                    break;
                                case "ResultBlockRoll":
                                    {
                                        var dieValue = result.DocumentElement.SelectSingleNode("Die/Value")!.InnerText.ParseInt();
                                        Debug.WriteLine($">> Block die {dieValue}");
                                    }
                                    break;
                                case "ResultPlayerRemoval":
                                    {
                                        var situation = (PlayerSituation)result.DocumentElement["Situation"]!.InnerText.ParseInt();
                                        if (situation == PlayerSituation.Reserve)
                                        {
                                            Debug.WriteLine($">> Surf by {lastBlockingPlayerId} on {lastDefendingPlayerId}");
                                            if (lastBlockingPlayerId >= 0 && lastDefendingPlayerId >= 0)
                                            {
                                                this.GetStatsFor(lastBlockingPlayerId).SurfsInflicted += 1;
                                                this.GetStatsFor(lastDefendingPlayerId).SurfsSustained += 1;
                                                if (ballCarrier >= 0 && ballCarrier == lastDefendingPlayerId)
                                                {
                                                    this.GetStatsFor(lastBlockingPlayerId).Sacks += 1;
                                                }
                                            }

                                            lastBlockingPlayerId = -1;
                                            lastDefendingPlayerId = -1;
                                            lastBlockOutcome = null;
                                        }
                                        else
                                        {
                                            Debug.WriteLine($">> Removing {playerIdR}, situation {situation}, reason {reason}");
                                            if (situation == PlayerSituation.Injured && playerIdR == lastDefendingPlayerId)
                                            {
                                                this.GetStatsFor(lastDefendingPlayerId).CasSustained += 1;
                                                if (lastBlockingPlayerId >= 0)
                                                {
                                                    this.GetStatsFor(lastBlockingPlayerId).CasInflicted += 1;
                                                    if (lastCas == CasualtyOutcome.Dead) this.GetStatsFor(lastBlockingPlayerId).Kills += 1;
                                                }
                                            }

                                            if (lastCas == CasualtyOutcome.Dead)
                                            {
                                                lastDeadPlayerId = playerIdR;
                                            }
                                        }

                                        lastCas = null;
                                    }
                                    break;
                                case "ResultBlockOutcome":
                                    {
                                        var attackerId = result.DocumentElement["AttackerId"]!.InnerText.ParseInt();
                                        var defenderId = result.DocumentElement["DefenderId"]!.InnerText.ParseInt();
                                        var outcome = (BlockOutcome)result.DocumentElement["Outcome"]!.InnerText.ParseInt();
                                        lastBlockingPlayerId = attackerId;
                                        lastDefendingPlayerId = defenderId;
                                        lastBlockOutcome = outcome;

                                        if (ballCarrier >= 0 && defenderId == ballCarrier)
                                        {
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
                                                    this.GetStatsFor(attackerId).Sacks += 1;
                                                    break;
                                                default:
                                                    throw new ArgumentOutOfRangeException();
                                            }
                                        }

                                        Debug.WriteLine($">> Block by {attackerId} on {defenderId}, outcome {outcome}");
                                    }
                                    break;
                                case "ResultInjuryRoll":
                                    {
                                        var injury = (InjuryOutcome)result.DocumentElement["Outcome"]!.InnerText.ParseInt();
                                        Debug.WriteLine($">> Injury outcome {injury}");
                                    }
                                    break;
                                case "ResultCasualtyRoll":
                                    var casualty = (CasualtyOutcome)result.DocumentElement["Outcome"]!.InnerText.ParseInt();
                                    lastCas = casualty;
                                    Debug.WriteLine($">> Casualty outcome {casualty}");
                                    break;
                                case "ResultRaisedDead":
                                    var zombieId = result.DocumentElement["RaisedPlayerId"]!.InnerText.ParseInt();
                                    Debug.WriteLine($">> Raising {lastDeadPlayerId} as {zombieId}");
                                    break;
                                case "ResultPlayerSentOff":
                                    {
                                        var sentOffId = result.DocumentElement["PlayerId"]!.InnerText.ParseInt();
                                        this.GetStatsFor(sentOffId).Expulsions += 1;
                                        Debug.WriteLine($">> Sending {sentOffId} off the pitch");
                                    }
                                    break;
                                default:
                                    Debug.WriteLine(resultsName);
                                    break;
                            }
                        }

                        if (stepType == StepType.Catch && passingPlayer >= 0 && catchingPlayer >= 0 && catchSuccess)
                        {
                            this.GetStatsFor(passingPlayer).PassCompletions += 1;
                            passingPlayer = -1;
                            catchingPlayer = -1;
                        }
                    }
                }
                else if (node.LocalName == "EventTouchdown")
                {
                    var playerId = node["PlayerId"]!.InnerText.ParseInt();
                    this.GetStatsFor(playerId).TouchdownsScored += 1;
                }
            }

            if (replayStep.SelectSingleNode("BoardState/Ball") is XmlElement ballNode)
            {
                if (ballNode["Carrier"] is { } carrierNode)
                {
                    var newCarrier = carrierNode.InnerText.ParseInt();
                    if (newCarrier != ballCarrier)
                    {
                        ballCarrier = newCarrier;
                        Debug.WriteLine($"* New ball carrier {newCarrier}!");
                    }
                }
                else if (ballCarrier != -1)
                {
                    ballCarrier = -1;
                    Debug.WriteLine("* Ball is loose!");
                }
            }
        }
    }

    private ZFLPlayerStats GetStatsFor(int playerId)
    {
        Debug.Assert(playerId >= 0);
        if (!this.stats.TryGetValue(playerId, out var playerStats))
        {
            var p = replay.GetPlayer(playerId);
            playerStats = new ZFLPlayerStats(playerId, p.Name, p.LobbyId);
            this.stats.Add(playerId, playerStats);
        }

        return playerStats;
    }
}
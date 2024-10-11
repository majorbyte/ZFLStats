namespace ZFLStats;

using System.CommandLine;
using System.Diagnostics;
using System.Xml;
using BloodBowl3;

public class Program
{
    private static void Run(FileSystemInfo fileOrDir, string? coachFilter, string? teamFilter, FileInfo? csvFile, bool autoCsv, bool silent)
    {
        var csv = csvFile?.CreateText();

        foreach (var replay in ReplayParser.GetReplays(fileOrDir, coachFilter, teamFilter))
        {
            if (replay == null) continue;

            if (autoCsv){
                csv?.Dispose();
                var filename = replay.File.FullName.Replace("bbr", "csv");
                csv = new FileInfo(filename).CreateText();
            }

            var stats = new Dictionary<int, ZFLPlayerStats>();

            ZFLPlayerStats GetStatsFor(int playerId)
            {
                Debug.Assert(playerId >= 0);
                if (!stats.TryGetValue(playerId, out var playerStats))
                {
                    playerStats = new ZFLPlayerStats();
                    stats.Add(playerId, playerStats);
                }

                return playerStats;
            }

            int lastBlockingPlayerId = -1;
            int lastDefendingPlayerId = -1;
            BlockOutcome? lastBlockOutcome = null;

            int passingPlayer = -1;
            int catchingPlayer = -1;

            int movingPlayer = -1;

            int ballCarrier = -1;

            foreach (var replayStep in replay.ReplayRoot.SelectNodes("ReplayStep").Cast<XmlElement>())
            {
                foreach (var stepResult in replayStep.SelectNodes("EventExecuteSequence/Sequence/StepResult").Cast<XmlElement>())
                {
                    var stepName = stepResult["Step"]["Name"].InnerText.FromBase64();
                    var stepMsgData = stepResult["Step"]["MessageData"].InnerText.FromBase64().FromBase64();
                    var step = new XmlDocument();
                    step.LoadXml(stepMsgData);
                    var stepType = (StepType)step.DocumentElement["StepType"].InnerText.ParseInt();
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
                            GetStatsFor(playerId).FoulsInflicted += 1;
                            GetStatsFor(targetId).FoulsSustained += 1;
                            break;
                        case StepType.Referee:
                            break;
                        default:
                            break;
                    }

                    CasualtyOutcome? lastCas = null;
                    int lastDeadPlayerId = -1;

                    bool catchSuccess = false;

                    foreach (var results in stepResult.SelectNodes("Results/StringMessage").Cast<XmlElement>())
                    {
                        var resultsName = results["Name"].InnerText.FromBase64();
                        var resultsMsgData = results["MessageData"].InnerText.FromBase64().FromBase64();
                        var result = new XmlDocument();
                        result.LoadXml(resultsMsgData);
                        var playerIdR = result.DocumentElement["PlayerId"]?.InnerText.ParseInt() ?? -1;
                        var reason = result.DocumentElement["Reason"]?.InnerXml.ParseInt() ?? -1;

                        switch (resultsName)
                        {
                            case "ResultSkillUsage":
                            {
                                var skill = (Skill)result.DocumentElement.SelectSingleNode("Skill").InnerText.ParseInt();
                                var used = result.DocumentElement.SelectSingleNode("Used").InnerText == "1";
                                if (used && skill == Skill.StripBall)
                                {
                                    GetStatsFor(playerIdR).Sacks += 1;
                                }

                                Debug.WriteLine($"ResultSkillUsage {skill} used? {used}");
                            }
                                break;
                            case "ResultMoveOutcome":
                            {
                                if (result.DocumentElement.SelectSingleNode("Rolls/RollSummary/Outcome")?.InnerText == "0")
                                {
                                    GetStatsFor(movingPlayer).DodgeTurnovers += 1;
                                }

                                Debug.WriteLine("ResultMoveOutcome");
                            }
                                break;
                            case "ResultRoll":
                            {
                                var dice = result.DocumentElement.SelectNodes("Dice/Die").Cast<XmlElement>().ToArray();
                                var dieType = (DieType)dice[0]["DieType"].InnerText.ParseInt();
                                var values = dice.Select(d => d["Value"].InnerText.ParseInt()).ToArray();
                                var failed = result.DocumentElement["Outcome"].InnerText == "0";
                                var rollType = (RollType)result.DocumentElement["RollType"].InnerText.ParseInt();

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
                                    GetStatsFor(targetId).ArmorRollsSustained += 1;
                                }
                                
                                Debug.WriteLine($">> {rollType} {dieType} rolls: {string.Join(", ", values)}");
                            }
                                break;
                            case "QuestionBlockDice":
                            {
                                var dice = result.DocumentElement.SelectNodes("Dice/Die").Cast<XmlElement>().ToArray();
                                var dieType = (DieType) dice[0]["DieType"].InnerText.ParseInt();
                                Debug.Assert(dieType == DieType.Block);
                                var values = dice.Select(d => d["Value"].InnerText.ParseInt()).ToArray();
                                Debug.WriteLine($">> Picking block dice: {string.Join(", ", values)}");
                                if (values.Length >= 2 && values.All(v => v == 0))
                                    GetStatsFor(lastBlockingPlayerId).DubskullsRolled += 1;
                            }
                                break;
                            case "ResultBlockRoll":
                            {
                                var dieValue = result.DocumentElement.SelectSingleNode("Die/Value").InnerText.ParseInt();
                                Debug.WriteLine($">> Block die {dieValue}");
                            }
                                break;
                            case "ResultPlayerRemoval":
                            {
                                var situation = (PlayerSituation)result.DocumentElement["Situation"].InnerText.ParseInt();
                                if (situation == PlayerSituation.Reserve)
                                {
                                    Debug.WriteLine($">> Surf by {lastBlockingPlayerId} on {lastDefendingPlayerId}");
                                    if (lastBlockingPlayerId >= 0 && lastDefendingPlayerId >= 0)
                                    {
                                        GetStatsFor(lastBlockingPlayerId).SurfsInflicted += 1;
                                        GetStatsFor(lastDefendingPlayerId).SurfsSustained += 1;
                                        if (ballCarrier >= 0 && ballCarrier == lastDefendingPlayerId)
                                        {
                                            GetStatsFor(lastBlockingPlayerId).Sacks += 1;
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
                                        GetStatsFor(lastDefendingPlayerId).CasSustained += 1;
                                        if (lastBlockingPlayerId >= 0)
                                        {
                                            GetStatsFor(lastBlockingPlayerId).CasInflicted += 1;
                                            if (lastCas == CasualtyOutcome.Dead)
                                                GetStatsFor(lastBlockingPlayerId).Kills += 1;
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
                                var attackerId = result.DocumentElement["AttackerId"].InnerText.ParseInt();
                                var defenderId = result.DocumentElement["DefenderId"].InnerText.ParseInt();
                                var outcome = (BlockOutcome)result.DocumentElement["Outcome"].InnerText.ParseInt();
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
                                            GetStatsFor(attackerId).Sacks += 1;
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
                                var injury = (InjuryOutcome)result.DocumentElement["Outcome"].InnerText.ParseInt();
                                Debug.WriteLine($">> Injury outcome {injury}");
                            }
                                break;
                            case "ResultCasualtyRoll":
                                var casualty = (CasualtyOutcome)result.DocumentElement["Outcome"].InnerText.ParseInt();
                                lastCas = casualty;
                                Debug.WriteLine($">> Casualty outcome {casualty}");
                                break;
                            case "ResultRaisedDead":
                                var zombieId = result.DocumentElement["RaisedPlayerId"].InnerText.ParseInt();
                                Debug.WriteLine($">> Raising {lastDeadPlayerId} as {zombieId}");
                                break;
                            case "ResultPlayerSentOff":
                            {
                                var sentOffId = result.DocumentElement["PlayerId"].InnerText.ParseInt();
                                GetStatsFor(sentOffId).Expulsions += 1;
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
                        GetStatsFor(passingPlayer).PassCompletions += 1;
                        passingPlayer = -1;
                        catchingPlayer = -1;
                    }
                }

                foreach (var touchdownEvent in replayStep.SelectNodes("EventTouchdown").Cast<XmlElement>())
                {
                    var playerId = touchdownEvent["PlayerId"].InnerText.ParseInt();
                    GetStatsFor(playerId).TouchdownsScored += 1;
                }

                if (replayStep.SelectSingleNode("BoardState/Ball") is XmlElement ballNode)
                {
                    if (ballNode["Carrier"] is XmlElement carrierNode)
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

            foreach (var playerData in replay.ReplayRoot.SelectNodes("//PlayerState").Cast<XmlElement>())
            {
                if (playerData["ExperienceGained"]?.InnerText.ParseInt() is { } xp and > 0)
                {
                    var id = playerData["Id"].InnerText.ParseInt();
                    var s = GetStatsFor(id);
                    if (s.SppEarned + 4 == xp)
                        s.Mvp = true;
                    s.SppEarned = xp;
//                    Console.WriteLine($"Player {id} ({replay.GetPlayer(id).Name}) gained {xp} xp");
                }
            }

            int fanAttendanceHome = replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/HomeRoll/Dice/Die/Value").InnerText.ParseInt();
            int fanAttendanceAway = replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/AwayRoll/Dice/Die/Value").InnerText.ParseInt();

            var properties = typeof(ZFLPlayerStats).GetProperties();

            csv?.WriteLine($"{replay.HomeTeam.Name} vs {replay.AwayTeam.Name}");
            csv?.WriteLine($" Fan attendance home: {fanAttendanceHome}");
            csv?.WriteLine($" Fan attendance away: {fanAttendanceAway}");
            csv?.WriteLine($"Player;{string.Join(';', properties.Select(p => p.Name))}");

            if (!silent) Console.WriteLine($"{replay.HomeTeam.Name} vs {replay.AwayTeam.Name}");
            if (!silent) Console.WriteLine($" Fan attendance: {fanAttendanceHome} / {fanAttendanceAway}");
            foreach (var playerId in replay.HomeTeam.Players.Keys.Concat(replay.AwayTeam.Players.Keys))
            {
                var playerStats = GetStatsFor(playerId);
                if (!silent) Console.WriteLine($"  {replay.GetPlayer(playerId).Name} (id={playerId}):");
                playerStats.PrintToConsole(4);
                if (playerStats.ExpectedSPP != playerStats.SppEarned)
                {
                    if (!silent) Console.WriteLine($"      !!! Expected {playerStats.ExpectedSPP}spp but found {playerStats.SppEarned}. Bug or prayer to Nuffle?");
                }

                csv?.WriteLine($"{replay.GetPlayer(playerId).Name};{string.Join(';', properties.Select(p => p.GetValue(playerStats)))}");
            }
            csv?.Flush();
        }

        csv?.Dispose();
        if (!Console.IsOutputRedirected && !Debugger.IsAttached)
        {
            Console.Error.Write("Press Enter to exit...");
            Console.Error.Flush();
            Console.ReadLine();
        }
    }

    public static int Main(string[] args)
    {
        var fileOrDirArg = new Argument<FileSystemInfo>("file or directory").ExistingOnly();
        var coachOpt = new Option<string>(["--coach", "-c"]);
        var teamOpt = new Option<string>(["--team", "-t"]);
        var csvOpt = new Option<FileInfo>("--csv");
        var autoCsvOpt = new Option<bool>("--auto-csv");
        var silentOpt = new Option<bool>("--silent");

        var rootCommand = new RootCommand
        {
            fileOrDirArg,
            coachOpt,
            teamOpt,
            csvOpt,
            autoCsvOpt,
            silentOpt
        };

        rootCommand.SetHandler(Run, fileOrDirArg, coachOpt, teamOpt, csvOpt, autoCsvOpt, silentOpt);

        return rootCommand.Invoke(args);
    }
}
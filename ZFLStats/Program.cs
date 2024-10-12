namespace ZFLStats;

using System.CommandLine;
using System.CommandLine.Binding;
using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using BloodBowl3;

public class Program
{
    private static readonly Dictionary<int, ZFLPlayerStats> Stats = [];

    private class OutputArguments
    {
        public bool Csv;
        public FileInfo? OutputCsv; 
        public bool Json;
        public FileInfo? OutputJson; 
        public bool AutoGenerate; 
        public bool Silent;
    }

    private static void Run(FileSystemInfo fileOrDir, string? coachFilter, string? teamFilter, OutputArguments args )
    {

        var csvFile = args.OutputCsv?.CreateText();
        var jsonFile = args.OutputJson?.CreateText();

        foreach (var replay in ReplayParser.GetReplays(fileOrDir, coachFilter, teamFilter))
        {
            if (replay == null) continue;
            InitPlayerStats(replay);

            StepResult stepResult = new();
            foreach (var replayStep in replay.ReplayRoot.SelectNodes("ReplayStep")!.Cast<XmlElement>())
            {
                foreach (var stepResultNode in replayStep.SelectNodes("EventExecuteSequence/Sequence/StepResult")!.Cast<XmlElement>())
                {
                    stepResult = ParseStepResult(stepResultNode, stepResult);
                }

                foreach (var touchdownEvent in replayStep.SelectNodes("EventTouchdown")!.Cast<XmlElement>())
                {
                    var playerId = touchdownEvent["PlayerId"]!.InnerText.ParseInt();
                    GetPlayerStats(playerId).TouchdownsScored += 1;
                }

                if (replayStep.SelectSingleNode("BoardState/Ball") is not XmlElement ballNode) continue;
                
                if (ballNode["Carrier"] is XmlElement carrierNode)
                {
                    var newCarrier = carrierNode.InnerText.ParseInt();
                    if (newCarrier != stepResult.BallCarrier)
                    {
                        stepResult.BallCarrier = newCarrier;
                        Debug.WriteLine($"* New ball carrier {newCarrier}!");
                    }
                }
                else if (stepResult.BallCarrier != -1)
                {
                    stepResult.BallCarrier = -1;
                    Debug.WriteLine("* Ball is loose!");
                }
            }

            foreach (var playerData in replay.ReplayRoot.SelectNodes("//PlayerState")!.Cast<XmlElement>())
            {
                if (playerData["ExperienceGained"]?.InnerText.ParseInt() is { } xp and > 0)
                {
                    var id = playerData["Id"]!.InnerText.ParseInt();
                    var s = GetPlayerStats(id);
                    if (s.SppEarned + 4 == xp)
                        s.Mvp = true;
                    s.SppEarned = xp;
                    //Console.WriteLine($"Player {id} ({replay.GetPlayer(id).Name}) gained {xp} xp");
                }
            }

            int fanAttendanceHome = replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/HomeRoll/Dice/Die/Value")!.InnerText.ParseInt();
            int fanAttendanceAway = replay.ReplayRoot.SelectSingleNode("ReplayStep/EventFanFactor/AwayRoll/Dice/Die/Value")!.InnerText.ParseInt();



            if (args.Csv){
                if (args.AutoGenerate){
                    csvFile?.Dispose();
                    csvFile = new FileInfo(replay.File.FullName.Replace("bbr", "csv")).CreateText();
                }
                WriteCsv(csvFile, args.Silent, replay.HomeTeam.Name, replay.AwayTeam.Name, fanAttendanceHome, fanAttendanceAway);
            }

            if (args.Json)
            {
                if (args.AutoGenerate){
                    jsonFile?.Dispose();
                    jsonFile = new FileInfo(replay.File.FullName.Replace("bbr", "json")).CreateText();
                }
                WriteJson(jsonFile, args.Silent, new {
                    id = replay.File.Name.Replace(".bbr",""),
                    home = new {
                        name = replay.HomeTeam.Name,
                        fans = fanAttendanceHome,
                        players = Stats.Values.Where(x => replay.HomeTeam.Players.Any(p => p.Value.Id == x.PlayerId)).OrderBy(x => x.PlayerId)
                    },
                    away = new {
                        name = replay.AwayTeam.Name,
                        fans = fanAttendanceAway,
                        players = Stats.Values.Where(x => replay.AwayTeam.Players.Any(p => p.Value.Id == x.PlayerId)).OrderBy(x => x.PlayerId)
                    }
                });
            }
        }

        csvFile?.Dispose();
        jsonFile?.Dispose();
        if (!Console.IsOutputRedirected && !Debugger.IsAttached)
        {
            Console.Error.Flush();
        }
    }

    private static void WriteCsv(StreamWriter? file, bool silent, string homeTeamName, string awayTeamName, int fanAttendanceHome, int fanAttendanceAway){
        var properties = typeof(ZFLPlayerStats).GetProperties();

        file?.WriteLine($"{homeTeamName} vs {awayTeamName}");
        file?.WriteLine($" Fan attendance home: {fanAttendanceHome}");
        file?.WriteLine($" Fan attendance away: {fanAttendanceAway}");
        file?.WriteLine($"Player;{string.Join(';', properties.Select(p => p.Name))}");

        if (!silent) Console.WriteLine($"{homeTeamName} vs {awayTeamName}");
        if (!silent) Console.WriteLine($" Fan attendance: {fanAttendanceHome} / {fanAttendanceAway}");
        
        foreach (var stats in Stats.Values)
        {
            if (!silent) Console.WriteLine($"  {stats.Name} (id={stats.PlayerId}):");
            if (!silent) stats.PrintToConsole(4);
            
            if (stats.ExpectedSPP != stats.SppEarned && !silent) Console.WriteLine($"      !!! Expected {stats.ExpectedSPP}spp but found {stats.SppEarned}. Bug or prayer to Nuffle?");

            file?.WriteLine($"{stats.Name};{string.Join(';', properties.Select(p => p.GetValue(stats)))}");
        }
        file?.Flush();

    }

    private static void WriteJson(StreamWriter? file, bool silent, object data)
    {
        var json = JsonSerializer.Serialize(data);
        file?.Write(json);
        file?.Flush();
        if (!silent) Console.WriteLine(json);
    }

    private static void InitPlayerStats(Replay replay)
    {
        foreach(var player in replay.HomeTeam.Players) Stats.TryAdd(player.Value.Id, new ZFLPlayerStats(player.Value.Id, player.Value.LobbyId, player.Value.Name));
        foreach(var player in replay.AwayTeam.Players) Stats.TryAdd(player.Value.Id, new ZFLPlayerStats(player.Value.Id, player.Value.LobbyId, player.Value.Name));
    }

    private static ZFLPlayerStats GetPlayerStats(int playerId)
    {
        Debug.Assert(playerId >= 0);
        if (!Stats.TryGetValue(playerId, out var playerStats))
        {
            playerStats = new ZFLPlayerStats(playerId,"","");
            Stats.Add(playerId, playerStats);
        }

        return playerStats;
    }

    private static StepResult ParseStepResult(XmlElement node, StepResult stepResult){

        var stepName = node["Step"]!["Name"]!.InnerText.FromBase64();
        var stepMsgData = node["Step"]!["MessageData"]!.InnerText.FromBase64().FromBase64();
        var step = new XmlDocument();
        step.LoadXml(stepMsgData);
        var stepType = (StepType)step.DocumentElement!["StepType"]!.InnerText.ParseInt();
        var playerId = step.DocumentElement["PlayerId"]?.InnerText.ParseInt() ?? -1;
        stepResult.TargetId = step.DocumentElement["TargetId"]?.InnerText.ParseInt() ?? -1;
        Debug.WriteLine($"{stepName}: {stepType}, player {playerId}, target {stepResult.TargetId}");
        switch (stepType)
        {
            case StepType.Kickoff:
                stepResult.LastBlockingPlayerId = -1;
                stepResult.LastDefendingPlayerId = -1;
                stepResult.PassingPlayer = -1;
                stepResult.CatchingPlayer = -1;
                stepResult.BallCarrier = -1;
                break;
            case StepType.Activation:
                stepResult.LastBlockingPlayerId = -1;
                stepResult.LastDefendingPlayerId = -1;
                stepResult.PassingPlayer = -1;
                stepResult.CatchingPlayer = -1;
                break;
            case StepType.Move:
                stepResult.LastBlockingPlayerId = -1;
                stepResult.LastDefendingPlayerId = -1;
                stepResult.PassingPlayer = -1;
                stepResult.CatchingPlayer = -1;
                stepResult.MovingPlayer = playerId;
                break;
            case StepType.Damage:
                break;
            case StepType.Block:
                stepResult.LastBlockingPlayerId = playerId;
                stepResult.LastDefendingPlayerId = stepResult.TargetId;
                stepResult.PassingPlayer = -1;
                stepResult.CatchingPlayer = -1;
                break;
            case StepType.Pass:
                stepResult.PassingPlayer = playerId;
                stepResult.CatchingPlayer = stepResult.TargetId;
                break;
            case StepType.Catch:
                break;
            case StepType.Foul:
                stepResult.LastBlockingPlayerId = -1;
                stepResult.LastDefendingPlayerId = -1;
                stepResult.PassingPlayer = -1;
                stepResult.CatchingPlayer = -1;
                GetPlayerStats(playerId).FoulsInflicted += 1;
                GetPlayerStats(stepResult.TargetId).FoulsSustained += 1;
                break;
            case StepType.Referee:
                break;
            default:
                break;
        }

        stepResult.LastCas = null;
        stepResult.LastDeadPlayerId = -1;

        stepResult.CatchSuccess = false;

        foreach (var results in node.SelectNodes("Results/StringMessage")!.Cast<XmlElement>())
        {
            var resultsName = results["Name"]!.InnerText.FromBase64();
            var resultsMsgData = results["MessageData"]!.InnerText.FromBase64().FromBase64();
            var result = new XmlDocument();
            result.LoadXml(resultsMsgData);
            if (result.DocumentElement == null) continue;

            switch (resultsName)
            {
                case "ResultSkillUsage":
                    ParseResultSkillUsage(result);
                    break;
                case "ResultMoveOutcome":
                    if (result.DocumentElement.SelectSingleNode("Rolls/RollSummary/Outcome")?.InnerText == "0")
                    {
                        GetPlayerStats(stepResult.MovingPlayer).DodgeTurnovers += 1;
                    }

                    Debug.WriteLine("ResultMoveOutcome");
                    break;
                case "ResultRoll":
                    ParseResultRoll(result,stepResult);
                    break;
                case "QuestionBlockDice":
                    ParseQuestionBlockDice(result,stepResult);
                    break;
                case "ResultBlockRoll":
                    var dieValue = result.DocumentElement.SelectSingleNode("Die/Value")!.InnerText.ParseInt();
                    Debug.WriteLine($">> Block die {dieValue}");
                    break;
                case "ResultPlayerRemoval":
                    ParseResultPlayerRemoval(result,stepResult);
                    break;
                case "ResultBlockOutcome":
                    ParseResultBlockOutcome(result, stepResult);
                    break;
                case "ResultInjuryRoll":
                {
                    var injury = (InjuryOutcome)result.DocumentElement["Outcome"]!.InnerText.ParseInt();
                    Debug.WriteLine($">> Injury outcome {injury}");
                }
                    break;
                case "ResultCasualtyRoll":
                    stepResult.LastCas = (CasualtyOutcome)result.DocumentElement["Outcome"]!.InnerText.ParseInt();
                    Debug.WriteLine($">> Casualty outcome {stepResult.LastCas}");
                    break;
                case "ResultRaisedDead":
                    var zombieId = result.DocumentElement["RaisedPlayerId"]!.InnerText.ParseInt();
                    Debug.WriteLine($">> Raising {stepResult.LastDeadPlayerId} as {zombieId}");
                    break;
                case "ResultPlayerSentOff":
                {
                    var sentOffId = result.DocumentElement["PlayerId"]!.InnerText.ParseInt();
                    GetPlayerStats(sentOffId).Expulsions += 1;
                    Debug.WriteLine($">> Sending {sentOffId} off the pitch");
                }
                    break;
                default:
                    Debug.WriteLine(resultsName);
                    break;
            }
        }

        if (stepType == StepType.Catch && stepResult.PassingPlayer >= 0 && stepResult.CatchingPlayer >= 0 && stepResult.CatchSuccess)
        {
            GetPlayerStats(stepResult.PassingPlayer).PassCompletions += 1;
            stepResult.PassingPlayer = -1;
            stepResult.CatchingPlayer = -1;
        }
        return stepResult;

    }

    private static void ParseResultSkillUsage(XmlDocument doc)
    {
        var playerId = doc.DocumentElement!["PlayerId"]?.InnerText.ParseInt() ?? -1;

        var skill = (Skill)doc.DocumentElement.SelectSingleNode("Skill")!.InnerText.ParseInt();
        var used = doc.DocumentElement.SelectSingleNode("Used")!.InnerText == "1";
        if (used && skill == Skill.StripBall)
        {
            GetPlayerStats(playerId).Sacks += 1;
        }
        Debug.WriteLine($"ResultSkillUsage {skill} used? {used}");
    }

    private static void ParseResultRoll(XmlDocument doc, StepResult stepResult)
    {
        var dice = doc.DocumentElement!.SelectNodes("Dice/Die")!.Cast<XmlElement>().ToArray();
        var dieType = (DieType)dice[0]["DieType"]!.InnerText.ParseInt();
        var values = dice.Select(d => d["Value"]!.InnerText.ParseInt()).ToArray();
        var failed = doc.DocumentElement["Outcome"]!.InnerText == "0";
        var rollType = (RollType)doc.DocumentElement["RollType"]!.InnerText.ParseInt();

        // Pass and catch reroll seem to be handled differently??
        if (failed && rollType == RollType.Pass)
        {
            stepResult.PassingPlayer = -1;
            stepResult.CatchingPlayer = -1;
        }

        if (!failed && rollType == RollType.Catch)
        {
            stepResult.CatchSuccess = true;
        }

        if (rollType == RollType.Armor)
        {
            GetPlayerStats(stepResult.TargetId).ArmorRollsSustained += 1;
        }
        
        Debug.WriteLine($">> {rollType} {dieType} rolls: {string.Join(", ", values)}");
    }

    private static void ParseQuestionBlockDice(XmlDocument doc, StepResult stepResult)
    {
        var dice = doc.DocumentElement!.SelectNodes("Dice/Die")!.Cast<XmlElement>().ToArray();
        var dieType = (DieType) dice[0]["DieType"]!.InnerText.ParseInt();
        Debug.Assert(dieType == DieType.Block);
        var values = dice.Select(d => d["Value"]!.InnerText.ParseInt()).ToArray();
        Debug.WriteLine($">> Picking block dice: {string.Join(", ", values)}");
        if (values.Length >= 2 && values.All(v => v == 0))
            GetPlayerStats(stepResult.LastBlockingPlayerId).DubskullsRolled += 1;
    }

    private static void ParseResultPlayerRemoval(XmlDocument doc, StepResult stepResult)
    {
        var playerIdR = doc.DocumentElement!["PlayerId"]?.InnerText.ParseInt() ?? -1;
        var reason = doc.DocumentElement["Reason"]?.InnerXml.ParseInt() ?? -1;

        var situation = (PlayerSituation)doc.DocumentElement["Situation"]!.InnerText.ParseInt();
        if (situation == PlayerSituation.Reserve)
        {
            Debug.WriteLine($">> Surf by {stepResult.LastBlockingPlayerId} on {stepResult.LastDefendingPlayerId}");
            if (stepResult.LastBlockingPlayerId >= 0 && stepResult.LastDefendingPlayerId >= 0)
            {
                GetPlayerStats(stepResult.LastBlockingPlayerId).SurfsInflicted += 1;
                GetPlayerStats(stepResult.LastDefendingPlayerId).SurfsSustained += 1;
                if (stepResult.BallCarrier >= 0 && stepResult.BallCarrier == stepResult.LastDefendingPlayerId)
                {
                    GetPlayerStats(stepResult.LastBlockingPlayerId).Sacks += 1;
                }
            }

            stepResult.LastBlockingPlayerId = -1;
            stepResult.LastDefendingPlayerId = -1;
            stepResult.LastBlockOutcome = null;
        }
        else
        {
            Debug.WriteLine($">> Removing {playerIdR}, situation {situation}, reason {reason}");
            if (situation == PlayerSituation.Injured && playerIdR == stepResult.LastDefendingPlayerId)
            {
                GetPlayerStats(stepResult.LastDefendingPlayerId).CasSustained += 1;
                if (stepResult.LastBlockingPlayerId >= 0)
                {
                    GetPlayerStats(stepResult.LastBlockingPlayerId).CasInflicted += 1;
                    if (stepResult.LastCas == CasualtyOutcome.Dead)
                        GetPlayerStats(stepResult.LastBlockingPlayerId).Kills += 1;
                }
            }

            if (stepResult.LastCas == CasualtyOutcome.Dead)
            {
                stepResult.LastDeadPlayerId = playerIdR;
            }
        }

        stepResult.LastCas = null;

    }


    private static void ParseResultBlockOutcome(XmlDocument doc, StepResult stepResult)
    {
        var defenderId = doc.DocumentElement!["DefenderId"]!.InnerText.ParseInt();

        if (stepResult.BallCarrier < 0 || defenderId != stepResult.BallCarrier) return;

        var attackerId = doc.DocumentElement["AttackerId"]!.InnerText.ParseInt();
        var outcome = (BlockOutcome)doc.DocumentElement["Outcome"]!.InnerText.ParseInt();
        stepResult.LastBlockingPlayerId = attackerId;
        stepResult.LastDefendingPlayerId = defenderId;
        stepResult.LastBlockOutcome = outcome;

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
                GetPlayerStats(attackerId).Sacks += 1;
                break;
            default:
                throw new ArgumentOutOfRangeException($"Block outcome: {outcome}");
        }

        Debug.WriteLine($">> Block by {attackerId} on {defenderId}, outcome {outcome}");
    }


    private class OutputArgumentBinder: BinderBase<OutputArguments>
    {

        private readonly Option<bool> _csvOpt;
        private readonly Option<FileInfo> _outputCsvtOpt;
        private readonly Option<bool> _jsonOpt;
        private readonly Option<FileInfo> _outputJsonOpt;
        private readonly Option<bool> _autoOpt;
        private readonly Option<bool> _silentOpt;

        public OutputArgumentBinder(Option<bool> csvOpt,Option<FileInfo> outputCsvtOpt,Option<bool> jsonOpt,Option<FileInfo> outputJsonOpt,Option<bool> autoOpt,Option<bool> silentOpt){
            _csvOpt =  csvOpt;
            _outputCsvtOpt =  outputCsvtOpt;
            _jsonOpt =  jsonOpt;
            _outputJsonOpt =  outputJsonOpt;
            _autoOpt =  autoOpt;
            _silentOpt =  silentOpt;
        }

        protected override OutputArguments GetBoundValue(BindingContext bindingContext) => new OutputArguments{
            Csv = bindingContext.ParseResult.GetValueForOption(_csvOpt),
            OutputCsv = bindingContext.ParseResult.GetValueForOption(_outputCsvtOpt),
            Json = bindingContext.ParseResult.GetValueForOption(_jsonOpt),
            OutputJson = bindingContext.ParseResult.GetValueForOption(_outputJsonOpt),
            AutoGenerate = bindingContext.ParseResult.GetValueForOption(_autoOpt),
            Silent = bindingContext.ParseResult.GetValueForOption(_silentOpt)
        };
    }
    public static int Main(string[] args)
    {
        var fileOrDirArg = new Argument<FileSystemInfo>("file or directory").ExistingOnly();
        var coachOpt = new Option<string>(["--coach", "-c"]);
        var teamOpt = new Option<string>(["--team", "-t"]);
        var csvOpt = new Option<bool>("--csv");
        var outputCsvtOpt = new Option<FileInfo>("--outputCsv");
        var jsonOpt = new Option<bool>("--json");
        var outputJsonOpt = new Option<FileInfo>("--outputJson");

        var autoOpt = new Option<bool>("--auto");
        var silentOpt = new Option<bool>("--silent");

        var rootCommand = new RootCommand
        {
            fileOrDirArg,
            coachOpt,
            teamOpt,
            csvOpt,
            outputCsvtOpt,
            jsonOpt,
            outputJsonOpt,
            autoOpt,
            silentOpt
        };

        rootCommand.SetHandler(Run, fileOrDirArg, coachOpt, teamOpt, new OutputArgumentBinder(csvOpt, outputCsvtOpt, jsonOpt, outputJsonOpt, autoOpt, silentOpt));

        return rootCommand.Invoke(args);
    }
}
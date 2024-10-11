using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace BloodBowl3;

public static class ReplayParser
{
    public static IEnumerable<Replay> GetReplays(FileSystemInfo fileOrDir, string? coachFilter = null, string? teamFilter = null)
    {
        if (fileOrDir is DirectoryInfo dir)
        {
            var replayTasks = LoadReplaysFromFolderAsync(dir, coachFilter, teamFilter).ToArray();
            Task.WaitAll(replayTasks.ToArray<Task>());
            return replayTasks.Select(t => t.Result).ToList();
        }

        var doc = LoadDocument((FileInfo)fileOrDir);
        return doc.DocumentElement != null ? new[] { GetReplayAsync((FileInfo)fileOrDir, doc.DocumentElement).Result } : new List<Replay>();
    }

    private static IEnumerable<Task<Replay>> LoadReplaysFromFolderAsync(DirectoryInfo dir, string? coachFilter = null, string? teamFilter = null)
    {
        var coachPattern = string.IsNullOrEmpty(coachFilter) ? null : new Regex(coachFilter, RegexOptions.IgnoreCase);
        var teamPattern =  string.IsNullOrEmpty(teamFilter) ? null : new Regex(teamFilter, RegexOptions.IgnoreCase);

        foreach (var path in dir.EnumerateFiles("*.bbr"))
        {
            var doc = LoadDocument(path);
            var coaches = GetCoachNames(doc.DocumentElement!).ToArray();
            var teamNames = GetTeamNames(doc.DocumentElement!).ToArray();
            var teamMatches = teamPattern == null || teamNames.Any(teamPattern.IsMatch);
            var coachMatches = coachPattern == null || coaches.Any(coachPattern.IsMatch);
            if (teamMatches && coachMatches)
            {
                yield return GetReplayAsync(path, doc.DocumentElement!);
            }
        }
    }

    private static Task<Replay> GetReplayAsync(FileInfo file, XmlElement root)
    {
        return Task.Run(() => GetReplayImpl(file, root));
    }

    private static XmlDocument LoadDocument(FileInfo path)
    {
        var doc = new XmlDocument();

        // TODO base64 decode lazily
        var contents = File.ReadAllText(path.FullName);
        var bytes = Convert.FromBase64String(contents);
        using var ms = new MemoryStream();
        ms.Write(bytes);
        ms.Position = 0;
        var zs = new ZLibStream(ms, CompressionMode.Decompress, true);

//#if DEBUG
//        if (!File.Exists(path.FullName + ".xml"))
//        {
//            using var fs = File.Create(path.FullName + ".xml");
//            zs.CopyTo(fs);
//            zs.Close();
//            ms.Position = 0;
//            zs = new ZLibStream(ms, CompressionMode.Decompress);
//        }
//#endif

        doc.Load(zs);
        zs.Close();
        return doc;
    }

    private static Replay GetReplayImpl(FileInfo file, XmlElement root)
    {
        var competitionName = "";
        var homeCoach = "";
        var awayCoach = "";
        Team? homeTeam = null;
        Team? awayTeam = null;

        if (root.SelectSingleNode("NotificationGameJoined/GameInfos") is XmlElement gameInfos)
        {
            if (gameInfos.SelectSingleNode("Competition/CompetitionInfos") is XmlElement compInfos)
            {
                competitionName = compInfos["Name"]!.InnerText.FromBase64();
            }

            if (gameInfos.SelectSingleNode("GamersInfos") is XmlElement gamersInfos)
            {
                var coaches = gamersInfos.SelectNodes("GamerInfos/Name")!.Cast<XmlNode>().ToArray();
                homeCoach = coaches[0].InnerText.FromBase64();
                awayCoach = coaches[1].InnerText.FromBase64();

                var teamNameNodes = gamersInfos.SelectNodes("GamerInfos/Roster/Name")!.Cast<XmlElement>().ToArray();
                homeTeam = new Team( teamNameNodes[0].InnerText.FromBase64(),  homeCoach);
                awayTeam = new Team (teamNameNodes[1].InnerText.FromBase64(), awayCoach);
                
            }
        }


        var teamPlayerLists = root.SelectNodes("NotificationGameJoined/InitialBoardState/ListTeams/TeamState/ListPitchPlayers")!
                .Cast<XmlElement>().ToArray();

        foreach (var playerData in teamPlayerLists[0].SelectNodes("PlayerState/Data")!.Cast<XmlElement>())
        {
            var player = GetPlayer(0, playerData);
            homeTeam!.Players.TryAdd(player.Id, player);
        }

        foreach (var playerData in teamPlayerLists[1].SelectNodes("PlayerState/Data")!.Cast<XmlElement>())
        {
            var player = GetPlayer(1, playerData);
            awayTeam!.Players.TryAdd(player.Id, player);
        }


        return new Replay
        {
            File = file,
            ClientVersion = root.SelectSingleNode("ClientVersion")!.InnerText,
            ReplayRoot = root,
            CompetitionName = competitionName,
            HomeCoach = homeCoach,
            AwayCoach = awayCoach,
            HomeTeam = homeTeam!,
            AwayTeam = awayTeam!
        };
    }

    private static Player GetPlayer(int team, XmlNode playerData)
    {
        return new Player(team, playerData["Id"]!.InnerText.ParseInt(), playerData["Name"]!.InnerText.FromBase64());
    }

    private static IEnumerable<string> GetTeamNames(XmlNode doc)
    {
        return doc.SelectNodes("NotificationGameJoined/GameInfos/GamersInfos/GamerInfos/Roster/Name")!.Cast<XmlElement>().Select(node => node.InnerText.FromBase64());
    }

    private static IEnumerable<string> GetCoachNames(XmlNode doc)
    {
        return doc.SelectNodes("NotificationGameJoined/GameInfos/GamersInfos/GamerInfos/Name")!.Cast<XmlElement>().Select(node => node.InnerText.FromBase64());
    }
}
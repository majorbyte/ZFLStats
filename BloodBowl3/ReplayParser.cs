using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace BloodBowl3;

public static class ReplayParser
{
    public static async IAsyncEnumerable<Replay> GetReplays(FileSystemInfo fileOrDir, string? coachFilter = null, string? teamFilter = null)
    {
        var replayTasks = new HashSet<Task<Replay>>();

        if (fileOrDir is DirectoryInfo dir)
        {
            Regex? coachPattern = null, teamPattern = null;
            if (!string.IsNullOrEmpty(coachFilter))
            {
                coachPattern = new Regex(coachFilter, RegexOptions.IgnoreCase);
            }

            if (!string.IsNullOrEmpty(teamFilter))
            {
                teamPattern = new Regex(teamFilter, RegexOptions.IgnoreCase);
            }

            foreach (var path in dir.EnumerateFiles("*.bbr"))
            {
                var doc = LoadDocument(path);
                var coaches = GetCoachNames(doc.DocumentElement!).ToArray();
                var teamNames = GetTeamNames(doc.DocumentElement!).ToArray();
                var teamMatches = teamPattern == null || teamNames.Any(teamPattern.IsMatch);
                var coachMatches = coachPattern == null || coaches.Any(coachPattern.IsMatch);
                if (teamMatches && coachMatches)
                {
                    replayTasks.Add(GetReplayAsync(path, doc.DocumentElement!));
                }
            }
        }
        else
        {
            var doc = LoadDocument((FileInfo)fileOrDir);
            replayTasks.Add(GetReplayAsync((FileInfo)fileOrDir, doc.DocumentElement!));
        }

        while (replayTasks.Count > 0)
        {
            var task = await Task.WhenAny(replayTasks);
            replayTasks.Remove(task);
            yield return await task;
        }
    }

    public static Task<Replay> GetReplayAsync(FileInfo file, XmlElement root)
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
        string competitionName = "", homeCoach = "", awayCoach = "";
        Team? homeTeam = null, awayTeam = null;
        
        if (root.SelectSingleNode("NotificationGameJoined/GameInfos") is XmlElement gameInfos)
        {
            if (gameInfos.SelectSingleNode("Competition/CompetitionInfos") is XmlElement compInfos)
            {
                competitionName = compInfos["Name"]?.InnerText.FromBase64() ?? "";
            }

            if (gameInfos.SelectSingleNode("GamersInfos") is XmlElement gamersInfos)
            {
                var coaches = gamersInfos.SelectNodes("GamerInfos/Name")!.Cast<XmlNode>().ToArray();
                homeCoach = coaches[0].InnerText.FromBase64();
                awayCoach = coaches[1].InnerText.FromBase64();

                var teamNameNodes = gamersInfos.SelectNodes("GamerInfos/Roster/Name")!.Cast<XmlElement>().ToArray();
                homeTeam = new Team(teamNameNodes[0].InnerText.FromBase64(), homeCoach);
                awayTeam = new Team(teamNameNodes[1].InnerText.FromBase64(), awayCoach);
            }
        }

        const string path = "EndGame/RulesEventGameFinished/MatchResult/GamerResults/GamerResult/TeamResult/PlayerResults";
        var teamPlayerLists = root.SelectNodes(path)!.Cast<XmlElement>().ToArray();

        foreach (var playerData in teamPlayerLists[0].SelectNodes("PlayerResult/PlayerData")!.Cast<XmlElement>())
        {
            var player = GetPlayer(0, playerData);
            homeTeam!.Players.TryAdd(player.Id, player);
        }
        foreach (var playerData in teamPlayerLists[1].SelectNodes("PlayerResult/PlayerData")!.Cast<XmlElement>())
        {
            var player = GetPlayer(1, playerData);
            awayTeam!.Players.TryAdd(player.Id, player);
        }

        return new Replay(file, root.SelectSingleNode("ClientVersion")!.InnerText, root)
        {
            HomeCoach = homeCoach, AwayCoach = awayCoach, HomeTeam = homeTeam!,  AwayTeam = awayTeam!,
            CompetitionName = competitionName
        };
    }

    private static Player GetPlayer(int team, XmlNode playerData)
    {
        return new Player(team, playerData["Id"]!.InnerText.ParseInt(), playerData["Name"]!.InnerText.FromBase64(), playerData["LobbyId"]?.InnerText.FromBase64());
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
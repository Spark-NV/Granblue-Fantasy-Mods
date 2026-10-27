using System.Globalization;

namespace Spark.Gbfr.Mods;

internal static class FsmTimerPatch
{
    private const double WarningWindowSeconds = 60;

    public static (byte[] Data, int Checks)? Patch(byte[] raw, int originalLimitSeconds,
        IReadOnlyCollection<int> originalObjectives, int deltaSeconds)
    {
        var root = MsgPack.Parse(raw);
        if (root.Pairs is null)
            return null;

        var checks = root.Pairs
            .Where(p => p.Key.Text == "CheckTimer" && p.Value.Pairs is not null)
            .Select(p => MsgPack.Fields(p.Value))
            .Where(f => f.ContainsKey("timerId_") && f.ContainsKey("time_"))
            .ToList();

        var limits = checks
            .Where(f => (f.TryGetValue("isBasedQuestLimitTimer_", out var flag) && MsgPack.ReadBool(raw, flag) == true)
                || MsgPack.ReadNumber(raw, f["time_"]) == originalLimitSeconds)
            .ToList();
        var replacements = new List<(int Start, int End, byte[] Bytes)>();
        foreach (var check in checks)
        {
            if (MsgPack.ReadNumber(raw, check["time_"]) is double time && originalObjectives.Contains((int)time)
                && time == Math.Floor(time) && !limits.Contains(check))
            {
                var node = check["time_"];
                replacements.Add((node.Start, node.End, MsgPack.EncodeNumberLike(raw, node, time + deltaSeconds)));
            }
        }

        foreach (var limit in limits)
        {
            var timerId = MsgPack.ReadNumber(raw, limit["timerId_"]);
            var limitTime = MsgPack.ReadNumber(raw, limit["time_"]);
            if (timerId is null || limitTime is not double limitSeconds || limitSeconds <= 0)
                continue;

            foreach (var check in checks)
            {
                if (MsgPack.ReadNumber(raw, check["timerId_"]) != timerId
                    || MsgPack.ReadNumber(raw, check["time_"]) is not double time
                    || time < limitSeconds - WarningWindowSeconds || time > limitSeconds)
                    continue;

                var node = check["time_"];
                if (replacements.Any(r => r.Start == node.Start))
                    continue;
                replacements.Add((node.Start, node.End, MsgPack.EncodeNumberLike(raw, node, time + deltaSeconds)));
            }
        }

        if (replacements.Count == 0)
            return null;

        var patched = new List<byte>(raw);
        foreach (var (start, end, bytes) in replacements.OrderByDescending(r => r.Start))
        {
            patched.RemoveRange(start, end - start);
            patched.InsertRange(start, bytes);
        }

        byte[] result = patched.ToArray();
        if (MsgPack.Parse(result).End != result.Length)
            throw new InvalidDataException("patched FSM did not parse to its final byte");
        return (result, replacements.Count);
    }

    public static string? QuestIdOf(string path)
    {
        var parts = Path.GetFileName(path).Split('_');
        return parts.Length >= 3 && parts[0] == "quest" ? parts[1].ToLower(CultureInfo.InvariantCulture) : null;
    }
}

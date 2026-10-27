using System.Globalization;

namespace Spark.Gbfr.Mods;

internal static class QuestTimerPatch
{
    private const string CompletionTimeObjectiveType = "2";

    public static (byte[] Data, int OriginalSeconds, IReadOnlyList<int> OriginalObjectives)? Patch(byte[] raw, int deltaSeconds)
    {
        var root = MsgPack.Parse(raw);
        var rootFields = MsgPack.Fields(root);
        if (!rootFields.TryGetValue("time_", out var timer) || PositiveDecimal(timer) is not int seconds)
            return null;

        var values = new List<(MsgPack.Node Node, int Value)> { (timer, seconds + deltaSeconds) };
        var objectives = new List<int>();

        if (rootFields.TryGetValue("targetList_", out var targets) && targets.Items is not null)
        {
            foreach (var target in targets.Items)
            {
                var targetFields = MsgPack.Fields(target);
                if (!targetFields.TryGetValue("type_", out var type) || type.Text != CompletionTimeObjectiveType)
                    continue;
                if (!targetFields.TryGetValue("count_", out var count) || PositiveDecimal(count) is not int oldCount)
                    continue;
                values.Add((count, oldCount + deltaSeconds));
                objectives.Add(oldCount);
            }
        }

        byte[] result = MsgPack.ReplaceStringsSameSize(raw, root,
            values.Select(v => (v.Node, v.Value.ToString(CultureInfo.InvariantCulture))).ToList());
        return (result, seconds, objectives);
    }

    private static int? PositiveDecimal(MsgPack.Node node) =>
        node.Kind == "str" && int.TryParse(node.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : null;
}

namespace Spark.Gbfr.Mods;

internal static class QuestObjectivePatch
{

    private const string ThrownType = "26";
    private const string ThrownText = "TXT_QT_SM0090_00";
    private const string DefeatType = "3";
    private const string DefeatText = "TXT_QT_SM0390_00";
    private const string AnyEnemyId = "4294967295";

    public static (byte[] Data, int Swapped)? Patch(byte[] raw, int defeatCount)
    {
        var root = MsgPack.Parse(raw);
        if (!MsgPack.Fields(root).TryGetValue("targetList_", out var targets) || targets.Items is null)
            return null;

        var values = new List<(MsgPack.Node Node, string Value)>();
        int swapped = 0;
        foreach (var target in targets.Items)
        {
            var fields = MsgPack.Fields(target);
            if (!fields.TryGetValue("type_", out var type) || type.Text != ThrownType
                || !fields.TryGetValue("textLabel_", out var text) || text.Text != ThrownText
                || !fields.TryGetValue("id_", out var id) || id.Kind != "str"
                || !fields.TryGetValue("count_", out var count) || count.Kind != "str")
                continue;

            values.Add((type, DefeatType));
            values.Add((text, DefeatText));
            values.Add((id, AnyEnemyId));
            values.Add((count, defeatCount.ToString()));
            swapped++;
        }

        return swapped == 0 ? null : (MsgPack.ReplaceStringsSameSize(raw, root, values), swapped);
    }
}

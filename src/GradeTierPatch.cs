using System.Buffers.Binary;

namespace Spark.Gbfr.Mods;

internal static class GradeTierPatch
{
    public const string QuestTablePath = "system/table/quest_baseinfo_ex_data.tbl";

    private const int RowSize = 112;
    private const int TimeTiersOffset = 0x10;
    private const int KillTiersOffset = 0x24;
    private const int HpTiersOffset = 0x38;
    private const int TierCount = 5;

    public sealed record Counts(int TimeQuests, int KillQuests, int HpQuests);

    public static Counts? Apply(byte[] table, int easierPercent)
    {
        if (table.Length < 8)
            return null;

        var rows = BinaryPrimitives.ReadInt64LittleEndian(table);
        if (rows <= 0 || 8 + rows * RowSize != table.LongLength)
            return null;

        uint percent = (uint)easierPercent;
        int time = 0, kills = 0, hp = 0;
        for (var row = 0L; row < rows; row++)
        {
            var record = table.AsSpan(checked((int)(8 + row * RowSize)), RowSize);
            if (Scale(record.Slice(TimeTiersOffset, TierCount * 4), v => (v * (100 + percent) + 99) / 100))
                time++;
            if (Scale(record.Slice(KillTiersOffset, TierCount * 4), v => Math.Max(1, v * (100 - percent) / 100)))
                kills++;
            if (Scale(record.Slice(HpTiersOffset, TierCount * 4), v => v * (100 - percent) / 100))
                hp++;
        }

        return new Counts(time, kills, hp);
    }

    private static bool Scale(Span<byte> tiers, Func<uint, uint> adjust)
    {
        bool used = false;
        for (int i = 0; i < TierCount; i++)
        {
            var field = tiers.Slice(i * 4, 4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(field);
            if (value == 0)
                continue;
            BinaryPrimitives.WriteUInt32LittleEndian(field, adjust(value));
            used = true;
        }
        return used;
    }
}

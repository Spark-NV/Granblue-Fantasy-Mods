using System.Buffers.Binary;

namespace Spark.Gbfr.Mods;

internal static class FateEpisodePatch
{
    public const string FateEpisodeTablePath = "system/table/fate_episode.tbl";

    private const int RowSize = 96;
    private const int ReqQuestIdOffset = 0x48;
    private const int MissionQuestIdOffset = 0x4C;
    private const int ReqLevelOffset = 0x58;
    private const int UnlockedLevel = 1;

    public sealed record Counts(int Episodes, int QuestGates, int MissionGates, int LevelGates);

    public static Counts? Apply(byte[] table)
    {
        if (table.Length < 8)
            return null;

        var rows = BinaryPrimitives.ReadInt64LittleEndian(table);
        if (rows <= 0 || 8 + rows * RowSize != table.LongLength)
            return null;

        int questGates = 0, missionGates = 0, levelGates = 0;
        for (var row = 0L; row < rows; row++)
        {
            var record = table.AsSpan(checked((int)(8 + row * RowSize)), RowSize);
            if (Clear(record.Slice(ReqQuestIdOffset, 4)))
                questGates++;
            if (Clear(record.Slice(MissionQuestIdOffset, 4)))
                missionGates++;

            var level = record.Slice(ReqLevelOffset, 4);
            if (BinaryPrimitives.ReadInt32LittleEndian(level) > UnlockedLevel)
            {
                BinaryPrimitives.WriteInt32LittleEndian(level, UnlockedLevel);
                levelGates++;
            }
        }

        return new Counts((int)rows, questGates, missionGates, levelGates);
    }

    private static bool Clear(Span<byte> field)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(field) == 0)
            return false;
        BinaryPrimitives.WriteUInt32LittleEndian(field, 0);
        return true;
    }
}

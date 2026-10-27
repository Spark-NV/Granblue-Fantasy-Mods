using System.Buffers.Binary;

namespace Spark.Gbfr.Mods;

internal static class ItemCapPatch
{
    public const string ItemCategoryTablePath = "system/table/item_category.tbl";
    public const uint OriginalCap = 999;

    private const int RowSize = 12;
    private const int MaxHoldableOffset = 0x04;

    public static int? Apply(byte[] table, uint newCap)
    {
        if (table.Length < 8)
            return null;

        var rows = BinaryPrimitives.ReadInt64LittleEndian(table);
        if (rows <= 0 || 8 + rows * RowSize != table.LongLength)
            return null;

        int raised = 0;
        for (var row = 0L; row < rows; row++)
        {
            var field = table.AsSpan(checked((int)(8 + row * RowSize + MaxHoldableOffset)), 4);
            if (BinaryPrimitives.ReadUInt32LittleEndian(field) != OriginalCap)
                continue;
            BinaryPrimitives.WriteUInt32LittleEndian(field, newCap);
            raised++;
        }

        return raised;
    }
}

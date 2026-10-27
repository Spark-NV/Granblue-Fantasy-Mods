using System.Buffers.Binary;
using System.Text;

namespace Spark.Gbfr.Mods;

internal static class CaptainUnlockPatch
{
    public const string FormationTablePath = "system/table/formation_slot.tbl";

    private const uint EmptyHash = 0x887AE0B0;
    private const int RowSize = 64;
    private const int PlayerCharaOffset = 0x00;
    private const int LockSlotsOffset = 0x2C;
    private const int KeyOffset = 0x3C;
    private static readonly string[] StoryPresets = ["FSLOT_HERO_PARTY", "FSLOT_FIRST_PLAY"];

    public static int? ApplyFormationTable(byte[] table)
    {
        if (table.Length < 8)
            return null;
        var rows = BinaryPrimitives.ReadInt64LittleEndian(table);
        if (rows <= 0 || 8 + rows * RowSize != table.LongLength)
            return null;

        var keys = StoryPresets.Select(GameHash).ToHashSet();
        int freed = 0;
        for (var row = 0L; row < rows; row++)
        {
            var record = table.AsSpan(checked((int)(8 + row * RowSize)), RowSize);
            if (!keys.Contains(BinaryPrimitives.ReadUInt32LittleEndian(record[KeyOffset..])))
                continue;
            BinaryPrimitives.WriteUInt32LittleEndian(record[PlayerCharaOffset..], EmptyHash);
            record.Slice(LockSlotsOffset, 16).Clear();
            freed++;
        }
        return freed == StoryPresets.Length ? freed : null;
    }

    public static (byte[] Data, int Sections)? ApplyStoryFsm(byte[] raw)
    {
        var root = MsgPack.Parse(raw);
        if (root.Pairs is null)
            return null;

        byte[] result = (byte[])raw.Clone();
        int sections = 0;
        foreach (var (key, value) in root.Pairs)
        {
            if (key.Text != "BeginSection"
                || !MsgPack.Fields(value).TryGetValue("partyCharaType_", out var slots)
                || slots.Items is not { Count: 4 })
                continue;

            var elements = slots.Items.Select(s => MsgPack.Fields(s).GetValueOrDefault("Element")).ToList();
            if (elements.Any(e => e is null))
                continue;
            var types = elements.Select(e => MsgPack.ReadNumber(raw, e!)).ToList();
            if (types[0] != 2 || types.Skip(1).Any(t => t != 1) || raw[elements[0]!.Start] != 0x02)
                continue;

            result[elements[0]!.Start] = 0x01;
            sections++;
        }

        if (sections == 0)
            return null;
        if (MsgPack.Parse(result).End != result.Length)
            throw new InvalidDataException("story party edit did not parse to its final byte");
        return (result, sections);
    }

    public static uint GameHash(string text)
    {
        const uint Prime1 = 0x9E3779B1, Prime2 = 0x85EBCA77, Prime3 = 0xC2B2AE3D, Prime4 = 0x27D4EB2F, Prime5 = 0x165667B1;
        static uint Rotl(uint x, int r) => x << r | x >> (32 - r);
        static uint Round(uint seed, uint input) => Rotl(seed + input * Prime2, 13) * Prime1;

        ReadOnlySpan<byte> input = Encoding.ASCII.GetBytes(text);
        var p = input;
        uint h32 = 0x178A54A4;
        if (input.Length >= 16)
        {
            uint v1 = 0x2557311B, v2 = 0x871FB76A, v3 = 0x0133ECF3, v4 = 0x62FC7342;
            do
            {
                v1 = Round(v1, BinaryPrimitives.ReadUInt32LittleEndian(p));
                v2 = Round(v2, BinaryPrimitives.ReadUInt32LittleEndian(p[4..]));
                v3 = Round(v3, BinaryPrimitives.ReadUInt32LittleEndian(p[8..]));
                v4 = Round(v4, BinaryPrimitives.ReadUInt32LittleEndian(p[12..]));
                p = p[16..];
            }
            while (p.Length > 16);
            h32 = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
        }

        h32 += (uint)input.Length;
        while (p.Length >= 4)
        {
            h32 = Rotl(h32 + BinaryPrimitives.ReadUInt32LittleEndian(p) * Prime3, 17) * Prime4;
            p = p[4..];
        }
        while (p.Length > 0)
        {
            h32 = Rotl(h32 + p[0] * Prime5, 11) * Prime1;
            p = p[1..];
        }

        h32 ^= h32 >> 15;
        h32 *= Prime2;
        h32 ^= h32 >> 13;
        h32 *= Prime3;
        h32 ^= h32 >> 16;
        return h32;
    }
}

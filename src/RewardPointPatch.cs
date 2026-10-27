using System.Buffers.Binary;

namespace Spark.Gbfr.Mods;

internal static class RewardPointPatch
{
    public const string RewardTablePath = "system/table/reward.tbl";
    public const string RewardPointTablePath = "system/table/reward_point.tbl";

    private const uint EmptyHash = 0x887AE0B0;
    private const int RewardRowSize = 52;
    private const int RewardExpKeyOffset = 0x1C;
    private const int RewardGoldKeyOffset = 0x20;
    private const int RewardMspKeyOffset = 0x24;
    private const int PointRowSize = 24;
    private const int PointMinimumOffset = 0x0C;
    private const int PointMaximumOffset = 0x10;

    public sealed record Counts(int MspEntries, int MspKeys, int ExpGoldEntries, int ExpGoldKeys, int SharedKeys);

    public static Counts? Apply(byte[] rewards, byte[] points, int mspBonusPercent, int expGoldBonusPercent)
    {
        if (rewards.Length < 8 || points.Length < 8)
            return null;

        var rewardRows = BinaryPrimitives.ReadInt64LittleEndian(rewards);
        var pointRows = BinaryPrimitives.ReadInt64LittleEndian(points);
        if (!HasExpectedLayout(rewards, rewardRows, RewardRowSize) || !HasExpectedLayout(points, pointRows, PointRowSize))
            return null;

        var mspKeys = new HashSet<uint>();
        var expGoldKeys = new HashSet<uint>();
        for (var row = 0L; row < rewardRows; row++)
        {
            var rowOffset = checked((int)(8 + row * RewardRowSize));
            AddRewardKey(mspKeys, rewards, rowOffset + RewardMspKeyOffset);
            AddRewardKey(expGoldKeys, rewards, rowOffset + RewardExpKeyOffset);
            AddRewardKey(expGoldKeys, rewards, rowOffset + RewardGoldKeyOffset);
        }

        var sharedKeys = mspKeys.Count(expGoldKeys.Contains);

        int mspPatched = 0, expGoldPatched = 0;
        for (var row = 0L; row < pointRows; row++)
        {
            var offset = checked((int)(8 + row * PointRowSize));
            var key = BinaryPrimitives.ReadUInt32LittleEndian(points.AsSpan(offset, 4));
            if (mspKeys.Contains(key))
            {
                PatchAmount(points.AsSpan(offset + PointMinimumOffset, 4), mspBonusPercent);
                PatchAmount(points.AsSpan(offset + PointMaximumOffset, 4), mspBonusPercent);
                mspPatched++;
            }
            else if (expGoldKeys.Contains(key))
            {
                PatchAmount(points.AsSpan(offset + PointMinimumOffset, 4), expGoldBonusPercent);
                PatchAmount(points.AsSpan(offset + PointMaximumOffset, 4), expGoldBonusPercent);
                expGoldPatched++;
            }
        }

        return new Counts(mspPatched, mspKeys.Count, expGoldPatched, expGoldKeys.Count, sharedKeys);
    }

    private static bool HasExpectedLayout(byte[] table, long rows, int rowSize) =>
        rows >= 0 && 8 + rows * rowSize == table.LongLength;

    private static void AddRewardKey(HashSet<uint> keys, byte[] table, int offset)
    {
        var key = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(offset, 4));
        if (key != 0 && key != EmptyHash)
            keys.Add(key);
    }

    private static void PatchAmount(Span<byte> field, int bonusPercent)
    {
        var original = BinaryPrimitives.ReadUInt32LittleEndian(field);
        var adjusted = ((ulong)original * (ulong)(100 + bonusPercent) + 99) / 100;
        BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)Math.Min(adjusted, uint.MaxValue));
    }
}

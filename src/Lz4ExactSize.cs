using K4os.Compression.LZ4;

namespace Spark.Gbfr.Mods;

internal static class Lz4ExactSize
{
    private const int MinMatch = 4;

    private struct Sequence
    {
        public int LiteralLength;
        public int Offset;
        public int MatchLength;
    }

    public static byte[]? Encode(byte[] data, int targetSize)
    {
        byte[] compressed = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        int compressedSize = LZ4Codec.Encode(data, compressed, LZ4Level.L12_MAX);
        if (compressedSize <= 0 || compressedSize > targetSize)
            return null;

        var sequences = Parse(compressed.AsSpan(0, compressedSize));
        long deficit = targetSize - compressedSize;

        int i = sequences.Count - 2;
        while (deficit > 0 && i >= 0)
        {
            var a = sequences[i];
            var b = sequences[i + 1];
            var merged = b with { LiteralLength = a.LiteralLength + a.MatchLength + b.LiteralLength };
            long fullDelta = Size(merged) - Size(a) - Size(b);
            if (fullDelta <= deficit)
            {
                sequences[i + 1] = merged;
                sequences.RemoveAt(i);
                deficit -= fullDelta;
                i--;
                continue;
            }

            bool exact = false;
            for (int j = 1; j <= a.MatchLength - MinMatch; j++)
            {
                var shortA = a with { MatchLength = a.MatchLength - j };
                var longB = b with { LiteralLength = b.LiteralLength + j };
                long delta = Size(shortA) + Size(longB) - Size(a) - Size(b);
                if (delta == deficit)
                {
                    sequences[i] = shortA;
                    sequences[i + 1] = longB;
                    deficit = 0;
                    exact = true;
                    break;
                }
                if (delta > deficit)
                    break;
            }

            if (!exact)
                i--;
        }

        if (deficit != 0)
            return null;

        byte[] block = Write(sequences, data, targetSize);
        byte[] check = new byte[data.Length];
        if (block.Length != targetSize || LZ4Codec.Decode(block, check) != data.Length || !check.AsSpan().SequenceEqual(data))
            return null;
        return block;
    }

    private static List<Sequence> Parse(ReadOnlySpan<byte> src)
    {
        var sequences = new List<Sequence>();
        int p = 0;
        while (p < src.Length)
        {
            byte token = src[p++];
            int literals = token >> 4;
            if (literals == 15)
            {
                byte b;
                do { b = src[p++]; literals += b; } while (b == 255);
            }
            p += literals;
            if (p >= src.Length)
            {
                sequences.Add(new Sequence { LiteralLength = literals });
                break;
            }

            int offset = src[p] | src[p + 1] << 8;
            p += 2;
            int match = token & 15;
            if (match == 15)
            {
                byte b;
                do { b = src[p++]; match += b; } while (b == 255);
            }
            sequences.Add(new Sequence { LiteralLength = literals, Offset = offset, MatchLength = match + MinMatch });
        }
        return sequences;
    }

    private static long Size(Sequence s) =>
        1 + ExtraLength(s.LiteralLength) + s.LiteralLength
        + (s.MatchLength == 0 ? 0 : 2 + ExtraLength(s.MatchLength - MinMatch));

    private static int ExtraLength(int length) => length < 15 ? 0 : (length - 15) / 255 + 1;

    private static byte[] Write(List<Sequence> sequences, byte[] data, int capacity)
    {
        var output = new byte[capacity];
        int o = 0, position = 0;
        foreach (var s in sequences)
        {
            if (o + Size(s) > capacity)
                return [];
            int matchCode = s.MatchLength == 0 ? 0 : s.MatchLength - MinMatch;
            output[o++] = (byte)(Math.Min(s.LiteralLength, 15) << 4 | Math.Min(matchCode, 15));
            WriteLength(output, ref o, s.LiteralLength);
            data.AsSpan(position, s.LiteralLength).CopyTo(output.AsSpan(o));
            o += s.LiteralLength;
            position += s.LiteralLength;
            if (s.MatchLength == 0)
                continue;
            output[o++] = (byte)s.Offset;
            output[o++] = (byte)(s.Offset >> 8);
            WriteLength(output, ref o, matchCode);
            position += s.MatchLength;
        }
        return o == capacity ? output : [];
    }

    private static void WriteLength(byte[] output, ref int o, int length)
    {
        if (length < 15)
            return;
        length -= 15;
        while (length >= 255)
        {
            output[o++] = 255;
            length -= 255;
        }
        output[o++] = (byte)length;
    }
}

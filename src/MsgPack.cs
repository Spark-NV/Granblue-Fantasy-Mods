using System.Buffers.Binary;
using System.Text;

namespace Spark.Gbfr.Mods;

internal static class MsgPack
{
    public sealed class Node
    {
        public required string Kind;
        public required int Start;
        public required int End;
        public string? Text;
        public List<(Node Key, Node Value)>? Pairs;
        public List<Node>? Items;
    }

    public static Node Parse(byte[] data)
    {
        var root = Parse(data, 0);
        if (root.End != data.Length)
            throw new InvalidDataException("unexpected bytes after MessagePack root");
        return root;
    }

    public static Dictionary<string, Node> Fields(Node node)
    {
        var fields = new Dictionary<string, Node>();
        if (node.Pairs is null)
            return fields;
        foreach (var (key, value) in node.Pairs)
            if (key.Kind == "str" && key.Text is not null)
                fields[key.Text] = value;
        return fields;
    }

    public static byte[] ReplaceStringsSameSize(byte[] raw, Node root, IReadOnlyList<(Node Node, string Value)> values)
    {
        var padding = new int[values.Count];
        int growth = values.Sum(v => EncodeString(v.Value, 0).Length - (v.Node.End - v.Node.Start));

        var shrunkHeaders = new List<Node>();
        foreach (var container in Containers(root))
        {
            if (growth <= 0)
                break;
            byte tag = raw[container.Start];
            if (tag is 0xDF or 0xDD && Unsigned(raw, container.Start + 1, 4) <= 0xFFFF)
            {
                shrunkHeaders.Add(container);
                growth -= 2;
            }
        }

        for (int i = 0; growth < 0 && i < values.Count; i++)
        {
            padding[i] = Math.Min(2, -growth);
            growth += padding[i];
        }

        if (growth != 0)
            throw new InvalidDataException("edit cannot keep the record size");

        var replacements = new List<(int Start, int End, byte[] Bytes)>();
        for (int i = 0; i < values.Count; i++)
            replacements.Add((values[i].Node.Start, values[i].Node.End, EncodeString(values[i].Value, padding[i])));
        foreach (var header in shrunkHeaders)
        {
            byte tag = raw[header.Start] == 0xDF ? (byte)0xDE : (byte)0xDC;
            ushort count = (ushort)Unsigned(raw, header.Start + 1, 4);
            replacements.Add((header.Start, header.Start + 5, [tag, (byte)(count >> 8), (byte)count]));
        }

        var patched = new List<byte>(raw);
        foreach (var (start, end, bytes) in replacements.OrderByDescending(r => r.Start))
        {
            patched.RemoveRange(start, end - start);
            patched.InsertRange(start, bytes);
        }

        byte[] result = patched.ToArray();
        if (result.Length != raw.Length || Parse(result).End != result.Length)
            throw new InvalidDataException("patched MessagePack is not a same-size, fully parsed record");
        return result;
    }

    private static IEnumerable<Node> Containers(Node node)
    {
        if (node.Kind is not ("map" or "array"))
            yield break;
        yield return node;
        var children = node.Pairs?.SelectMany(p => new[] { p.Key, p.Value }) ?? node.Items ?? [];
        foreach (var child in children)
            foreach (var nested in Containers(child))
                yield return nested;
    }

    private static byte[] EncodeString(string value, int padding)
    {
        byte[] text = Encoding.ASCII.GetBytes(value);
        if (text.Length > 31)
            throw new InvalidDataException("value unexpectedly exceeds fixstr capacity");
        return padding switch
        {
            0 => [(byte)(0xA0 | text.Length), .. text],
            1 => [0xD9, (byte)text.Length, .. text],
            _ => [0xDA, 0, (byte)text.Length, .. text],
        };
    }

    public static bool? ReadBool(byte[] data, Node node) =>
        data[node.Start] switch { 0xC2 => false, 0xC3 => true, _ => null };

    public static double? ReadNumber(byte[] data, Node node)
    {
        int p = node.Start;
        byte tag = data[p];
        if (tag <= 0x7F) return tag;
        if (tag >= 0xE0) return (sbyte)tag;
        var span = data.AsSpan(p + 1);
        return tag switch
        {
            0xCC => span[0],
            0xCD => BinaryPrimitives.ReadUInt16BigEndian(span),
            0xCE => BinaryPrimitives.ReadUInt32BigEndian(span),
            0xCF => BinaryPrimitives.ReadUInt64BigEndian(span),
            0xD0 => (sbyte)span[0],
            0xD1 => BinaryPrimitives.ReadInt16BigEndian(span),
            0xD2 => BinaryPrimitives.ReadInt32BigEndian(span),
            0xD3 => BinaryPrimitives.ReadInt64BigEndian(span),
            0xCA => BinaryPrimitives.ReadSingleBigEndian(span),
            0xCB => BinaryPrimitives.ReadDoubleBigEndian(span),
            _ => null,
        };
    }

    public static byte[] EncodeNumberLike(byte[] data, Node node, double value)
    {
        byte tag = data[node.Start];
        if (tag == 0xCA)
        {
            var bytes = new byte[5];
            bytes[0] = 0xCA;
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(1), (float)value);
            return bytes;
        }
        if (tag == 0xCB)
        {
            var bytes = new byte[9];
            bytes[0] = 0xCB;
            BinaryPrimitives.WriteDoubleBigEndian(bytes.AsSpan(1), value);
            return bytes;
        }

        long n = checked((long)Math.Round(value));
        if (n >= 0 && n <= 0x7F) return [(byte)n];
        if (n >= 0 && n <= 0xFF) return [0xCC, (byte)n];
        if (n >= 0 && n <= 0xFFFF) return [0xCD, (byte)(n >> 8), (byte)n];
        if (n >= 0 && n <= 0xFFFFFFFF) return [0xCE, (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];
        throw new InvalidDataException($"cannot encode {value} as a timer value");
    }

    private static Node Parse(byte[] data, int pos)
    {
        int start = pos;
        byte tag = Byte(data, pos++);

        if (tag <= 0x7F || tag >= 0xE0)
            return Scalar(start, pos);
        if (tag is >= 0x80 and <= 0x8F)
            return ParseMap(data, start, pos, tag & 0x0F);
        if (tag is >= 0x90 and <= 0x9F)
            return ParseArray(data, start, pos, tag & 0x0F);
        if (tag is >= 0xA0 and <= 0xBF)
            return ParseString(data, start, pos, tag & 0x1F);

        switch (tag)
        {
            case 0xC0 or 0xC2 or 0xC3: return Scalar(start, pos);
            case 0xC4: return Scalar(start, pos + 1 + (int)Unsigned(data, pos, 1));
            case 0xC5: return Scalar(start, pos + 2 + (int)Unsigned(data, pos, 2));
            case 0xC6: return Scalar(start, pos + 4 + (int)Unsigned(data, pos, 4));
            case 0xC7: return Scalar(start, pos + 2 + (int)Unsigned(data, pos, 1));
            case 0xC8: return Scalar(start, pos + 3 + (int)Unsigned(data, pos, 2));
            case 0xC9: return Scalar(start, pos + 5 + (int)Unsigned(data, pos, 4));
            case 0xCA or 0xCE or 0xD2: return Scalar(start, pos + 4);
            case 0xCB or 0xCF or 0xD3: return Scalar(start, pos + 8);
            case 0xCC or 0xD0: return Scalar(start, pos + 1);
            case 0xCD or 0xD1: return Scalar(start, pos + 2);
            case >= 0xD4 and <= 0xD8: return Scalar(start, pos + 1 + (1 << (tag - 0xD4)));
            case 0xD9: return ParseString(data, start, pos + 1, (int)Unsigned(data, pos, 1));
            case 0xDA: return ParseString(data, start, pos + 2, (int)Unsigned(data, pos, 2));
            case 0xDB: return ParseString(data, start, pos + 4, (int)Unsigned(data, pos, 4));
            case 0xDC: return ParseArray(data, start, pos + 2, (int)Unsigned(data, pos, 2));
            case 0xDD: return ParseArray(data, start, pos + 4, (int)Unsigned(data, pos, 4));
            case 0xDE: return ParseMap(data, start, pos + 2, (int)Unsigned(data, pos, 2));
            case 0xDF: return ParseMap(data, start, pos + 4, (int)Unsigned(data, pos, 4));
            default: throw new InvalidDataException($"unsupported MessagePack tag 0x{tag:x2}");
        }

        Node Scalar(int s, int end)
        {
            if (end > data.Length)
                throw new InvalidDataException("truncated MessagePack record");
            return new Node { Kind = "scalar", Start = s, End = end };
        }
    }

    private static Node ParseString(byte[] data, int start, int payload, int length)
    {
        if (payload + length > data.Length)
            throw new InvalidDataException("truncated MessagePack record");
        return new Node
        {
            Kind = "str", Start = start, End = payload + length,
            Text = Encoding.Latin1.GetString(data, payload, length),
        };
    }

    private static Node ParseArray(byte[] data, int start, int pos, int count)
    {
        var items = new List<Node>(count);
        for (int i = 0; i < count; i++)
        {
            var item = Parse(data, pos);
            items.Add(item);
            pos = item.End;
        }
        return new Node { Kind = "array", Start = start, End = pos, Items = items };
    }

    private static Node ParseMap(byte[] data, int start, int pos, int count)
    {
        var pairs = new List<(Node, Node)>(count);
        for (int i = 0; i < count; i++)
        {
            var key = Parse(data, pos);
            var value = Parse(data, key.End);
            pairs.Add((key, value));
            pos = value.End;
        }
        return new Node { Kind = "map", Start = start, End = pos, Pairs = pairs };
    }

    private static byte Byte(byte[] data, int pos) =>
        pos < data.Length ? data[pos] : throw new InvalidDataException("truncated MessagePack record");

    public static ulong Unsigned(byte[] data, int pos, int length)
    {
        if (pos + length > data.Length)
            throw new InvalidDataException("truncated MessagePack record");
        ulong value = 0;
        for (int i = 0; i < length; i++)
            value = value << 8 | data[pos + i];
        return value;
    }
}

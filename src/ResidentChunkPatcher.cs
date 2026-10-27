using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using K4os.Compression.LZ4;

namespace Spark.Gbfr.Mods;

internal sealed class ResidentChunkPatcher
{
    private const string CacheVersion = "chunk1-exact-v4";

    private readonly Action<string> _log;

    public ResidentChunkPatcher(Action<string> log) => _log = log;

    public const int TargetChunk = 1;

    public sealed record Result(string ModdedData0Path, int PatchedFiles);

    public Result? Apply(string gameDir, byte[] index, string cacheDir,
        Func<Func<string, byte[]?>, IReadOnlyList<(string Path, byte[] Data)>> buildEdits)
    {
        var view = new IndexView(index);
        if (view.XxHashSeed != 0)
        {
            _log($"ERROR: unexpected index hash seed {view.XxHashSeed}; patch skipped.");
            return null;
        }

        var chunk = view.ReadChunk(TargetChunk);
        if (chunk.DataFileNumber != 0 || chunk.Size == chunk.UncompressedSize)
        {
            _log($"ERROR: chunk {TargetChunk} layout differs from the known game build; patch skipped.");
            return null;
        }

        string data0Path = Path.Combine(gameDir, "data.0");
        byte[] compressed = new byte[chunk.Size];
        using (var fs = new FileStream(data0Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Seek((long)chunk.FileOffset, SeekOrigin.Begin);
            fs.ReadExactly(compressed);
        }

        byte[] original = new byte[chunk.UncompressedSize];
        if (LZ4Codec.Decode(compressed, original) != original.Length)
        {
            _log($"ERROR: chunk {TargetChunk} failed to decompress; patch skipped.");
            return null;
        }

        int SlotOf(string path)
        {
            ulong hash = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            int slot = view.FindArchiveFile(hash);
            return slot >= 0 && view.ReadIndexer(slot).ChunkIndex == TargetChunk ? slot : -1;
        }

        byte[]? Read(string path)
        {
            int slot = SlotOf(path);
            if (slot < 0)
                return null;
            var indexer = view.ReadIndexer(slot);
            return original.AsSpan((int)indexer.Offset, (int)indexer.FileSize).ToArray();
        }

        var edits = buildEdits(Read).Select(e => (Slot: SlotOf(e.Path), e.Data)).ToList();
        if (edits.Count == 0)
        {
            _log("No in-archive changes enabled; archive left vanilla.");
            return null;
        }
        if (edits.Any(e => e.Slot < 0))
        {
            _log("ERROR: an edited file is not in the resident chunk; patch skipped.");
            return null;
        }

        byte[] patchedChunk = (byte[])original.Clone();
        foreach (var (slot, data) in edits)
        {
            var indexer = view.ReadIndexer(slot);
            if (data.Length != indexer.FileSize)
            {
                _log($"ERROR: an edit changed a resident file's size ({indexer.FileSize} -> {data.Length}); patch skipped.");
                return null;
            }
            data.CopyTo(patchedChunk, (int)indexer.Offset);
        }

        ulong sourceKey = XxHash64.HashToUInt64(compressed);
        ulong patchedKey = XxHash64.HashToUInt64(patchedChunk);
        string cacheKey = $"{CacheVersion}|{sourceKey:x16}|{patchedKey:x16}|{new FileInfo(data0Path).Length}";

        Directory.CreateDirectory(cacheDir);
        string moddedData0 = Path.Combine(cacheDir, "data.0");
        string keyPath = Path.Combine(cacheDir, "data.0.key");

        if (File.Exists(moddedData0) && File.Exists(keyPath) && File.ReadAllText(keyPath) == cacheKey)
        {
            _log("Using cached resident chunk rebuild.");
            return new Result(moddedData0, edits.Count);
        }

        byte[]? block = Lz4ExactSize.Encode(patchedChunk, (int)chunk.Size);
        if (block is null)
        {
            _log($"ERROR: could not encode the rebuilt chunk at exactly {chunk.Size} bytes; patch skipped.");
            return null;
        }

        string tempPath = moddedData0 + ".tmp";
        File.Copy(data0Path, tempPath, overwrite: true);
        using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Write))
        {
            fs.Seek((long)chunk.FileOffset, SeekOrigin.Begin);
            fs.Write(block);
        }
        File.Move(tempPath, moddedData0, overwrite: true);
        File.WriteAllText(keyPath, cacheKey);
        return new Result(moddedData0, edits.Count);
    }

    public static bool IsStreamedArchiveFile(byte[] index, string path)
    {
        var view = new IndexView(index);
        int slot = view.FindArchiveFile(XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(path.ToLowerInvariant())));
        return slot >= 0 && !view.CachedChunks().Contains(view.ReadIndexer(slot).ChunkIndex);
    }

    private sealed class IndexView
    {
        private readonly byte[] _buf;
        private readonly int _root;
        private readonly int[] _fieldOffsets;

        public IndexView(byte[] buf)
        {
            _buf = buf;
            _root = (int)U32(0);
            int vtable = _root - BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(_root));
            int vtableLength = U16(vtable);
            _fieldOffsets = new int[(vtableLength - 4) / 2];
            for (int i = 0; i < _fieldOffsets.Length; i++)
                _fieldOffsets[i] = U16(vtable + 4 + i * 2);
        }

        public ushort XxHashSeed => Field(2) is int f ? U16(f) : (ushort)0;

        public (ulong FileOffset, uint Size, uint UncompressedSize, byte DataFileNumber) ReadChunk(int i)
        {
            int at = Element(5, i, 24);
            return (BinaryPrimitives.ReadUInt64LittleEndian(_buf.AsSpan(at)), U32(at + 8), U32(at + 12), _buf[at + 22]);
        }

        public (int ChunkIndex, uint FileSize, uint Offset) ReadIndexer(int i)
        {
            int at = Element(4, i, 12);
            return (BinaryPrimitives.ReadInt32LittleEndian(_buf.AsSpan(at)), U32(at + 4), U32(at + 8));
        }

        public HashSet<int> CachedChunks()
        {
            var cached = new HashSet<int>();
            for (int i = 0; i < Count(8); i++)
                cached.Add((int)U32(Element(8, i, 4)));
            return cached;
        }

        public int FindArchiveFile(ulong hash)
        {
            int count = Count(3);
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buf.AsSpan(Element(3, mid, 8)));
                if (value == hash) return mid;
                if (value < hash) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        private int? Field(int i) => i < _fieldOffsets.Length && _fieldOffsets[i] != 0 ? _root + _fieldOffsets[i] : null;

        private int Vector(int field)
        {
            int f = Field(field) ?? throw new InvalidDataException($"index field {field} is missing");
            return f + (int)U32(f);
        }

        private int Count(int field) => (int)U32(Vector(field));

        private int Element(int field, int i, int size)
        {
            int vector = Vector(field);
            if ((uint)i >= U32(vector))
                throw new IndexOutOfRangeException($"index field {field} element {i}");
            return vector + 4 + i * size;
        }

        private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_buf.AsSpan(at));
        private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_buf.AsSpan(at));
    }
}

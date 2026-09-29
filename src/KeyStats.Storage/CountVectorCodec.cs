using System.Buffers.Binary;
using KeyStats.Core;

namespace KeyStats.Storage;

public static class CountVectorCodec
{
    public const byte FormatVersion = 1;
    public const int MinimumSupportedKeyMapVersion = 1;
    public const int KeyMapVersion = 2;
    private const int HeaderSize = 3;
    private const int EntrySize = 6;

    public static byte[] Encode(ReadOnlySpan<uint> counts)
    {
        ValidateVectorLength(counts.Length);

        var nonZeroCount = 0;
        foreach (var count in counts)
        {
            if (count != 0)
            {
                nonZeroCount++;
            }
        }

        var data = new byte[HeaderSize + (nonZeroCount * EntrySize)];
        data[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(1, 2), checked((ushort)nonZeroCount));

        var offset = HeaderSize;
        for (var keyIndex = 0; keyIndex < counts.Length; keyIndex++)
        {
            var count = counts[keyIndex];
            if (count == 0)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), checked((ushort)keyIndex));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 2, 4), count);
            offset += EntrySize;
        }

        return data;
    }

    public static uint[] Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("计数向量过短。");
        }

        if (data[0] != FormatVersion)
        {
            throw new InvalidDataException($"不支持的计数向量版本：{data[0]}。");
        }

        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1, 2));
        var expectedLength = HeaderSize + (entryCount * EntrySize);
        if (data.Length != expectedLength)
        {
            throw new InvalidDataException("计数向量长度不正确。");
        }

        var counts = new uint[KeyCatalog.Count];
        var seen = new bool[KeyCatalog.Count];
        var offset = HeaderSize;
        for (var entry = 0; entry < entryCount; entry++)
        {
            var keyIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            if (keyIndex >= counts.Length || seen[keyIndex])
            {
                throw new InvalidDataException("计数向量包含无效或重复 Key ID。");
            }

            var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 2, 4));
            if (count == 0)
            {
                throw new InvalidDataException("稀疏计数向量不应包含零值。");
            }

            counts[keyIndex] = count;
            seen[keyIndex] = true;
            offset += EntrySize;
        }

        return counts;
    }

    private static void ValidateVectorLength(int length)
    {
        if (length != KeyCatalog.Count)
        {
            throw new ArgumentException($"计数向量长度应为 {KeyCatalog.Count}，实际为 {length}。");
        }
    }
}

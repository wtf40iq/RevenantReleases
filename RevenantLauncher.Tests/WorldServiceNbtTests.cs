using System.IO;
using System.IO.Compression;
using System.Text;
using RevenantLauncher.Services;
using Xunit;

namespace RevenantLauncher.Tests;

/// <summary>
/// Тесты мини-парсера NBT (WorldService.ReadLevelName) на сконструированных
/// бинарных данных — проверяем поиск LevelName, пропуск списков и компаундов.
/// </summary>
public class WorldServiceNbtTests
{
    private static byte[] Gzip(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    private static byte[] BE(int v) => new[]
    {
        (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v
    };

    private static byte[] TagName(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var result = new List<byte> { (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF) };
        result.AddRange(bytes);
        return result.ToArray();
    }

    private static byte[] CompoundOpen(string name)
    {
        var result = new List<byte> { 10 };
        result.AddRange(TagName(name));
        return result.ToArray();
    }

    private static byte[] IntTag(string name, int value)
    {
        var result = new List<byte> { 3 };
        result.AddRange(TagName(name));
        result.AddRange(BE(value));
        return result.ToArray();
    }

    private static byte[] Str(string name, string value)
    {
        var v = Encoding.UTF8.GetBytes(value);
        var result = new List<byte> { 8 };
        result.AddRange(TagName(name));
        result.AddRange(BE(v.Length));
        result.AddRange(v);
        return result.ToArray();
    }

    private static byte[] FloatList(string name, params float[] values)
    {
        var result = new List<byte> { 9 };
        result.AddRange(TagName(name));
        result.Add(5); // тип элемента: float
        result.AddRange(BE(values.Length));
        foreach (var f in values)
            result.AddRange(BitConverter.GetBytes(f).Reverse());
        return result.ToArray();
    }

    /// <summary>Пишет gzip-NBT в temp-папку с level.dat и вызывает парсер</summary>
    private static string? ReadFrom(params byte[] nbt)
    {
        var dir = Path.Combine(Path.GetTempPath(), "revenant-nbt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "level.dat"), Gzip(nbt));
            return WorldService.ReadLevelName(dir);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadLevelName_SimpleCompound_FindsValue()
    {
        var data = new List<byte>();
        data.AddRange(CompoundOpen("Data"));
        data.AddRange(IntTag("version", 19133));
        data.AddRange(Str("LevelName", "My World"));
        data.Add(0); // конец Data

        Assert.Equal("My World", ReadFrom(data.ToArray()));
    }

    [Fact]
    public void ReadLevelName_MissingTag_ReturnsNull()
    {
        var data = new List<byte>();
        data.AddRange(CompoundOpen("Data"));
        data.AddRange(IntTag("version", 19133));
        data.Add(0);

        Assert.Null(ReadFrom(data.ToArray()));
    }

    [Fact]
    public void ReadLevelName_SkipsListsAndFindsNestedValue()
    {
        // Data: { Rotation: [1.5, -0.5], Player: {}, Inner: { LevelName: "Deep" }, LevelName: "Top" }
        var data = new List<byte>();
        data.AddRange(CompoundOpen("Data"));
        data.AddRange(FloatList("Rotation", 1.5f, -0.5f));
        data.AddRange(CompoundOpen("Player"));
        data.Add(0); // пустой Player
        data.AddRange(CompoundOpen("Inner"));
        data.AddRange(Str("LevelName", "Deep"));
        data.Add(0); // конец Inner
        data.AddRange(Str("LevelName", "Top"));
        data.Add(0); // конец Data

        // Поиск идёт в глубину — сначала находится вложенный LevelName
        Assert.Equal("Deep", ReadFrom(data.ToArray()));
    }

    [Fact]
    public void ReadLevelName_ListOfCompounds_DoesNotCorruptParsing()
    {
        // Data: { SpawnPotentials: [ { Weight: 1 } ], LevelName: "Listed" }
        var data = new List<byte>();
        data.AddRange(CompoundOpen("Data"));

        data.Add(9); // список
        data.AddRange(TagName("SpawnPotentials"));
        data.Add(10); // тип элемента: compound
        data.AddRange(BE(1)); // один элемент
        data.AddRange(IntTag("Weight", 1)); // элемент-компаунд: именованный тег внутри
        data.Add(0); // конец элемента-компаунда

        data.AddRange(Str("LevelName", "Listed"));
        data.Add(0); // конец Data

        Assert.Equal("Listed", ReadFrom(data.ToArray()));
    }

    [Fact]
    public void ReadLevelName_ByteArrayTag_IsSkipped()
    {
        // Data: { Icon: byte[] len 4, LevelName: "Ok" }
        var data = new List<byte>();
        data.AddRange(CompoundOpen("Data"));
        data.Add(7); // byte array
        data.AddRange(TagName("Icon"));
        data.AddRange(BE(4));
        data.AddRange(new byte[] { 1, 2, 3, 4 });
        data.AddRange(Str("LevelName", "Ok"));
        data.Add(0);

        Assert.Equal("Ok", ReadFrom(data.ToArray()));
    }
}

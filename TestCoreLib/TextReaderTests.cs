using System;
using System.Text;

namespace TestCoreLib;

public class TextReaderTests
{
    [Fact]
    public void Read_DefaultReturnsMinusOne()
    {
        var reader = new MinimalTextReader();
        Assert.Equal(-1, reader.Read());
    }

    [Fact]
    public void Read_SpanChar_Works()
    {
        using var reader = new TestTextReader("Hello World");
        Span<char> buffer = stackalloc char[5];
        int read = reader.Read(buffer);
        Assert.Equal(5, read);
        Assert.Equal("Hello", buffer.ToString());
    }

    [Fact]
    public void ReadLine_SupportsLfAndCrLf_Only()
    {
        using var reader = new TestTextReader("line1\nline2\r\nline3\r\n");

        Assert.Equal("line1", reader.ReadLine());
        Assert.Equal("line2", reader.ReadLine());
        Assert.Equal("line3", reader.ReadLine());
        Assert.Null(reader.ReadLine());
    }

    [Fact]
    public void ReadLine_LoneCr_IsTreatedAsNormalCharacter()
    {
        using var reader = new TestTextReader("cronly\rnext");

        Assert.Equal("cronly\rnext", reader.ReadLine());
        Assert.Null(reader.ReadLine());
    }

    [Fact]
    public void ReadLine_EmptyInput_ReturnsNull()
    {
        using var reader = new TestTextReader("");
        Assert.Null(reader.ReadLine());
    }

    [Fact]
    public void ReadLine_EndsWithoutNewline()
    {
        using var reader = new TestTextReader("last line");
        Assert.Equal("last line", reader.ReadLine());
        Assert.Null(reader.ReadLine());
    }
}

internal sealed class MinimalTextReader : CoreLib.TextReader
{
    public override void SetBufferSize(int charSize = 512, int byteSize = 4096) { }
}

internal sealed class TestTextReader(string? input) : CoreLib.TextReader, IDisposable
{
    private readonly string _input = input ?? string.Empty;
    private int _position;

    public override int Read()
    {
        if (_position >= _input.Length)
            return -1;
        return _input[_position++];
    }

    public override int Read(Span<char> buffer)
    {
        if (buffer.IsEmpty || _position >= _input.Length)
            return 0;

        var count = Math.Min(buffer.Length, _input.Length - _position);
        _input.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || _position >= _input.Length)
            return 0;

        var remaining = _input.AsSpan(_position);
        var bytes = Encoding.UTF8.GetBytes(remaining.ToArray());
        var count = Math.Min(buffer.Length, bytes.Length);
        bytes.AsSpan(0, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override void SetBufferSize(int charSize = 512, int byteSize = 4096) { }

    public void Dispose() { }
}
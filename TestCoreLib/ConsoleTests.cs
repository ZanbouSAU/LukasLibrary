using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestCoreLib;

public class ConsoleTests
{
    private sealed class TestTextWriter : CoreLib.TextWriter
    {
        private readonly StringBuilder _output = new();
        private readonly Lock _lock = new();

        public string Output
        {
            get { lock (_lock) return _output.ToString(); }
        }

        public int FlushCount { get; private set; }
        public int CharSize { get; private set; } = 512;
        public int ByteSize { get; private set; } = 4096;
        public bool CharBufferEnabled { get; private set; } = true;
        public bool ByteBufferEnabled { get; private set; } = true;
        public bool AutoFlushEnabled { get; private set; }

        public override void Flush()
        {
            lock (_lock)
            {
                FlushCount++;
            }
        }

        public override void SetBufferSize(int charSize = 512, int byteSize = 4096)
        {
            lock (_lock)
            {
                CharSize = charSize;
                ByteSize = byteSize;
            }
        }

        public override void EnableCharBuffer(bool enableCharBuffer)
        {
            lock (_lock) { CharBufferEnabled = enableCharBuffer; }
        }

        public override void EnableByteBuffer(bool enableByteBuffer)
        {
            lock (_lock) { ByteBufferEnabled = enableByteBuffer; }
        }

        public override void EnableAutoFlush(bool enableAutoFlush)
        {
            lock (_lock) { AutoFlushEnabled = enableAutoFlush; }
        }

        public override void Write(ReadOnlySpan<byte> value, bool isLine = false)
        {
            lock (_lock)
            {
                _output.Append(Encoding.UTF8.GetString(value));
                if (isLine) _output.Append('\n');
            }
        }

        public override void Write(ReadOnlySpan<char> value, bool isLine = false)
        {
            lock (_lock)
            {
                _output.Append(value);
                if (isLine) _output.Append('\n');
            }
        }
    }
    
    private sealed class TestTextReader(string? input) : CoreLib.TextReader
    {
        private readonly string _input = input ?? string.Empty;
        private int _position;

        public override int Read()
        {
            if (_position >= _input.Length)
                return -1;
            return _input[_position++];
        }
        
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || _position >= _input.Length)
                return 0;
            
            var remaining = _input.Substring(_position);
            var bytes = Encoding.UTF8.GetBytes(remaining);
            var toCopy = Math.Min(bytes.Length, buffer.Length);

            bytes.AsSpan(0, toCopy).CopyTo(buffer);
            _position += Encoding.UTF8.GetCharCount(bytes, 0, toCopy);

            return toCopy;
        }

        public override void SetBufferSize(int charSize = 512, int byteSize = 4096) { }
    }

    private readonly TestTextWriter _writer;

    public ConsoleTests()
    {
        _writer = new TestTextWriter();
        CoreLib.Console.SetOut(_writer);

        var reader = new TestTextReader(string.Empty);
        CoreLib.Console.SetIn(reader);
    }

    [Fact]
    public void WriteLine_String_AppendsContentWithNewline()
    {
        CoreLib.Console.WriteLine("Hello, CoreLib!");
        Assert.Equal("Hello, CoreLib!\n", _writer.Output);
    }

    [Fact]
    public void Write_String_DoesNotAddNewline()
    {
        CoreLib.Console.Write("Part1");
        CoreLib.Console.Write("Part2");
        Assert.Equal("Part1Part2", _writer.Output);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("test", "test")]
    public void Write_Object_HandlesNullAndValue(string? input, string expected)
    {
        CoreLib.Console.Write((object?)input);
        Assert.Equal(expected, _writer.Output);
    }

    [Fact]
    public void WriteLine_NullObject_WritesOnlyNewline()
    {
        CoreLib.Console.WriteLine((object?)null);
        Assert.Equal("\n", _writer.Output);
    }

    [Fact]
    public void Write_CharArray_WorksCorrectly()
    {
        CoreLib.Console.Write(new[] { 'A', 'B', 'C' });
        Assert.Equal("ABC", _writer.Output);
    }

    [Fact]
    public void WriteLine_CharArray_AppendsNewline()
    {
        CoreLib.Console.WriteLine(new[] { 'X', 'Y' });
        Assert.Equal("XY\n", _writer.Output);
    }

    [Fact]
    public void Write_SpanChar_Works()
    {
        ReadOnlySpan<char> span = "SpanTest".AsSpan();
        CoreLib.Console.Write(span);
        Assert.Equal("SpanTest", _writer.Output);
    }

    [Fact]
    public void WriteLine_SpanByte_Utf8()
    {
        var bytes = "UTF8测试"u8;
        CoreLib.Console.WriteLine(bytes);
        Assert.Equal("UTF8测试\n", _writer.Output);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(-123456789)]
    [InlineData(0)]
    public void Write_GenericInt_Works(int value)
    {
        CoreLib.Console.Write(value);
        Assert.Equal(value.ToString(), _writer.Output);
    }

    [Theory]
    [InlineData(3.14159)]
    [InlineData(-0.0001)]
    public void WriteLine_GenericDouble_Works(double value)
    {
        CoreLib.Console.WriteLine(value);
        Assert.StartsWith(value.ToString(CultureInfo.InvariantCulture), _writer.Output.TrimEnd('\n'));
        Assert.EndsWith("\n", _writer.Output);
    }

    [Fact]
    public void WriteLine_GenericBool_TrueFalse()
    {
        CoreLib.Console.WriteLine(true);
        CoreLib.Console.WriteLine(false);
        Assert.Equal("True\nFalse\n", _writer.Output);
    }

    [Fact]
    public void WriteLine_GenericDateTime_Works()
    {
        var dt = new DateTime(2026, 5, 21, 17, 30, 0, DateTimeKind.Utc);
        CoreLib.Console.WriteLine(dt);
        Assert.Contains("2026", _writer.Output);
        Assert.EndsWith("\n", _writer.Output);
    }

    [Fact]
    public void ReadLine_ReadsCorrectLine()
    {
        CoreLib.Console.SetIn(new TestTextReader("First line\nSecond line\r\nThird"));
        var line1 = CoreLib.Console.ReadLine();
        var line2 = CoreLib.Console.ReadLine();
        var line3 = CoreLib.Console.ReadLine();
        Assert.Equal("First line", line1);
        Assert.Equal("Second line", line2);
        Assert.Equal("Third", line3);
    }

    [Fact]
    public void ReadLine_HandlesEmptyAndNull()
    {
        CoreLib.Console.SetIn(new TestTextReader("\n\n"));
        Assert.Equal("", CoreLib.Console.ReadLine());
        Assert.Equal("", CoreLib.Console.ReadLine());
        Assert.Null(CoreLib.Console.ReadLine());
    }

    [Fact]
    public void Read_CharByChar_Works()
    {
        CoreLib.Console.SetIn(new TestTextReader("ABC"));
        Assert.Equal('A', (char)CoreLib.Console.Read());
        Assert.Equal('B', (char)CoreLib.Console.Read());
        Assert.Equal('C', (char)CoreLib.Console.Read());
        Assert.Equal(-1, CoreLib.Console.Read());
    }

    [Fact]
    public void Read_SpanChar_FillsBuffer()
    {
        CoreLib.Console.SetIn(new TestTextReader("HelloWorld"));
        Span<char> buffer = stackalloc char[5];
        var read = CoreLib.Console.Read(buffer);
        Assert.Equal(5, read);
        Assert.Equal("Hello", new string(buffer));
    }

    [Fact]
    public void Read_SpanByte_RawBytes()
    {
        CoreLib.Console.SetIn(new TestTextReader("Raw"));
        Span<byte> buffer = stackalloc byte[10];
        var read = CoreLib.Console.Read(buffer);
        Assert.Equal(3, read);
        Assert.Equal((byte)'R', buffer[0]);
    }

    [Fact]
    public void FlushBuffer_CallsFlushOnWriter()
    {
        CoreLib.Console.FlushBuffer();
        Assert.True(_writer.FlushCount >= 1);
    }

    [Fact]
    public void SetOutBufferSize_UpdatesWriter()
    {
        CoreLib.Console.SetOutBufferSize(1024, 8192);
        Assert.Equal(1024, _writer.CharSize);
        Assert.Equal(8192, _writer.ByteSize);
    }

    [Fact]
    public void EnableCharBuffer_SetsFlag()
    {
        CoreLib.Console.EnableCharBuffer(false);
        Assert.False(_writer.CharBufferEnabled);
        CoreLib.Console.EnableCharBuffer(true);
        Assert.True(_writer.CharBufferEnabled);
    }

    [Fact]
    public void EnableByteBuffer_SetsFlag()
    {
        CoreLib.Console.EnableByteBuffer(false);
        Assert.False(_writer.ByteBufferEnabled);
    }

    [Fact]
    public void EnableAutoFlush_SetsFlag()
    {
        CoreLib.Console.EnableAutoFlush(true);
        Assert.True(_writer.AutoFlushEnabled);
    }

    [Fact]
    public void SetIn_SwitchesReaderSuccessfully()
    {
        var newReader = new TestTextReader("Switched");
        CoreLib.Console.SetIn(newReader);
        Assert.Equal("Switched", CoreLib.Console.ReadLine());
    }

    [Fact]
    public async Task ConcurrentWrites_NoExceptionAndDataAppended()
    {
        var writer = new TestTextWriter();
        CoreLib.Console.SetOut(writer);

        var tasks = new Task[8];
        for (var i = 0; i < tasks.Length; i++)
        {
            var local = i;
            tasks[i] = Task.Run(() =>
            {
                for (var j = 0; j < 50; j++)
                {
                    CoreLib.Console.Write($"T{local}-{j} ");
                }
            });
        }

        await Task.WhenAll(tasks);

        var result = writer.Output;
        Assert.NotEmpty(result);
        Assert.True(result.Length > 1000, $"实际长度只有 {result.Length}");
        
        for (var i = 0; i < 8; i++)
        {
            Assert.Contains($"T{i}-", result);
        }
    }

    [Fact]
    public void WriteLine_EmptyString_WritesOnlyNewline()
    {
        CoreLib.Console.WriteLine("");
        Assert.Equal("\n", _writer.Output);
    }

    [Fact]
    public void Write_EmptySpan_DoesNothing()
    {
        CoreLib.Console.Write(ReadOnlySpan<char>.Empty);
        CoreLib.Console.Write(ReadOnlySpan<byte>.Empty);
        Assert.Equal("", _writer.Output);
    }
}

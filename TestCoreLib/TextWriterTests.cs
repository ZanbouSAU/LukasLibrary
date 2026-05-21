using System;
using System.Text;

namespace TestCoreLib;

/// <summary>
/// 针对 CoreLib.TextWriter 基类方法（尤其是泛型 Write&lt;T&gt; 和各种重载）的详细测试。
/// 使用 TestTextWriter 验证格式化、换行、null 处理等逻辑。
/// </summary>
public class TextWriterTests
{
    private sealed class CapturingWriter : CoreLib.TextWriter
    {
        public StringBuilder Buffer { get; } = new();

        public override void Flush() { }
        public override void SetBufferSize(int charSize = 512, int byteSize = 4096) { }
        public override void EnableCharBuffer(bool enableCharBuffer) { }
        public override void EnableByteBuffer(bool enableByteBuffer) { }
        public override void EnableAutoFlush(bool enableAutoFlush) { }

        public override void Write(ReadOnlySpan<byte> value, bool isLine = false)
        {
            Buffer.Append(Encoding.UTF8.GetString(value));
            if (isLine) Buffer.Append('\n');
        }

        public override void Write(ReadOnlySpan<char> value, bool isLine = false)
        {
            Buffer.Append(value);
            if (isLine) Buffer.Append('\n');
        }
    }

    [Fact]
    public void Write_Generic_IUtf8SpanFormattable_UsesTryFormat()
    {
        var writer = new CapturingWriter();
        writer.Write(12345);
        Assert.Equal("12345", writer.Buffer.ToString());
    }

    [Fact]
    public void WriteLine_Generic_AppendsNewline()
    {
        var writer = new CapturingWriter();
        writer.WriteLine(9876L);
        Assert.Equal("9876\n", writer.Buffer.ToString());
    }

    [Fact]
    public void Write_Object_ToString_AndNull()
    {
        var writer = new CapturingWriter();
        writer.Write((object)"obj");
        writer.Write((object?)null);
        writer.WriteLine((object)"end");
        Assert.Equal("objend\n", writer.Buffer.ToString());
    }

    [Fact]
    public void Write_String_NullSafe()
    {
        var writer = new CapturingWriter();
        writer.Write((string?)null);
        writer.WriteLine((string?)null);
        Assert.Equal("\n", writer.Buffer.ToString());
    }

    [Fact]
    public void WriteLine_Empty_WritesNewline()
    {
        var writer = new CapturingWriter();
        writer.WriteLine();
        Assert.Equal("\n", writer.Buffer.ToString());
    }

    [Fact]
    public void Write_CharArray_AndLine()
    {
        var writer = new CapturingWriter();
        writer.Write(new[] { 'a', 'b' });
        writer.WriteLine(new[] { 'c', 'd' });
        Assert.Equal("abcd\n", writer.Buffer.ToString());
    }
}

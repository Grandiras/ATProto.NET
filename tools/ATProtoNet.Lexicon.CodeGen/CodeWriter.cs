using System;
using System.Collections.Generic;
using System.Text;

namespace ATProtoNet.Lexicon.CodeGen;

// Indented C# text with "\n" line endings, so output is byte-identical on every platform (LEX-25, LEX-29).
internal sealed class CodeWriter
{
    private readonly StringBuilder _text = new();
    private int _indent;

    public CodeWriter Line(string line = "")
    {
        if (line.Length > 0) _text.Append(' ', _indent * 4).Append(line);
        _text.Append('\n');
        return this;
    }

    public CodeWriter Lines(IEnumerable<string> lines)
    {
        foreach (var line in lines) Line(line);
        return this;
    }

    public IDisposable Block(string header)
    {
        Line(header).Line("{");
        _indent++;
        return new Close(this);
    }

    // A blank line between members, but never after an opening brace.
    public CodeWriter Separate()
    {
        if (_text.Length > 1 && !(_text[_text.Length - 2] == '{' || _text[_text.Length - 2] == '\n')) _text.Append('\n');
        return this;
    }

    public override string ToString() => _text.ToString();

    private sealed class Close(CodeWriter writer) : IDisposable
    {
        public void Dispose()
        {
            writer._indent--;
            writer.Line("}");
        }
    }
}

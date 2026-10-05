using System.Text;

namespace Tessera.Generator
{
    internal sealed class CodeWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(16 * 1024);
        private readonly string _unit;
        private int _indent;

        public CodeWriter(string indentUnit = "    ") => _unit = indentUnit;

        public CodeWriter Line(string text = "")
        {
            if (text.Length > 0)
            {
                for (int i = 0; i < _indent; i++) _sb.Append(_unit);
                _sb.Append(text);
            }

            _sb.Append('\n');
            return this;
        }

        public CodeWriter Open(string text)
        {
            Line(text);
            Line("{");
            _indent++;
            return this;
        }

        public CodeWriter Close(string suffix = "")
        {
            _indent--;
            Line("}" + suffix);
            return this;
        }

        public void Indent() => _indent++;

        public void Outdent() => _indent--;

        public override string ToString() => _sb.ToString();
    }
}

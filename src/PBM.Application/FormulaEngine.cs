using System.Globalization;

namespace PBM.Application;

public sealed class FormulaEngine : IFormulaEngine
{
    public decimal Evaluate(string expression, IReadOnlyDictionary<string, decimal> variables)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Formula expression is required.", nameof(expression));

        return new Parser(expression, variables).Parse();
    }

    private sealed class Parser(string text, IReadOnlyDictionary<string, decimal> variables)
    {
        private int _position;

        public decimal Parse()
        {
            var value = ParseExpression();
            SkipWhiteSpace();
            if (_position != text.Length)
                throw Error($"Unexpected token '{text[_position]}'.");
            return value;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhiteSpace();
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhiteSpace();
                if (Match('*')) value *= ParseFactor();
                else if (Match('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0) throw new DivideByZeroException("Formula division by zero.");
                    value /= divisor;
                }
                else return value;
            }
        }

        private decimal ParseFactor()
        {
            SkipWhiteSpace();
            if (Match('+')) return ParseFactor();
            if (Match('-')) return -ParseFactor();

            if (Match('('))
            {
                var value = ParseExpression();
                Expect(')');
                return value;
            }

            if (Peek() == '[')
                return ParseVariable();

            if (char.IsLetter(Peek()))
                return ParseFunction();

            return ParseNumber();
        }

        private decimal ParseVariable()
        {
            Expect('[');
            var start = _position;
            while (_position < text.Length && text[_position] != ']') _position++;
            if (_position >= text.Length) throw Error("Missing closing ] in variable reference.");
            var name = text[start.._position].Trim();
            _position++;
            if (!variables.TryGetValue(name, out var value))
                throw new KeyNotFoundException($"Formula variable '{name}' was not provided.");
            return value;
        }

        private decimal ParseFunction()
        {
            var name = ReadIdentifier().ToUpperInvariant();
            Expect('(');
            var args = new List<decimal>();
            SkipWhiteSpace();
            if (!Match(')'))
            {
                do { args.Add(ParseExpression()); SkipWhiteSpace(); }
                while (Match(','));
                Expect(')');
            }

            return name switch
            {
                "ABS" when args.Count == 1 => Math.Abs(args[0]),
                "MIN" when args.Count >= 1 => args.Min(),
                "MAX" when args.Count >= 1 => args.Max(),
                "ROUND" when args.Count == 2 => Math.Round(args[0], checked((int)args[1]), MidpointRounding.AwayFromZero),
                _ => throw Error($"Unknown function or invalid argument count: {name}.")
            };
        }

        private decimal ParseNumber()
        {
            SkipWhiteSpace();
            var start = _position;
            var dotSeen = false;
            while (_position < text.Length)
            {
                var ch = text[_position];
                if (char.IsDigit(ch)) { _position++; continue; }
                if (ch == '.' && !dotSeen) { dotSeen = true; _position++; continue; }
                break;
            }

            if (start == _position) throw Error("Number, variable or function expected.");
            var raw = text[start.._position];
            return decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        private string ReadIdentifier()
        {
            SkipWhiteSpace();
            var start = _position;
            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '_')) _position++;
            if (start == _position) throw Error("Identifier expected.");
            return text[start.._position];
        }

        private void Expect(char expected)
        {
            SkipWhiteSpace();
            if (!Match(expected)) throw Error($"Expected '{expected}'.");
        }

        private bool Match(char ch)
        {
            if (_position < text.Length && text[_position] == ch) { _position++; return true; }
            return false;
        }

        private char Peek() => _position < text.Length ? text[_position] : '\0';
        private void SkipWhiteSpace() { while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++; }
        private FormatException Error(string message) => new($"{message} Position: {_position}.");
    }
}

public static class BudgetCoordinateKey
{
    public static string Create(IEnumerable<DimensionSelection> dimensions)
    {
        var canonical = string.Join('|', dimensions
            .OrderBy(x => x.DimensionId)
            .Select(x => $"{x.DimensionId:N}={x.MemberId:N}"));

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }
}

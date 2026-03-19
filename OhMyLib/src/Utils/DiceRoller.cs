using System.Text;

namespace OhMyLib.Utils;

public static class DiceRoller
{
    public enum TokenType
    {
        Number,
        Dice, // 123 / d
        Plus,
        Minus,
        Star,
        Slash,
        LParen,
        RParen,
        EOF
    }

    public record Token(TokenType Type, string Raw, int NumValue = 0);

    public class DiceResult
    {
        public int Total { get; init; }
        public string Expression { get; init; } = ""; // 原始表达式
        public string Breakdown { get; init; } = "";  // 展开详情，如 [3,5,2]+10

        public override string ToString()
            => $"{Expression} = {Breakdown} = **{Total}**";
    }

    public class Lexer(string src)
    {
        private int _pos;

        public Token Next()
        {
            if (_pos >= src.Length) return new Token(TokenType.EOF, "");

            var c = src[_pos];

            if (char.IsDigit(c))
            {
                var sb = new StringBuilder();
                while (_pos < src.Length && char.IsDigit(src[_pos]))
                    sb.Append(src[_pos++]);
                var num = int.Parse(sb.ToString());
                return new Token(TokenType.Number, sb.ToString(), num);
            }

            _pos++;
            return c switch
            {
                'd' => new Token(TokenType.Dice, "d"),
                '+' => new Token(TokenType.Plus, "+"),
                '-' => new Token(TokenType.Minus, "-"),
                '*' => new Token(TokenType.Star, "*"),
                '/' => new Token(TokenType.Slash, "/"),
                '(' => new Token(TokenType.LParen, "("),
                ')' => new Token(TokenType.RParen, ")"),
                _ => throw new FormatException($"Invalid character: '{c}' at {_pos}")
            };
        }

        public Token Peek()
        {
            var saved = _pos;
            var tok = Next();
            _pos = saved;
            return tok;
        }
    }

// ────────────────────────────────────────────────
// 递归下降解析器 Parser
// 语法：
//   expr   := term  (('+' | '-') term)*
//   term   := unary (('*' | '/') unary)*
//   unary  := '-' unary | primary
//   primary:= '(' expr ')' | diceExpr
//   diceExpr := NUMBER? 'd' NUMBER | NUMBER
// ────────────────────────────────────────────────
    public class Parser(Lexer lexer, Random rng)
    {
        private Token _cur = lexer.Next(); // 当前 token

        private Token Consume()
        {
            var t = _cur;
            _cur = lexer.Next();
            return t;
        }

        private Token Expect(TokenType t)
        {
            if (_cur.Type != t)
                throw new FormatException($"Expect {t} but got '{_cur.Raw}'");
            return Consume();
        }

        // ── 入口 ─────────────────────────────────────
        public DiceResult Parse(string expr)
        {
            var details = new List<string>();
            var total = Expr(details);

            // 把 details 合并成展开字符串
            var breakdown = string.Join(" ", details).Trim();

            return new DiceResult
            {
                Total = total,
                Breakdown = breakdown,
                Expression = expr
            };
        }

        // ── expr: 加减 ────────────────────────────────
        private int Expr(List<string> d)
        {
            var val = Term(d);

            while (_cur.Type is TokenType.Plus or TokenType.Minus)
            {
                var op = Consume();
                var sub = new List<string>();
                var right = Term(sub);

                if (op.Type == TokenType.Plus)
                {
                    val += right;
                    d.Add("+");
                }
                else
                {
                    val -= right;
                    d.Add("-");
                }

                d.AddRange(sub); // ← 最后合并
            }

            return val;
        }

        // ── term: 乘除 ────────────────────────────────
        private int Term(List<string> d)
        {
            var val = Unary(d);

            while (_cur.Type is TokenType.Star or TokenType.Slash)
            {
                var op = Consume();
                var sub = new List<string>();
                var right = Unary(sub);

                if (op.Type == TokenType.Star)
                {
                    val *= right;
                    d.Add("×");
                }
                else
                {
                    if (right == 0) throw new DivideByZeroException();
                    val /= right;
                    d.Add("÷");
                }

                d.AddRange(sub);
            }

            return val;
        }

        // ── unary: 一元负号 ───────────────────────────
        private int Unary(List<string> d)
        {
            if (_cur.Type == TokenType.Minus)
            {
                Consume();
                var val = Primary(d);
                return -val;
            }

            return Primary(d);
        }

        // ── primary: 括号 / 骰子 / 数字 ──────────────
        private int Primary(List<string> d)
        {
            // 括号
            if (_cur.Type == TokenType.LParen)
            {
                Consume();
                var sub = new List<string>();
                var val = Expr(sub);
                Expect(TokenType.RParen);
                d.Add($"({string.Join(" ", sub)})");
                return val;
            }

            // 可能是 NdM 或 dM 或 N
            var count = 1;
            string? countStr = null;

            if (_cur.Type == TokenType.Number)
            {
                var tok = Consume();
                count = tok.NumValue;
                countStr = tok.Raw;
            }

            // 有 'd' → 骰子
            if (_cur.Type == TokenType.Dice)
            {
                Consume();
                var faces = Expect(TokenType.Number).NumValue;

                if (count is < 1 or > 100)
                    throw new ArgumentOutOfRangeException(nameof(count), $"Dice count {count} out of range [1,100]");
                if (faces < 2)
                    throw new ArgumentOutOfRangeException(nameof(faces), $"Dice faces must be at least 2 faces, got {faces}");

                var rolls = new int[count];
                for (var i = 0; i < count; i++)
                    rolls[i] = rng.Next(1, faces + 1);

                var sum = rolls.Sum();

                var detail = $"{count}d{faces}[{string.Join(",", rolls)}]";

                d.Add(detail);
                return sum;
            }

            // 纯数字
            if (countStr != null)
            {
                d.Add(countStr);
                return count;
            }

            throw new FormatException($"Unexpect token: '{_cur.Raw}'");
        }
    }

    public static DiceResult Roll(string expression, Random? rng = null)
    {
        var cleaned = expression.Replace(" ", "").ToLower();
        var lexer = new Lexer(cleaned);
        var parser = new Parser(lexer, rng ?? Random.Shared);
        return parser.Parse(cleaned);
    }
}
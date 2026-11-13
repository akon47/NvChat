using System.Text;

namespace NvChat.Controls;

/// <summary>
/// LaTeX 수식을 읽을 수 있는 유니코드 텍스트로 바꾼다(예: <c>\frac{a}{b} \leq x^2</c> → <c>a/b ≤ x²</c>).
/// 조판 엔진이 아니라 근사 변환이다 — 분수는 한 줄로, 위/아래첨자는 대응하는 유니코드 글자가 있을 때만 작게 쓴다.
/// 외부 수식 라이브러리 없이도 모델이 내놓는 <c>\( … \)</c>, <c>$$ … $$</c> 가 날것의 역슬래시 덩어리로 보이지 않게 하는 것이 목적이며,
/// 결과가 일반 텍스트라 드래그 선택·복사가 그대로 된다. 모르는 명령은 이름만 남긴다.
/// </summary>
internal static class LatexToUnicode
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        // 그리스 문자
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η",
        ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π",
        ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ", ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Upsilon"] = "Υ", ["Phi"] = "Φ",
        ["Psi"] = "Ψ", ["Omega"] = "Ω",

        // 연산자 · 관계
        ["times"] = "×", ["cdot"] = "·", ["div"] = "÷", ["pm"] = "±", ["mp"] = "∓", ["ast"] = "∗", ["star"] = "⋆", ["circ"] = "∘", ["bullet"] = "•",
        ["leq"] = "≤", ["le"] = "≤", ["geq"] = "≥", ["ge"] = "≥", ["neq"] = "≠", ["ne"] = "≠", ["approx"] = "≈", ["equiv"] = "≡", ["sim"] = "∼", ["simeq"] = "≃",
        ["cong"] = "≅", ["propto"] = "∝", ["ll"] = "≪", ["gg"] = "≫", ["mid"] = "|", ["parallel"] = "∥", ["perp"] = "⊥",

        // 큰 연산자 · 미적분
        ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["iint"] = "∬", ["iiint"] = "∭", ["oint"] = "∮", ["partial"] = "∂", ["nabla"] = "∇", ["infty"] = "∞",
        ["lim"] = "lim", ["max"] = "max", ["min"] = "min", ["sup"] = "sup", ["inf"] = "inf", ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp",
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["cot"] = "cot", ["sec"] = "sec", ["csc"] = "csc", ["arcsin"] = "arcsin", ["arccos"] = "arccos",
        ["arctan"] = "arctan", ["sinh"] = "sinh", ["cosh"] = "cosh", ["tanh"] = "tanh", ["det"] = "det", ["dim"] = "dim", ["gcd"] = "gcd", ["deg"] = "deg",
        ["arg"] = "arg", ["mod"] = "mod", ["bmod"] = "mod", ["Pr"] = "Pr",

        // 화살표
        ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["gets"] = "←", ["leftrightarrow"] = "↔", ["Rightarrow"] = "⇒", ["Leftarrow"] = "⇐",
        ["Leftrightarrow"] = "⇔", ["implies"] = "⟹", ["iff"] = "⟺", ["mapsto"] = "↦", ["uparrow"] = "↑", ["downarrow"] = "↓", ["longrightarrow"] = "⟶",

        // 집합 · 논리
        ["in"] = "∈", ["notin"] = "∉", ["ni"] = "∋", ["subset"] = "⊂", ["subseteq"] = "⊆", ["supset"] = "⊃", ["supseteq"] = "⊇", ["cup"] = "∪", ["cap"] = "∩",
        ["setminus"] = "∖", ["emptyset"] = "∅", ["varnothing"] = "∅", ["forall"] = "∀", ["exists"] = "∃", ["nexists"] = "∄", ["neg"] = "¬", ["lnot"] = "¬",
        ["land"] = "∧", ["wedge"] = "∧", ["lor"] = "∨", ["vee"] = "∨", ["oplus"] = "⊕", ["otimes"] = "⊗", ["therefore"] = "∴", ["because"] = "∵",

        // 기타 기호
        ["cdots"] = "⋯", ["ldots"] = "…", ["dots"] = "…", ["vdots"] = "⋮", ["ddots"] = "⋱", ["angle"] = "∠", ["degree"] = "°", ["prime"] = "′",
        ["hbar"] = "ℏ", ["ell"] = "ℓ", ["Re"] = "ℜ", ["Im"] = "ℑ", ["aleph"] = "ℵ", ["langle"] = "⟨", ["rangle"] = "⟩", ["lfloor"] = "⌊", ["rfloor"] = "⌋",
        ["lceil"] = "⌈", ["rceil"] = "⌉", ["lbrace"] = "{", ["rbrace"] = "}", ["vert"] = "|", ["Vert"] = "‖", ["backslash"] = "\\", ["triangle"] = "△",
        ["square"] = "□", ["checkmark"] = "✓", ["dagger"] = "†",

        // 간격 · 무시할 것
        ["quad"] = "  ", ["qquad"] = "    ", ["left"] = "", ["right"] = "", ["big"] = "", ["Big"] = "", ["bigg"] = "", ["Bigg"] = "", ["displaystyle"] = "",
        ["textstyle"] = "", ["limits"] = "", ["nolimits"] = "", ["nonumber"] = "", ["hline"] = "",
    };

    /// <summary>인자를 그대로 내보내는 명령(글꼴/텍스트 지정).</summary>
    private static readonly HashSet<string> PassThrough = new(StringComparer.Ordinal)
    {
        "text", "textbf", "textit", "textrm", "texttt", "mathrm", "mathbf", "mathit", "mathsf", "mathtt", "mathcal", "mathfrak", "operatorname", "boldsymbol", "bm",
        "mbox", "hbox", "label", "tag",
    };

    /// <summary>인자의 각 글자 뒤에 결합 문자를 붙이는 명령(악센트).</summary>
    private static readonly Dictionary<string, char> Accents = new(StringComparer.Ordinal)
    {
        ["hat"] = '̂', ["widehat"] = '̂', ["bar"] = '̄', ["overline"] = '̅', ["vec"] = '⃗', ["dot"] = '̇', ["ddot"] = '̈',
        ["tilde"] = '̃', ["widetilde"] = '̃', ["underline"] = '̲',
    };

    private static readonly Dictionary<char, string> Blackboard = new()
    {
        ['N'] = "ℕ", ['Z'] = "ℤ", ['Q'] = "ℚ", ['R'] = "ℝ", ['C'] = "ℂ", ['P'] = "ℙ", ['H'] = "ℍ", ['E'] = "𝔼",
    };

    private const string SuperscriptFrom = "0123456789+-=()niabcdefghjklmoprstuvwxyzTABDEGHIJKLMNOPRUVW";
    private const string SuperscriptTo = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ⁿⁱᵃᵇᶜᵈᵉᶠᵍʰʲᵏˡᵐᵒᵖʳˢᵗᵘᵛʷˣʸᶻᵀᴬᴮᴰᴱᴳᴴᴵᴶᴷᴸᴹᴺᴼᴾᴿᵁⱽᵂ";
    private const string SubscriptFrom = "0123456789+-=()aehijklmnoprstuvx";
    private const string SubscriptTo = "₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ";

    public static string Convert(string? latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return "";

        int position = 0;
        string result = ParseUntil(latex, ref position, terminator: '\0');

        // 줄 단위로 앞뒤 공백만 정리한다(식 안의 간격은 \quad 등으로 일부러 넣은 것일 수 있어 건드리지 않는다).
        return string.Join("\n", result.Split('\n').Select(line => line.Trim())).Trim();
    }

    /// <summary>terminator(닫는 중괄호 등)를 만날 때까지 변환한다. terminator 는 소비한다.</summary>
    private static string ParseUntil(string s, ref int i, char terminator)
    {
        StringBuilder sb = new();

        while (i < s.Length)
        {
            char c = s[i];

            if (terminator != '\0' && c == terminator)
            {
                i++;
                return sb.ToString();
            }

            switch (c)
            {
                case '\\':
                    i++;
                    AppendCommand(sb, s, ref i);
                    break;

                case '{':
                    i++;
                    sb.Append(ParseUntil(s, ref i, '}'));
                    break;

                case '}':
                    i++; // 짝 없는 닫는 중괄호는 버린다
                    break;

                case '^':
                    i++;
                    sb.Append(Script(ReadArgument(s, ref i), SuperscriptFrom, SuperscriptTo, '^'));
                    break;

                case '_':
                    i++;
                    sb.Append(Script(ReadArgument(s, ref i), SubscriptFrom, SubscriptTo, '_'));
                    break;

                case '&':
                    i++;
                    sb.Append("   "); // 행렬/정렬 환경의 열 구분
                    break;

                case '~':
                    i++;
                    sb.Append(' ');
                    break;

                case '\r':
                    i++;
                    break;

                default:
                    sb.Append(c);
                    i++;
                    break;
            }
        }

        return sb.ToString();
    }

    private static void AppendCommand(StringBuilder sb, string s, ref int i)
    {
        if (i >= s.Length) return;

        // 한 글자 명령: \\ (줄바꿈), \, \; \! \  (간격), \{ \} \$ \% \_ \& \# (문자 그대로)
        if (!char.IsLetter(s[i]))
        {
            char symbol = s[i++];
            switch (symbol)
            {
                case '\\': sb.Append('\n'); break;
                case ',' or ';' or ':' or ' ': sb.Append(' '); break;
                case '!': break;
                case '[' or ']' or '(' or ')': break; // 남은 수식 구분자
                default: sb.Append(symbol); break;
            }
            return;
        }

        int start = i;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        string name = s[start..i];

        switch (name)
        {
            case "frac" or "dfrac" or "tfrac" or "cfrac":
            {
                string numerator = ReadArgument(s, ref i);
                string denominator = ReadArgument(s, ref i);
                sb.Append(Group(numerator)).Append('/').Append(Group(denominator));
                return;
            }

            case "binom":
            {
                string n = ReadArgument(s, ref i);
                string k = ReadArgument(s, ref i);
                sb.Append("C(").Append(n).Append(", ").Append(k).Append(')');
                return;
            }

            case "sqrt":
            {
                string? index = ReadOptionalArgument(s, ref i);
                string radicand = ReadArgument(s, ref i);
                if (index is not null) sb.Append(Script(index, SuperscriptFrom, SuperscriptTo, '^'));
                sb.Append('√').Append(Group(radicand));
                return;
            }

            case "mathbb":
            {
                string argument = ReadArgument(s, ref i);
                foreach (char ch in argument) sb.Append(Blackboard.TryGetValue(ch, out string? mapped) ? mapped : ch.ToString());
                return;
            }

            case "boxed":
                sb.Append('[').Append(ReadArgument(s, ref i)).Append(']');
                return;

            case "begin" or "end":
                ReadArgument(s, ref i); // 환경 이름은 버리고 내용만 남긴다(\\ 는 줄바꿈, & 는 간격으로 이미 처리된다)
                if (name == "begin") ReadOptionalArgument(s, ref i);
                sb.Append('\n');
                return;

            case "not":
                sb.Append('¬');
                return;
        }

        if (Accents.TryGetValue(name, out char accent))
        {
            foreach (char ch in ReadArgument(s, ref i)) sb.Append(ch).Append(accent);
            return;
        }

        if (PassThrough.Contains(name))
        {
            sb.Append(ReadArgument(s, ref i));
            return;
        }

        if (Symbols.TryGetValue(name, out string? replacement))
        {
            sb.Append(replacement);
            // 함수 이름 뒤에 글자가 곧바로 붙으면("\sin x" 의 공백이 명령 구분자로 사라졌을 때) 띄어 준다.
            if (replacement.Length > 1 && char.IsLetter(replacement[0]) && i < s.Length && char.IsLetterOrDigit(s[i])) sb.Append(' ');
            return;
        }

        sb.Append(name); // 모르는 명령은 이름만 남긴다
    }

    /// <summary>다음 인자 하나: 중괄호 묶음이면 그 안을, 아니면 명령 하나 또는 글자 하나를 변환해 돌려준다.</summary>
    private static string ReadArgument(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length) return "";

        if (s[i] == '{')
        {
            i++;
            return ParseUntil(s, ref i, '}');
        }

        if (s[i] == '\\')
        {
            i++;
            StringBuilder sb = new();
            AppendCommand(sb, s, ref i);
            return sb.ToString();
        }

        return s[i++].ToString();
    }

    private static string? ReadOptionalArgument(string s, ref int i)
    {
        int probe = i;
        while (probe < s.Length && s[probe] == ' ') probe++;
        if (probe >= s.Length || s[probe] != '[') return null;

        i = probe + 1;
        return ParseUntil(s, ref i, ']');
    }

    /// <summary>한 덩어리로 읽히지 않는 식(공백·연산자가 낀 식)은 괄호로 묶는다.</summary>
    private static string Group(string expression)
    {
        expression = expression.Trim();
        bool atomic = expression.Length <= 1 || expression.All(c => char.IsLetterOrDigit(c) || c > 0x036F || c == '.');
        return atomic ? expression : "(" + expression + ")";
    }

    /// <summary>모든 글자에 대응하는 위/아래첨자 유니코드가 있으면 그것으로, 아니면 "^(…)" / "_(…)" 로 쓴다.</summary>
    private static string Script(string text, string from, string to, char marker)
    {
        text = text.Trim();
        if (text.Length == 0) return "";

        // 흔한 예외: 도(°)와 프라임
        if (marker == '^' && text is "∘" or "°") return "°";
        if (marker == '^' && text == "′") return "′";

        var toRunes = to.EnumerateRunes().ToArray();
        StringBuilder mapped = new();
        foreach (char c in text)
        {
            int index = from.IndexOf(c);
            if (index < 0 || index >= toRunes.Length) return text.Length == 1 ? marker + text : marker + "(" + text + ")";
            mapped.Append(toRunes[index].ToString());
        }
        return mapped.ToString();
    }
}

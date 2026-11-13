using System.Text;

namespace NvChat.Services;

/// <summary>
/// 스트리밍 본문에 인라인으로 섞여 오는 <c>&lt;think&gt;…&lt;/think&gt;</c> 를 실시간으로 본문/추론으로 가른다.
/// 태그가 청크 경계에서 잘려 와도(예: "&lt;thi" + "nk&gt;") 올바로 처리하도록, 태그의 접두사일 수 있는 꼬리는 다음 청크까지 붙들어 둔다.
/// 여는 태그는 응답의 맨 앞(본문이 나오기 전)에서만 인정한다. 답변 중간에 설명용으로 등장하는 "&lt;think&gt;" 글자는 그대로 본문이다.
/// </summary>
internal sealed class ThinkTagSplitter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    private readonly StringBuilder _pending = new();
    private bool _inThink;
    private bool _thinkClosed;
    private bool _contentStarted;
    private bool _reasoningStarted;

    /// <summary>한 번이라도 인라인 추론 블록을 만났는지.</summary>
    public bool SawThink { get; private set; }

    /// <summary>새 델타를 넣고, 이번에 확정된 본문/추론 조각을 돌려준다(없으면 빈 문자열).</summary>
    public (string Content, string Reasoning) Push(string? delta)
    {
        if (string.IsNullOrEmpty(delta)) return ("", "");
        _pending.Append(delta);
        return Drain(final: false);
    }

    /// <summary>스트림이 끝났을 때 붙들고 있던 꼬리를 내보낸다.</summary>
    public (string Content, string Reasoning) Flush() => Drain(final: true);

    private (string Content, string Reasoning) Drain(bool final)
    {
        StringBuilder content = new();
        StringBuilder reasoning = new();
        string buffer = _pending.ToString();
        _pending.Clear();

        while (buffer.Length > 0)
        {
            if (_inThink)
            {
                int close = buffer.IndexOf(Close, StringComparison.OrdinalIgnoreCase);
                if (close >= 0)
                {
                    AppendReasoning(reasoning, buffer[..close]);
                    buffer = buffer[(close + Close.Length)..];
                    _inThink = false;
                    _thinkClosed = true;
                    continue;
                }

                int keep = final ? 0 : PartialSuffixLength(buffer, Close);
                AppendReasoning(reasoning, buffer[..^keep]);
                _pending.Append(buffer[^keep..]);
                break;
            }

            // 여는 태그는 본문이 시작되기 전, 그리고 한 번만 인정한다.
            if (!_contentStarted && !_thinkClosed)
            {
                string lead = buffer.TrimStart();
                if (lead.StartsWith(Open, StringComparison.OrdinalIgnoreCase))
                {
                    buffer = lead[Open.Length..];
                    _inThink = true;
                    SawThink = true;
                    continue;
                }

                // 아직 "<thi" 처럼 여는 태그의 앞부분일 수 있으면 다음 청크를 기다린다.
                if (!final && lead.Length < Open.Length && Open.StartsWith(lead, StringComparison.OrdinalIgnoreCase))
                {
                    _pending.Append(buffer);
                    break;
                }
            }

            AppendContent(content, buffer);
            break;
        }

        return (content.ToString(), reasoning.ToString());
    }

    private void AppendContent(StringBuilder target, string text)
    {
        // 추론 블록 뒤의 빈 줄 등, 본문 맨 앞의 공백은 버린다.
        if (!_contentStarted)
        {
            text = text.TrimStart();
            if (text.Length == 0) return;
            _contentStarted = true;
        }
        target.Append(text);
    }

    private void AppendReasoning(StringBuilder target, string text)
    {
        if (!_reasoningStarted)
        {
            text = text.TrimStart();
            if (text.Length == 0) return;
            _reasoningStarted = true;
        }
        target.Append(text);
    }

    /// <summary>buffer 의 끝이 tag 의 접두사와 겹치는 가장 긴 길이. (다음 청크에서 태그가 완성될 수 있는 꼬리)</summary>
    private static int PartialSuffixLength(string buffer, string tag)
    {
        for (int length = Math.Min(tag.Length - 1, buffer.Length); length > 0; length--)
        {
            if (string.Compare(buffer, buffer.Length - length, tag, 0, length, StringComparison.OrdinalIgnoreCase) == 0) return length;
        }
        return 0;
    }

    /// <summary>
    /// 스트림이 끝난 뒤의 보정. 여는 태그 없이 <c>&lt;/think&gt;</c> 만 오는 모델이 있어, 닫는 태그 앞쪽을 통째로 추론으로 옮긴다.
    /// 옮길 것이 없으면 null.
    /// </summary>
    public static (string Content, string Reasoning)? SplitOrphanClose(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        int close = content.IndexOf(Close, StringComparison.OrdinalIgnoreCase);
        if (close < 0) return null;

        string reasoning = content[..close];
        int open = reasoning.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
        if (open >= 0) reasoning = reasoning[..open] + reasoning[(open + Open.Length)..];

        return (content[(close + Close.Length)..].Trim(), reasoning.Trim());
    }
}

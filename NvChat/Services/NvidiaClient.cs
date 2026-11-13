using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NvChat.Localization;
using NvChat.Models;
using NvChat.Services.Api;

namespace NvChat.Services;

/// <summary>
/// <see cref="INvidiaClient"/> 의 HttpClient 기반 구현.
/// 베이스 URL / API 키는 호출 시점의 <see cref="AppSettings"/> 에서 읽으므로 설정이 바뀌어도 즉시 반영된다.
/// </summary>
public sealed class NvidiaClient(Func<AppSettings> settingsAccessor) : INvidiaClient, IDisposable
{
    /// <summary>
    /// 스트림이 이만큼 아무것도 보내지 않으면 끊긴 것으로 본다. 전체 타임아웃은 없으므로(긴 답변을 끊지 않으려고),
    /// 서버가 헤더만 보내고 멈추거나 중간에 조용히 죽으면 이것이 없을 때 사용자가 직접 중단할 때까지 기다리게 된다.
    /// </summary>
    internal static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(120);

    private static LocalizationManager L => LocalizationManager.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // 스트리밍 응답은 몇 분 이상 이어질 수 있으므로 전체 타임아웃을 끄고 취소는 CancellationToken 과 유휴 감시로 제어한다.
    // (HttpClient.Timeout 은 ResponseHeadersRead 를 써도 본문 읽기 전체에 적용되어 긴 응답을 끊어버린다.)
    // 트레이에 며칠씩 떠 있는 앱이므로 연결을 주기적으로 새로 맺어 DNS 변경을 따라간다.
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(20),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public void Dispose() => _http.Dispose();

    public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        GenerationParameters? parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = GetConfiguredSettings();
        var request = BuildRequest(model, messages, parameters ?? new(), stream: true);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, CombineUrl(settings.BaseUrl, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");
        httpRequest.Content = CreateJsonContent(JsonSerializer.Serialize(request, JsonOptions));

        // 유휴 감시: 사용자의 취소 토큰과 묶어 두고, 무언가 도착할 때마다 타이머를 다시 건다.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(StreamIdleTimeout);

        using var response = await GuardIdle(() => _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, idle.Token), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = await GuardIdle(() => response.Content.ReadAsStringAsync(idle.Token), cancellationToken).ConfigureAwait(false);
            throw CreateException((int)response.StatusCode, body, response);
        }

        using var stream = await GuardIdle(() => response.Content.ReadAsStreamAsync(idle.Token), cancellationToken).ConfigureAwait(false);
        using StreamReader reader = new(stream, Encoding.UTF8);

        while (true)
        {
            idle.CancelAfter(StreamIdleTimeout);
            string? rawLine = await GuardIdle(() => reader.ReadLineAsync(idle.Token).AsTask(), cancellationToken).ConfigureAwait(false);
            if (rawLine is null) break;

            string line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            var chunk = ParseChunk(data);

            // 200 으로 시작한 뒤 본문 안에 에러 객체를 흘려보내는 경우가 있다. 그냥 무시하면 "빈 응답"처럼 보이므로 실제 사유를 올려 보낸다.
            if (chunk.Error is { } streamError) throw new NvidiaApiException(streamError);

            // 사용량은 choices 가 빈 마지막 청크에 따로 온다.
            if (chunk.Usage is { } usage) yield return new ChatStreamDelta(null, null, null, usage);
            if (chunk.Delta is { } delta) yield return delta;
        }
    }

    /// <summary>
    /// 유휴 타임아웃으로 취소된 경우를 사용자 취소와 구분해, 사유가 담긴 예외로 바꾼다.
    /// (반복자 메서드에서는 yield 를 감싼 try/catch 를 쓸 수 없어 대기 지점마다 이 헬퍼로 감싼다.)
    /// </summary>
    private static async Task<T> GuardIdle<T>(Func<Task<T>> operation, CancellationToken userToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!userToken.IsCancellationRequested)
        {
            throw new NvidiaApiException(L.Tr("StreamIdleTimeout", (int)StreamIdleTimeout.TotalSeconds));
        }
        catch (IOException ex) when (!userToken.IsCancellationRequested)
        {
            throw new NvidiaApiException(L.Tr("StreamInterrupted", ex.Message), ex);
        }
        catch (HttpRequestException ex)
        {
            throw new NvidiaApiException(L.Tr("ConnectionFailed", ex.Message), ex);
        }
    }

    public async Task<ChatCompletionOutcome> CompleteChatAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        GenerationParameters? parameters,
        CancellationToken cancellationToken)
    {
        var settings = GetConfiguredSettings();
        var request = BuildRequest(model, messages, parameters ?? new(), stream: false);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, CombineUrl(settings.BaseUrl, "chat/completions"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        httpRequest.Content = CreateJsonContent(JsonSerializer.Serialize(request, JsonOptions));

        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw CreateException((int)response.StatusCode, body, response);

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
        string content = parsed?.Choices is [{ Message: { } message }, ..] ? message.Content ?? "" : "";
        var usage = parsed?.Usage is { } u ? new ChatUsage(u.PromptTokens, u.CompletionTokens, true) : default;
        return new ChatCompletionOutcome(content, usage);
    }

    public async Task<IReadOnlyList<NvModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var settings = GetConfiguredSettings();

        using HttpRequestMessage request = new(HttpMethod.Get, CombineUrl(settings.BaseUrl, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw CreateException((int)response.StatusCode, body, response);

        var parsed = JsonSerializer.Deserialize<ModelsResponse>(body, JsonOptions);
        return (parsed?.Data ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => NvModel.FromId(m.Id))
            .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m.Publisher, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ChatCompletionRequest BuildRequest(string model, IEnumerable<ChatMessage> messages, GenerationParameters parameters, bool stream) => new()
    {
        Model = model,
        Stream = stream,
        Temperature = parameters.Temperature,
        TopP = parameters.TopP,
        MaxTokens = parameters.MaxTokens,
        FrequencyPenalty = parameters.FrequencyPenalty,
        PresencePenalty = parameters.PresencePenalty,
        Messages = messages.Select(ToPayload).ToList(),
        StreamOptions = stream ? new StreamOptions { IncludeUsage = true } : null,
    };

    /// <summary>SSE 한 줄의 data 를 한 번만 파싱해 델타/사용량/오류로 나눈다. 부분·비정상 청크는 전부 null 로 조용히 건너뛴다.</summary>
    internal static (ChatStreamDelta? Delta, ChatUsage? Usage, string? Error) ParseChunk(string data)
    {
        ChatCompletionChunk? chunk;
        try { chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions); }
        catch (JsonException) { return default; }
        if (chunk is null) return default;

        // 에러 페이로드: choices 없이 error 만 있는 객체.
        if (chunk.Choices is null && chunk.Error is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
            return (null, null, TryExtractErrorDetail(data) is { Length: > 0 } detail ? detail : data);

        ChatUsage? usage = chunk.Usage is { } u ? new ChatUsage(u.PromptTokens, u.CompletionTokens, true) : null;

        ChatStreamDelta? delta = null;
        if (chunk.Choices is [{ } choice, ..])
        {
            string? content = choice.Delta?.Content;
            string? reasoning = choice.Delta?.ReasoningContent ?? choice.Delta?.Reasoning;

            // 본문이 없어도 종료 사유가 있으면 전달한다. (빈 응답의 원인 표시용)
            if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoning) || !string.IsNullOrEmpty(choice.FinishReason))
                delta = new ChatStreamDelta(content, reasoning, choice.FinishReason);
        }

        return (delta, usage, null);
    }

    private AppSettings GetConfiguredSettings()
    {
        var settings = settingsAccessor();
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new NvidiaApiException(L["ApiKeyNotSet"]);
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)) throw new NvidiaApiException(L["BaseUrlEmpty"]);
        return settings;
    }

    private static ChatMessagePayload ToPayload(ChatMessage message)
    {
        string role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.Assistant => "assistant",
            _ => "user",
        };

        // 이미지가 있으면 멀티모달 콘텐츠 파트 배열로 전송(비전 모델).
        if (message.Images is { Count: > 0 } images)
        {
            List<object> parts = [];
            if (!string.IsNullOrEmpty(message.Content)) parts.Add(new TextContentPart { Text = message.Content });
            parts.AddRange(images.Select(image => new ImageContentPart { ImageUrl = new ImageUrlValue { Url = image } }));
            return new ChatMessagePayload { Role = role, Content = parts };
        }

        return new ChatMessagePayload { Role = role, Content = message.Content ?? "" };
    }

    /// <summary>
    /// JSON 본문을 만든다. StringContent(json, UTF8, "application/json") 은 Content-Type 을 "application/json; charset=utf-8" 로
    /// 보내는데, 일부 모델 엔드포인트가 이를 거부하고 415 를 돌려준다. 본문은 UTF-8 로 쓰되 charset 파라미터는 떼어낸다.
    /// </summary>
    private static StringContent CreateJsonContent(string json)
    {
        StringContent content = new(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static string CombineUrl(string baseUrl, string relative) => $"{baseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";

    /// <summary>에러 응답 본문에서 사람이 읽을 메시지를 최대한 뽑아 예외로 만든다.</summary>
    private static NvidiaApiException CreateException(int statusCode, string body, HttpResponseMessage? response = null)
    {
        string detail = TryExtractErrorDetail(body) is { Length: > 0 } d ? d
            : string.IsNullOrWhiteSpace(body) ? L["NoBody"] : body.Trim();
        if (detail.Length > 500) detail = detail[..500] + "…";

        int? retryAfter = statusCode == 429 ? TryReadRetryAfterSeconds(response) : null;
        string hint = statusCode switch
        {
            401 or 403 => L["HintCheckKey"],
            429 => retryAfter is { } wait ? L.Tr("HintRateLimitWait", wait) : L["HintRateLimit"],
            400 or 413 when LooksLikeContextOverflow(detail) => L["HintContextTooLong"],
            _ => "",
        };

        return new NvidiaApiException($"[{statusCode}] {detail}{hint}") { StatusCode = statusCode, RetryAfterSeconds = retryAfter };
    }

    private static bool LooksLikeContextOverflow(string detail) =>
        detail.Contains("context", StringComparison.OrdinalIgnoreCase) && (detail.Contains("length", StringComparison.OrdinalIgnoreCase) || detail.Contains("token", StringComparison.OrdinalIgnoreCase))
        || detail.Contains("too long", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("maximum", StringComparison.OrdinalIgnoreCase) && detail.Contains("token", StringComparison.OrdinalIgnoreCase);

    /// <summary>429 응답의 Retry-After 헤더에서 대기 시간(초)을 읽는다.</summary>
    private static int? TryReadRetryAfterSeconds(HttpResponseMessage? response) => response?.Headers.RetryAfter switch
    {
        { Delta: { } delta } => (int)Math.Ceiling(delta.TotalSeconds),
        { Date: { } date } => Math.Max(0, (int)Math.Ceiling((date - DateTimeOffset.Now).TotalSeconds)),
        _ => null,
    };

    internal static string? TryExtractErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // { "error": { "message": "..." } } 또는 { "error": "..." }
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString();
            }

            // { "detail": "..." } 또는 { "detail": [ { "msg": "..." } ] }
            if (root.TryGetProperty("detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.String) return detail.GetString();
                if (detail.ValueKind == JsonValueKind.Array && detail.GetArrayLength() > 0
                    && detail[0] is { ValueKind: JsonValueKind.Object } first
                    && first.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return msg.GetString();
            }

            if (root.TryGetProperty("message", out var topMessage) && topMessage.ValueKind == JsonValueKind.String) return topMessage.GetString();
        }
        catch (JsonException) { /* 본문이 JSON 이 아니면 원문을 그대로 쓴다. */ }
        return null;
    }
}

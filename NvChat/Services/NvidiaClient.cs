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
    private static LocalizationManager L => LocalizationManager.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // 스트리밍 응답은 몇 분 이상 이어질 수 있으므로 전체 타임아웃을 끄고 취소는 전적으로 CancellationToken 으로 제어한다.
    // (HttpClient.Timeout 은 ResponseHeadersRead 를 써도 본문 읽기 전체에 적용되어 긴 응답을 끊어버린다.)
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

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

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new NvidiaApiException(BuildErrorMessage((int)response.StatusCode, body, response));
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using StreamReader reader = new(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            // 200 으로 시작한 뒤 본문 안에 에러 객체를 흘려보내는 경우가 있다. 그냥 무시하면 "빈 응답"처럼 보이므로 실제 사유를 올려 보낸다.
            if (TryParseStreamError(data) is { } streamError) throw new NvidiaApiException(streamError);

            // 사용량은 choices 가 빈 마지막 청크에 따로 온다.
            if (TryParseUsage(data) is { } usage)
                yield return new ChatStreamDelta(null, null, null, new ChatUsage(usage.PromptTokens, usage.CompletionTokens, true));

            if (TryParseDelta(data) is { } delta) yield return delta;
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
        if (!response.IsSuccessStatusCode) throw new NvidiaApiException(BuildErrorMessage((int)response.StatusCode, body, response));

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
        if (!response.IsSuccessStatusCode) throw new NvidiaApiException(BuildErrorMessage((int)response.StatusCode, body, response));

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

    /// <summary>청크에 담긴 토큰 사용량을 읽는다. (stream_options.include_usage 지원 서버만)</summary>
    private static TokenUsage? TryParseUsage(string data)
    {
        if (!data.Contains("\"usage\"", StringComparison.Ordinal)) return null;
        try { return JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions)?.Usage; }
        catch (JsonException) { return null; }
    }

    /// <summary>SSE 본문에 실려 온 에러 페이로드면 그 메시지. (choices 가 있는 정상 청크는 제외)</summary>
    private static string? TryParseStreamError(string data)
    {
        if (!data.Contains("\"error\"", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.TryGetProperty("choices", out _)) return null;
            return TryExtractErrorDetail(data) is { Length: > 0 } detail ? detail : null;
        }
        catch (JsonException) { return null; }
    }

    private static ChatStreamDelta? TryParseDelta(string data)
    {
        try
        {
            if (JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions)?.Choices is not [{ } choice, ..]) return null;

            string? content = choice.Delta?.Content;
            string? reasoning = choice.Delta?.ReasoningContent;

            // 본문이 없어도 종료 사유가 있으면 전달한다. (빈 응답의 원인 표시용)
            if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(reasoning) && string.IsNullOrEmpty(choice.FinishReason)) return null;
            return new ChatStreamDelta(content, reasoning, choice.FinishReason);
        }
        catch (JsonException) { return null; } // 부분/비정상 청크는 조용히 건너뛴다.
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

    /// <summary>에러 응답 본문에서 사람이 읽을 메시지를 최대한 뽑아낸다.</summary>
    private static string BuildErrorMessage(int statusCode, string body, HttpResponseMessage? response = null)
    {
        string detail = TryExtractErrorDetail(body) is { Length: > 0 } d ? d
            : string.IsNullOrWhiteSpace(body) ? L["NoBody"] : body.Trim();
        if (detail.Length > 500) detail = detail[..500] + "…";

        string hint = statusCode switch
        {
            401 or 403 => L["HintCheckKey"],
            429 => TryReadRetryAfterSeconds(response) is { } wait ? L.Tr("HintRateLimitWait", wait) : L["HintRateLimit"],
            _ => "",
        };
        return $"[{statusCode}] {detail}{hint}";
    }

    /// <summary>429 응답의 Retry-After 헤더에서 대기 시간(초)을 읽는다.</summary>
    private static int? TryReadRetryAfterSeconds(HttpResponseMessage? response) => response?.Headers.RetryAfter switch
    {
        { Delta: { } delta } => (int)Math.Ceiling(delta.TotalSeconds),
        { Date: { } date } => Math.Max(0, (int)Math.Ceiling((date - DateTimeOffset.Now).TotalSeconds)),
        _ => null,
    };

    private static string? TryExtractErrorDetail(string? body)
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

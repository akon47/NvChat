using NvChat.Models;
using NvChat.Services.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NvChat.Services
{
    /// <summary>
    /// <see cref="INvidiaClient"/> 의 HttpClient 기반 구현.
    /// 베이스 URL / API 키는 호출 시점의 <see cref="AppSettings"/> 에서 읽으므로
    /// 설정이 바뀌어도 즉시 반영된다.
    /// </summary>
    public sealed class NvidiaClient : INvidiaClient, IDisposable
    {
        #region Constructors

        public NvidiaClient(Func<AppSettings> settingsAccessor)
        {
            _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));

            _http = new HttpClient
            {
                // 스트리밍 응답은 몇 분 이상 이어질 수 있으므로 전체 타임아웃을 끄고,
                // 취소는 전적으로 CancellationToken 으로 제어한다. (HttpClient.Timeout 은
                // ResponseHeadersRead 를 써도 본문 읽기 전체에 적용되어 긴 응답을 끊어버린다.)
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
        }

        #endregion


        #region IDisposable

        public void Dispose()
        {
            _http.Dispose();
        }

        #endregion


        #region Fields

        private readonly Func<AppSettings> _settingsAccessor;
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        #endregion


        #region Helpers - Chat

        public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
            string model,
            IEnumerable<ChatMessage> messages,
            GenerationParameters parameters,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var settings = GetConfiguredSettings();
            parameters = parameters ?? new GenerationParameters();

            var request = new ChatCompletionRequest
            {
                Model = model,
                Stream = true,
                Temperature = parameters.Temperature,
                TopP = parameters.TopP,
                MaxTokens = parameters.MaxTokens,
                FrequencyPenalty = parameters.FrequencyPenalty,
                PresencePenalty = parameters.PresencePenalty,
                Messages = messages.Select(ToPayload).ToList()
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CombineUrl(settings.BaseUrl, "chat/completions"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            httpRequest.Headers.Accept.ParseAdd("text/event-stream");
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new NvidiaApiException(BuildErrorMessage((int)response.StatusCode, body));
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                line = line.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("data:", StringComparison.Ordinal) == false)
                    continue;

                var data = line.Substring("data:".Length).Trim();
                if (data == "[DONE]")
                    break;

                if (TryParseDelta(data, out var delta))
                    yield return delta;
            }
        }

        private static bool TryParseDelta(string data, out ChatStreamDelta delta)
        {
            delta = default;

            try
            {
                var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, _jsonOptions);
                var d = chunk?.Choices != null && chunk.Choices.Count > 0 ? chunk.Choices[0].Delta : null;
                if (d == null)
                    return false;

                if (string.IsNullOrEmpty(d.Content) && string.IsNullOrEmpty(d.ReasoningContent))
                    return false;

                delta = new ChatStreamDelta(d.Content, d.ReasoningContent);
                return true;
            }
            catch (JsonException)
            {
                // 부분/비정상 청크는 조용히 건너뛴다.
                return false;
            }
        }

        public async Task<string> CompleteChatAsync(
            string model,
            IEnumerable<ChatMessage> messages,
            GenerationParameters parameters,
            CancellationToken cancellationToken)
        {
            var settings = GetConfiguredSettings();
            parameters = parameters ?? new GenerationParameters();

            var request = new ChatCompletionRequest
            {
                Model = model,
                Stream = false,
                Temperature = parameters.Temperature,
                TopP = parameters.TopP,
                MaxTokens = parameters.MaxTokens,
                FrequencyPenalty = parameters.FrequencyPenalty,
                PresencePenalty = parameters.PresencePenalty,
                Messages = messages.Select(ToPayload).ToList()
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CombineUrl(settings.BaseUrl, "chat/completions"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode == false)
                throw new NvidiaApiException(BuildErrorMessage((int)response.StatusCode, body));

            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, _jsonOptions);
            var message = parsed?.Choices != null && parsed.Choices.Count > 0 ? parsed.Choices[0].Message : null;
            return message?.Content ?? string.Empty;
        }

        #endregion


        #region Helpers - Models

        public async Task<IReadOnlyList<NvModel>> GetModelsAsync(CancellationToken cancellationToken)
        {
            var settings = GetConfiguredSettings();

            using var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(settings.BaseUrl, "models"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode == false)
                throw new NvidiaApiException(BuildErrorMessage((int)response.StatusCode, body));

            var parsed = JsonSerializer.Deserialize<ModelsResponse>(body, _jsonOptions);
            var models = (parsed?.Data ?? new List<ModelEntry>())
                .Where(m => string.IsNullOrWhiteSpace(m.Id) == false)
                .Select(m => NvModel.FromId(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(m => m.Publisher, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return models;
        }

        #endregion


        #region Helpers - Common

        private AppSettings GetConfiguredSettings()
        {
            var settings = _settingsAccessor() ?? new AppSettings();

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new NvidiaApiException("API 키가 설정되지 않았습니다. 설정에서 build.nvidia.com API 키를 입력하세요.");

            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                throw new NvidiaApiException("베이스 URL 이 비어 있습니다.");

            return settings;
        }

        private static ChatMessagePayload ToPayload(ChatMessage message)
        {
            var role = ToRoleString(message.Role);

            // 이미지가 있으면 멀티모달 콘텐츠 파트 배열로 전송(비전 모델).
            if (message.Images != null && message.Images.Count > 0)
            {
                var parts = new List<object>();

                if (string.IsNullOrEmpty(message.Content) == false)
                    parts.Add(new TextContentPart { Text = message.Content });

                foreach (var image in message.Images)
                    parts.Add(new ImageContentPart { ImageUrl = new ImageUrlValue { Url = image } });

                return new ChatMessagePayload { Role = role, Content = parts };
            }

            return new ChatMessagePayload { Role = role, Content = message.Content ?? string.Empty };
        }

        private static string ToRoleString(ChatRole role)
        {
            switch (role)
            {
                case ChatRole.System: return "system";
                case ChatRole.Assistant: return "assistant";
                default: return "user";
            }
        }

        private static string CombineUrl(string baseUrl, string relative)
        {
            baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            relative = (relative ?? string.Empty).TrimStart('/');
            return $"{baseUrl}/{relative}";
        }

        /// <summary>
        /// 에러 응답 본문에서 사람이 읽을 메시지를 최대한 뽑아낸다.
        /// </summary>
        private static string BuildErrorMessage(int statusCode, string body)
        {
            var detail = TryExtractErrorDetail(body);

            if (string.IsNullOrWhiteSpace(detail))
                detail = string.IsNullOrWhiteSpace(body) ? "(본문 없음)" : body.Trim();

            if (detail.Length > 500)
                detail = detail.Substring(0, 500) + "…";

            var hint = statusCode == 401 || statusCode == 403
                ? " (API 키가 올바른지 확인하세요)"
                : statusCode == 429
                    ? " (요청이 너무 많습니다. 잠시 후 다시 시도하세요)"
                    : string.Empty;

            return $"[{statusCode}] {detail}{hint}";
        }

        private static string TryExtractErrorDetail(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    // { "error": { "message": "..." } } 또는 { "error": "..." }
                    if (root.TryGetProperty("error", out var error))
                    {
                        if (error.ValueKind == JsonValueKind.String)
                            return error.GetString();

                        if (error.ValueKind == JsonValueKind.Object &&
                            error.TryGetProperty("message", out var message) &&
                            message.ValueKind == JsonValueKind.String)
                            return message.GetString();
                    }

                    // { "detail": "..." } 또는 { "detail": [ { "msg": "..." } ] }
                    if (root.TryGetProperty("detail", out var d))
                    {
                        if (d.ValueKind == JsonValueKind.String)
                            return d.GetString();

                        if (d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0)
                        {
                            var first = d[0];
                            if (first.ValueKind == JsonValueKind.Object &&
                                first.TryGetProperty("msg", out var msg) &&
                                msg.ValueKind == JsonValueKind.String)
                                return msg.GetString();
                        }
                    }

                    if (root.TryGetProperty("message", out var topMessage) &&
                        topMessage.ValueKind == JsonValueKind.String)
                        return topMessage.GetString();
                }
            }
            catch (JsonException)
            {
                // 본문이 JSON 이 아니면 원문을 그대로 쓴다.
            }

            return null;
        }

        #endregion
    }
}

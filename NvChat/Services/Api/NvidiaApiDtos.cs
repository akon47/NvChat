using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NvChat.Services.Api
{
    /// <summary>
    /// POST /v1/chat/completions 요청 본문 (OpenAI 호환).
    /// </summary>
    internal sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("messages")]
        public List<ChatMessagePayload> Messages { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("frequency_penalty")]
        public double FrequencyPenalty { get; set; }

        [JsonPropertyName("presence_penalty")]
        public double PresencePenalty { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        /// <summary>스트리밍에서도 마지막 청크에 usage 를 실어 달라고 요청한다. (미지원 서버는 무시)</summary>
        [JsonPropertyName("stream_options")]
        public StreamOptions StreamOptions { get; set; }
    }

    internal sealed class StreamOptions
    {
        [JsonPropertyName("include_usage")]
        public bool IncludeUsage { get; set; }
    }

    /// <summary>
    /// 토큰 사용량. 스트리밍에서는 choices 가 빈 마지막 청크에 담겨 온다.
    /// </summary>
    internal sealed class TokenUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    internal sealed class ChatMessagePayload
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        // 텍스트만이면 string, 이미지가 있으면 콘텐츠 파트 배열(List&lt;object&gt;)이 들어간다.
        [JsonPropertyName("content")]
        public object Content { get; set; }
    }

    internal sealed class TextContentPart
    {
        [JsonPropertyName("type")]
        public string Type => "text";

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    internal sealed class ImageContentPart
    {
        [JsonPropertyName("type")]
        public string Type => "image_url";

        [JsonPropertyName("image_url")]
        public ImageUrlValue ImageUrl { get; set; }
    }

    internal sealed class ImageUrlValue
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// 스트리밍 응답의 한 청크 (data: {...}).
    /// </summary>
    internal sealed class ChatCompletionChunk
    {
        [JsonPropertyName("choices")]
        public List<ChunkChoice> Choices { get; set; }

        [JsonPropertyName("usage")]
        public TokenUsage Usage { get; set; }
    }

    internal sealed class ChunkChoice
    {
        [JsonPropertyName("delta")]
        public ChunkDelta Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }
    }

    internal sealed class ChunkDelta
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }

        // 일부 추론(reasoning) 모델은 별도 필드로 사고 과정을 스트리밍한다.
        [JsonPropertyName("reasoning_content")]
        public string ReasoningContent { get; set; }
    }

    /// <summary>
    /// 비스트리밍 채팅 완성 응답.
    /// </summary>
    internal sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ResponseChoice> Choices { get; set; }

        [JsonPropertyName("usage")]
        public TokenUsage Usage { get; set; }
    }

    internal sealed class ResponseChoice
    {
        [JsonPropertyName("message")]
        public ResponseMessage Message { get; set; }
    }

    internal sealed class ResponseMessage
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("reasoning_content")]
        public string ReasoningContent { get; set; }
    }

    /// <summary>
    /// GET /v1/models 응답.
    /// </summary>
    internal sealed class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<ModelEntry> Data { get; set; }
    }

    internal sealed class ModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("owned_by")]
        public string OwnedBy { get; set; }
    }
}

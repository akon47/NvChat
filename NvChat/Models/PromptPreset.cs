namespace NvChat.Models
{
    /// <summary>
    /// 저장된 프롬프트 프리셋(이름 + 본문).
    /// </summary>
    public class PromptPreset
    {
        public PromptPreset()
        {
        }

        public PromptPreset(string name, string text)
        {
            Name = name;
            Text = text;
        }

        public string Name { get; set; }

        public string Text { get; set; }
    }
}

using NvChat.Models;

namespace NvChat.Services
{
    public interface ISettingsStore
    {
        /// <summary>로드 중 문제가 있었을 경우의 안내 메시지(정상 시 null).</summary>
        string LoadError { get; }

        AppSettings Load();

        void Save(AppSettings settings);
    }
}

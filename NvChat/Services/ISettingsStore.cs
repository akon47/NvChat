using NvChat.Models;

namespace NvChat.Services
{
    public interface ISettingsStore
    {
        AppSettings Load();

        void Save(AppSettings settings);
    }
}

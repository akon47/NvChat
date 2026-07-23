using NvChat.Commands;
using NvChat.Localization;
using NvChat.Models;
using NvChat.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 설정 창의 뷰모델. API 키/베이스 URL/기본 파라미터를 편집한다.
    /// </summary>
    public class SettingsViewModel : WindowViewModel
    {
        private static LocalizationManager L => LocalizationManager.Instance;

        #region Constructors

        public SettingsViewModel(AppSettings current)
        {
            current = current ?? new AppSettings();
            var parameters = current.DefaultParameters ?? new GenerationParameters();

            _apiKey = current.ApiKey;
            _baseUrl = string.IsNullOrWhiteSpace(current.BaseUrl) ? new AppSettings().BaseUrl : current.BaseUrl;
            _defaultModelId = current.DefaultModelId;
            _defaultSystemPrompt = current.DefaultSystemPrompt;
            _sendOnEnter = current.SendOnEnter;
            _generateTitles = current.GenerateTitles;
            _aboutYou = current.AboutYou;
            _responseStyle = current.ResponseStyle;
            _globalHotkey = string.IsNullOrWhiteSpace(current.GlobalHotkey) ? "Ctrl+Shift+Space" : current.GlobalHotkey;
            _minimizeToTray = current.MinimizeToTrayOnClose;
            _autoCheckUpdates = current.AutoCheckUpdates;

            Languages = Localization.LocalizationManager.Available
                .Select(a => new LanguageOption(a.Culture, a.Display))
                .ToArray();
            _originalCulture = Localization.LocalizationManager.Instance.Culture;
            _selectedLanguage = Languages.FirstOrDefault(l => l.Culture == _originalCulture) ?? Languages.FirstOrDefault();
            Presets = new ObservableCollection<PromptPreset>((current.Presets ?? AppSettings.DefaultPresets()).Select(p => new PromptPreset(p.Name, p.Text)));
            _temperature = parameters.Temperature;
            _topP = parameters.TopP;
            _maxTokens = parameters.MaxTokens;
            _frequencyPenalty = parameters.FrequencyPenalty;
            _presencePenalty = parameters.PresencePenalty;
        }

        #endregion


        #region Properties

        /// <summary>
        /// 저장 버튼으로 닫혔는지 여부. 창이 닫힌 뒤 호출측에서 확인한다.
        /// </summary>
        public bool Saved { get; private set; }

        #endregion


        #region Bindable Properties

        private string _apiKey;

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (SetProperty(ref _apiKey, value))
                    ResetTestStatus();
            }
        }


        private string _baseUrl;

        public string BaseUrl
        {
            get => _baseUrl;
            set
            {
                if (SetProperty(ref _baseUrl, value))
                    ResetTestStatus();
            }
        }


        private string _defaultModelId;

        public string DefaultModelId
        {
            get => _defaultModelId;
            set => SetProperty(ref _defaultModelId, value);
        }


        private string _defaultSystemPrompt;

        public string DefaultSystemPrompt
        {
            get => _defaultSystemPrompt;
            set => SetProperty(ref _defaultSystemPrompt, value);
        }


        private double _temperature;

        public double Temperature
        {
            get => _temperature;
            set => SetProperty(ref _temperature, Math.Round(value, 2));
        }


        private double _topP;

        public double TopP
        {
            get => _topP;
            set => SetProperty(ref _topP, Math.Round(value, 2));
        }


        private int _maxTokens;

        public int MaxTokens
        {
            get => _maxTokens;
            set => SetProperty(ref _maxTokens, value);
        }


        private double _frequencyPenalty;

        public double FrequencyPenalty
        {
            get => _frequencyPenalty;
            set => SetProperty(ref _frequencyPenalty, Math.Round(value, 2));
        }


        private double _presencePenalty;

        public double PresencePenalty
        {
            get => _presencePenalty;
            set => SetProperty(ref _presencePenalty, Math.Round(value, 2));
        }


        private bool _sendOnEnter;

        public bool SendOnEnter
        {
            get => _sendOnEnter;
            set => SetProperty(ref _sendOnEnter, value);
        }


        private bool _generateTitles;

        public bool GenerateTitles
        {
            get => _generateTitles;
            set => SetProperty(ref _generateTitles, value);
        }


        private string _aboutYou;

        public string AboutYou
        {
            get => _aboutYou;
            set => SetProperty(ref _aboutYou, value);
        }


        private string _responseStyle;

        public string ResponseStyle
        {
            get => _responseStyle;
            set => SetProperty(ref _responseStyle, value);
        }


        private string _globalHotkey;

        public string GlobalHotkey
        {
            get => _globalHotkey;
            set => SetProperty(ref _globalHotkey, value);
        }


        private bool _minimizeToTray;

        public bool MinimizeToTrayOnClose
        {
            get => _minimizeToTray;
            set => SetProperty(ref _minimizeToTray, value);
        }


        public ObservableCollection<PromptPreset> Presets { get; private set; }

        public IReadOnlyList<string> HotkeyOptions { get; } = new[]
        {
            "Ctrl+Shift+Space",
            "Ctrl+Alt+Space",
            "Alt+Space",
            "Ctrl+Alt+N",
            "Ctrl+Shift+K",
            LocalizationManager.Instance["HotkeyOff"]
        };


        private bool _isTesting;

        public bool IsTesting
        {
            get => _isTesting;
            set
            {
                if (SetProperty(ref _isTesting, value))
                    _testConnectionCommand?.RaiseCanExecuteChanged();
            }
        }


        private string _testStatus;

        public string TestStatus
        {
            get => _testStatus;
            set => SetProperty(ref _testStatus, value);
        }


        private bool _testSucceeded;

        public bool TestSucceeded
        {
            get => _testSucceeded;
            set => SetProperty(ref _testSucceeded, value);
        }

        #endregion


        #region Commands

        private DelegateCommand _testConnectionCommand;

        public ICommand TestConnectionCommand => _testConnectionCommand ?? (_testConnectionCommand = new DelegateCommand(OnTestConnection, CanTestConnection));


        private DelegateCommand _saveCommand;

        public ICommand SaveCommand => _saveCommand ?? (_saveCommand = new DelegateCommand(OnSave));


        private DelegateCommand _cancelCommand;

        public ICommand CancelCommand => _cancelCommand ?? (_cancelCommand = new DelegateCommand(OnCancel));


        private DelegateCommand _openApiKeyPageCommand;

        public ICommand OpenApiKeyPageCommand => _openApiKeyPageCommand ?? (_openApiKeyPageCommand = new DelegateCommand(OnOpenApiKeyPage));


        private DelegateCommand _addPresetCommand;

        public ICommand AddPresetCommand => _addPresetCommand ?? (_addPresetCommand = new DelegateCommand(() => Presets.Add(new PromptPreset(L["NewPreset"], ""))));


        private DelegateCommand<PromptPreset> _removePresetCommand;

        public ICommand RemovePresetCommand => _removePresetCommand ?? (_removePresetCommand = new DelegateCommand<PromptPreset>(p => { if (p != null) Presets.Remove(p); }));


        #endregion


        #region Bindable Properties - 언어

        /// <summary>언어 선택 항목.</summary>
        public sealed class LanguageOption
        {
            public LanguageOption(string culture, string display)
            {
                Culture = culture;
                Display = display;
            }

            public string Culture { get; }

            public string Display { get; }
        }

        private readonly string _originalCulture;

        public LanguageOption[] Languages { get; }

        private LanguageOption _selectedLanguage;

        public LanguageOption SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (SetProperty(ref _selectedLanguage, value) && value != null)
                {
                    // 라이브 미리보기: 고르는 즉시 UI 언어를 바꾼다. (취소하면 RevertLanguage 로 되돌린다)
                    Localization.LocalizationManager.Instance.Culture = value.Culture;
                }
            }
        }

        /// <summary>저장하지 않고 창을 닫았을 때 원래 언어로 되돌린다.</summary>
        public void RevertLanguage()
        {
            Localization.LocalizationManager.Instance.Culture = _originalCulture;
        }

        #endregion


        #region Bindable Properties - 업데이트

        private bool _autoCheckUpdates;

        /// <summary>시작할 때 새 버전을 확인할지 여부.</summary>
        public bool AutoCheckUpdates
        {
            get => _autoCheckUpdates;
            set => SetProperty(ref _autoCheckUpdates, value);
        }

        /// <summary>"현재 버전 1.3.0" 표시.</summary>
        public string CurrentVersionText => L.Tr("CurrentVersion", UpdateService.CurrentVersion.ToString(3));


        #endregion


        #region Helpers

        public AppSettings BuildSettings()
        {
            return new AppSettings
            {
                // null = 사용자가 키 칸을 한 번도 건드리지 않음(복호화 실패로 비어 보이는 경우 포함) → 저장소가 기존 키를 보존한다.
                // "" = 사용자가 명시적으로 비움 → 실제로 지운다.
                ApiKey = _apiKey == null ? null : _apiKey.Trim(),
                BaseUrl = string.IsNullOrWhiteSpace(_baseUrl) ? new AppSettings().BaseUrl : _baseUrl.Trim(),
                DefaultModelId = (_defaultModelId ?? string.Empty).Trim(),
                DefaultSystemPrompt = _defaultSystemPrompt ?? string.Empty,
                SendOnEnter = _sendOnEnter,
                GenerateTitles = _generateTitles,
                AboutYou = _aboutYou ?? string.Empty,
                ResponseStyle = _responseStyle ?? string.Empty,
                GlobalHotkey = string.IsNullOrWhiteSpace(_globalHotkey) ? L["HotkeyOff"] : _globalHotkey,
                MinimizeToTrayOnClose = _minimizeToTray,
                AutoCheckUpdates = _autoCheckUpdates,
                Language = _selectedLanguage?.Culture ?? _originalCulture,
                Presets = Presets
                    .Where(p => string.IsNullOrWhiteSpace(p.Name) == false)
                    .Select(p => new PromptPreset(p.Name.Trim(), p.Text ?? string.Empty))
                    .ToList(),
                DefaultParameters = new GenerationParameters
                {
                    Temperature = _temperature,
                    TopP = _topP,
                    MaxTokens = _maxTokens <= 0 ? 1024 : _maxTokens,
                    FrequencyPenalty = _frequencyPenalty,
                    PresencePenalty = _presencePenalty
                }
            };
        }

        private void ResetTestStatus()
        {
            TestStatus = null;
            TestSucceeded = false;
            _testConnectionCommand?.RaiseCanExecuteChanged();
        }

        private bool CanTestConnection()
        {
            return _isTesting == false && string.IsNullOrWhiteSpace(_apiKey) == false;
        }

        private async void OnTestConnection()
        {
            IsTesting = true;
            TestSucceeded = false;
            TestStatus = L["TestConnecting"];

            try
            {
                var probe = BuildSettings();
                using var client = new NvidiaClient(() => probe);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var models = await client.GetModelsAsync(cts.Token);

                TestSucceeded = true;
                TestStatus = L.Tr("TestSuccess", models.Count);
            }
            catch (Exception ex)
            {
                TestSucceeded = false;
                TestStatus = L.Tr("TestFailed", ex.Message);
            }
            finally
            {
                IsTesting = false;
            }
        }

        private void OnSave()
        {
            Saved = true;
            RequestClose();
        }

        private void OnCancel()
        {
            Saved = false;
            RequestClose();
        }

        private void OnOpenApiKeyPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://build.nvidia.com/")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // 브라우저 실행 실패는 무시.
            }
        }

        #endregion
    }
}

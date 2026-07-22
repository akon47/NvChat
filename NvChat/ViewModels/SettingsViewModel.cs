using NvChat.Commands;
using NvChat.Models;
using NvChat.Services;
using System;
using System.Diagnostics;
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

        #endregion


        #region Helpers

        public AppSettings BuildSettings()
        {
            return new AppSettings
            {
                ApiKey = (_apiKey ?? string.Empty).Trim(),
                BaseUrl = string.IsNullOrWhiteSpace(_baseUrl) ? new AppSettings().BaseUrl : _baseUrl.Trim(),
                DefaultModelId = (_defaultModelId ?? string.Empty).Trim(),
                DefaultSystemPrompt = _defaultSystemPrompt ?? string.Empty,
                SendOnEnter = _sendOnEnter,
                GenerateTitles = _generateTitles,
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
            TestStatus = "연결 확인 중…";

            try
            {
                var probe = BuildSettings();
                using var client = new NvidiaClient(() => probe);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var models = await client.GetModelsAsync(cts.Token);

                TestSucceeded = true;
                TestStatus = $"연결 성공 · 모델 {models.Count}개 확인됨";
            }
            catch (Exception ex)
            {
                TestSucceeded = false;
                TestStatus = "연결 실패: " + ex.Message;
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

using Microsoft.Win32;
using NvChat.Commands;
using NvChat.Models;
using NvChat.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 메인 창의 뷰모델. 대화/모델/채팅 스트리밍/설정을 총괄한다.
    /// </summary>
    public class MainViewModel : WindowViewModel
    {
        #region Constructors

        public MainViewModel()
        {
            _settingsStore = new SettingsStore();
            _conversationStore = new ConversationStore();
            _settings = _settingsStore.Load();
            _client = new NvidiaClient(() => _settings);

            Attachments = new ObservableCollection<AttachmentViewModel>();
            Attachments.CollectionChanged += (_, __) =>
            {
                RaisePropertyChanged(nameof(HasAttachments));
                _sendCommand?.RaiseCanExecuteChanged();
            };
        }

        #endregion


        #region Fields

        private readonly ISettingsStore _settingsStore;
        private readonly IConversationStore _conversationStore;
        private readonly INvidiaClient _client;

        private AppSettings _settings;
        private CancellationTokenSource _streamCts;
        private ConversationViewModel _streamingConversation;
        private bool _suppressModelSync;
        private bool _refreshingModels;
        private ICollectionView _conversationsView;
        private ICollectionView _modelsView;
        private string _modelSearchText;
        private DateTime _modelPickerClosedAt;

        private static readonly string[] _fallbackModelIds =
        {
            "meta/llama-3.3-70b-instruct",
            "meta/llama-3.1-405b-instruct",
            "meta/llama-3.1-70b-instruct",
            "meta/llama-3.1-8b-instruct",
            "nvidia/llama-3.1-nemotron-70b-instruct",
            "nvidia/nemotron-4-340b-instruct",
            "mistralai/mixtral-8x22b-instruct-v0.1",
            "mistralai/mistral-7b-instruct-v0.3",
            "google/gemma-2-27b-it",
            "google/gemma-2-9b-it",
            "qwen/qwen2.5-coder-32b-instruct",
            "microsoft/phi-3.5-moe-instruct",
            "deepseek-ai/deepseek-r1"
        };

        #endregion


        #region Collections

        public ObservableCollection<ConversationViewModel> Conversations { get; } = new ObservableCollection<ConversationViewModel>();

        public ObservableCollection<NvModel> Models { get; } = new ObservableCollection<NvModel>();

        /// <summary>전송 대기 중인 첨부 이미지.</summary>
        public ObservableCollection<AttachmentViewModel> Attachments { get; }

        public bool HasAttachments => Attachments.Count > 0;

        /// <summary>사이드바용 그룹/정렬/검색 뷰.</summary>
        public ICollectionView ConversationsView
        {
            get
            {
                if (_conversationsView == null)
                    _conversationsView = BuildConversationsView();

                return _conversationsView;
            }
        }

        /// <summary>모델 선택 팝업용 검색/그룹 뷰.</summary>
        public ICollectionView ModelsView
        {
            get
            {
                if (_modelsView == null)
                    _modelsView = BuildModelsView();

                return _modelsView;
            }
        }

        /// <summary>프롬프트 프리셋(설정에서 관리).</summary>
        public System.Collections.Generic.IReadOnlyList<PromptPreset> Presets => _settings.Presets ?? new System.Collections.Generic.List<PromptPreset>();

        public IReadOnlyList<string> ExamplePrompts { get; } = new[]
        {
            "간단한 파이썬 스크립트로 CSV 파일을 읽어 요약해줘",
            "이 문장을 자연스러운 영어로 번역해줘",
            "동시성과 병렬성의 차이를 예시로 설명해줘",
            "여행 짐 싸기 체크리스트를 만들어줘"
        };

        #endregion


        #region Bindable Properties

        private ConversationViewModel _selectedConversation;

        public ConversationViewModel SelectedConversation
        {
            get => _selectedConversation;
            set
            {
                if (SetProperty(ref _selectedConversation, value))
                {
                    SyncSelectedModelToConversation();
                    RaisePropertyChanged(nameof(IsSelectedStreaming));
                    RaiseCommandStates();
                }
            }
        }


        private NvModel _selectedModel;

        public NvModel SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (SetProperty(ref _selectedModel, value))
                {
                    if (_suppressModelSync == false && value != null)
                    {
                        if (_selectedConversation != null)
                            _selectedConversation.ModelId = value.Id;

                        IsModelPickerOpen = false;
                    }
                }
            }
        }


        public string ModelSearchText
        {
            get => _modelSearchText;
            set
            {
                if (SetProperty(ref _modelSearchText, value))
                    ModelsView.Refresh();
            }
        }


        private bool _isModelPickerOpen;

        public bool IsModelPickerOpen
        {
            get => _isModelPickerOpen;
            set
            {
                if (SetProperty(ref _isModelPickerOpen, value) == false)
                    return;

                if (value == false)
                {
                    _modelPickerClosedAt = DateTime.UtcNow;

                    if (string.IsNullOrEmpty(_modelSearchText) == false)
                        ModelSearchText = string.Empty;
                }
            }
        }


        private string _inputText;

        public string InputText
        {
            get => _inputText;
            set
            {
                if (SetProperty(ref _inputText, value))
                    _sendCommand?.RaiseCanExecuteChanged();
            }
        }


        private string _searchText;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ConversationsView.Refresh();
            }
        }


        private bool _isStreaming;

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (SetProperty(ref _isStreaming, value))
                {
                    RaisePropertyChanged(nameof(IsNotStreaming));
                    RaisePropertyChanged(nameof(IsSelectedStreaming));
                    RaiseCommandStates();
                }
            }
        }

        public bool IsNotStreaming => _isStreaming == false;

        /// <summary>현재 선택한 대화가 스트리밍 중인지. (입력창의 전송/중단 전환에 사용)</summary>
        public bool IsSelectedStreaming => _isStreaming && ReferenceEquals(_streamingConversation, _selectedConversation);


        private bool _isModelLoading;

        public bool IsModelLoading
        {
            get => _isModelLoading;
            set
            {
                if (SetProperty(ref _isModelLoading, value))
                    _refreshModelsCommand?.RaiseCanExecuteChanged();
            }
        }


        private string _statusMessage;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (SetProperty(ref _statusMessage, value))
                    RaisePropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => string.IsNullOrWhiteSpace(_statusMessage) == false;


        private bool _isAdvancedOpen;

        public bool IsAdvancedOpen
        {
            get => _isAdvancedOpen;
            set => SetProperty(ref _isAdvancedOpen, value);
        }


        private bool _isSidebarCollapsed;

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set
            {
                if (SetProperty(ref _isSidebarCollapsed, value))
                    RaisePropertyChanged(nameof(SidebarWidth));
            }
        }

        public GridLength SidebarWidth => _isSidebarCollapsed ? new GridLength(0) : new GridLength(280);

        public bool HasApiKey => string.IsNullOrWhiteSpace(_settings?.ApiKey) == false;

        public bool SendOnEnter => _settings?.SendOnEnter ?? true;

        public bool MinimizeToTrayOnClose => _settings?.MinimizeToTrayOnClose ?? true;

        #endregion


        #region Commands

        private AsyncDelegateCommand _sendCommand;
        public ICommand SendCommand => _sendCommand ?? (_sendCommand = new AsyncDelegateCommand(SendAsync, CanSend));

        private DelegateCommand _stopCommand;
        public ICommand StopCommand => _stopCommand ?? (_stopCommand = new DelegateCommand(OnStop, () => IsSelectedStreaming));

        private DelegateCommand _newConversationCommand;
        public ICommand NewConversationCommand => _newConversationCommand ?? (_newConversationCommand = new DelegateCommand(OnNewConversation));

        private DelegateCommand<ConversationViewModel> _deleteConversationCommand;
        public ICommand DeleteConversationCommand => _deleteConversationCommand ?? (_deleteConversationCommand = new DelegateCommand<ConversationViewModel>(OnDeleteConversation, c => c != null));

        private DelegateCommand<ConversationViewModel> _pinConversationCommand;
        public ICommand PinConversationCommand => _pinConversationCommand ?? (_pinConversationCommand = new DelegateCommand<ConversationViewModel>(OnPinConversation, c => c != null));

        private DelegateCommand<ConversationViewModel> _beginRenameCommand;
        public ICommand BeginRenameCommand => _beginRenameCommand ?? (_beginRenameCommand = new DelegateCommand<ConversationViewModel>(OnBeginRename, c => c != null));

        private DelegateCommand<ConversationViewModel> _commitRenameCommand;
        public ICommand CommitRenameCommand => _commitRenameCommand ?? (_commitRenameCommand = new DelegateCommand<ConversationViewModel>(OnCommitRename, c => c != null));

        private DelegateCommand<ConversationViewModel> _cancelRenameCommand;
        public ICommand CancelRenameCommand => _cancelRenameCommand ?? (_cancelRenameCommand = new DelegateCommand<ConversationViewModel>(c => { if (c != null) c.IsRenaming = false; }));

        private DelegateCommand _clearMessagesCommand;
        public ICommand ClearMessagesCommand => _clearMessagesCommand ?? (_clearMessagesCommand = new DelegateCommand(OnClearMessages, CanClearMessages));

        private DelegateCommand _refreshModelsCommand;
        public ICommand RefreshModelsCommand => _refreshModelsCommand ?? (_refreshModelsCommand = new DelegateCommand(OnRefreshModels, () => _isModelLoading == false));

        private DelegateCommand _openSettingsCommand;
        public ICommand OpenSettingsCommand => _openSettingsCommand ?? (_openSettingsCommand = new DelegateCommand(OnOpenSettings));

        private DelegateCommand _toggleAdvancedCommand;
        public ICommand ToggleAdvancedCommand => _toggleAdvancedCommand ?? (_toggleAdvancedCommand = new DelegateCommand(() => IsAdvancedOpen = !IsAdvancedOpen));

        private DelegateCommand _toggleSidebarCommand;
        public ICommand ToggleSidebarCommand => _toggleSidebarCommand ?? (_toggleSidebarCommand = new DelegateCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed));

        private DelegateCommand _toggleModelPickerCommand;
        public ICommand ToggleModelPickerCommand => _toggleModelPickerCommand ?? (_toggleModelPickerCommand = new DelegateCommand(OnToggleModelPicker));

        private DelegateCommand<PromptPreset> _insertPresetCommand;
        public ICommand InsertPresetCommand => _insertPresetCommand ?? (_insertPresetCommand = new DelegateCommand<PromptPreset>(OnInsertPreset, p => p != null));

        private DelegateCommand<ChatMessageViewModel> _regenerateMessageCommand;
        public ICommand RegenerateMessageCommand => _regenerateMessageCommand ?? (_regenerateMessageCommand = new DelegateCommand<ChatMessageViewModel>(OnRegenerateMessage, m => m != null && _isStreaming == false));

        private DelegateCommand _regenerateLastCommand;
        public ICommand RegenerateLastCommand => _regenerateLastCommand ?? (_regenerateLastCommand = new DelegateCommand(OnRegenerateLast, CanRegenerateLast));

        private DelegateCommand<ChatMessageViewModel> _deleteMessageCommand;
        public ICommand DeleteMessageCommand => _deleteMessageCommand ?? (_deleteMessageCommand = new DelegateCommand<ChatMessageViewModel>(OnDeleteMessage, m => m != null && _isStreaming == false));

        private DelegateCommand<ChatMessageViewModel> _submitEditCommand;
        public ICommand SubmitEditCommand => _submitEditCommand ?? (_submitEditCommand = new DelegateCommand<ChatMessageViewModel>(OnSubmitEdit, m => m != null && _isStreaming == false));

        private DelegateCommand<string> _useExamplePromptCommand;
        public ICommand UseExamplePromptCommand => _useExamplePromptCommand ?? (_useExamplePromptCommand = new DelegateCommand<string>(OnUseExamplePrompt));

        private DelegateCommand _copyConversationCommand;
        public ICommand CopyConversationCommand => _copyConversationCommand ?? (_copyConversationCommand = new DelegateCommand(OnCopyConversation, () => _selectedConversation != null && _selectedConversation.Messages.Count > 0));

        private DelegateCommand _exportConversationCommand;
        public ICommand ExportConversationCommand => _exportConversationCommand ?? (_exportConversationCommand = new DelegateCommand(OnExportConversation, () => _selectedConversation != null && _selectedConversation.Messages.Count > 0));

        private DelegateCommand _attachImageCommand;
        public ICommand AttachImageCommand => _attachImageCommand ?? (_attachImageCommand = new DelegateCommand(OnAttachImage, () => _isStreaming == false));

        private DelegateCommand<AttachmentViewModel> _removeAttachmentCommand;
        public ICommand RemoveAttachmentCommand => _removeAttachmentCommand ?? (_removeAttachmentCommand = new DelegateCommand<AttachmentViewModel>(a => { if (a != null) Attachments.Remove(a); }));

        #endregion


        #region Events / Callbacks

        /// <summary>설정 창을 열어달라는 요청. View 가 구독한다.</summary>
        public event EventHandler SettingsRequested;

        /// <summary>설정이 적용되었을 때. App 이 전역 단축키 재등록 등에 사용.</summary>
        public event EventHandler SettingsChanged;

        /// <summary>확인 대화상자. View 가 설정한다. (제목, 메시지) → 확인 여부.</summary>
        public Func<string, string, bool> ConfirmCallback { get; set; }

        #endregion


        #region Helpers - Lifecycle

        public async Task LoadAsync()
        {
            foreach (var data in _conversationStore.LoadAll())
                Conversations.Add(ConversationViewModel.FromData(data));

            if (string.IsNullOrEmpty(_settingsStore.LoadError) == false)
                StatusMessage = _settingsStore.LoadError;
            else if (string.IsNullOrEmpty(_conversationStore.LoadError) == false)
                StatusMessage = _conversationStore.LoadError;

            if (Conversations.Count == 0)
                CreateConversation(select: true);
            else
                SelectedConversation = Conversations.OrderByDescending(c => c.UpdatedAt).First();

            await RefreshModelsAsync();

            if (HasApiKey == false)
            {
                StatusMessage = "시작하려면 build.nvidia.com API 키가 필요합니다. 설정에서 입력하세요.";
                OnOpenSettings();
            }
        }

        /// <summary>대화를 저장한다. (트레이 최소화 등에서도 호출되므로 스트림은 건드리지 않는다.)</summary>
        public void SaveState()
        {
            SaveConversations();
        }

        /// <summary>진행 중인 스트리밍을 취소한다. 실제 앱 종료 경로에서만 호출.</summary>
        public void CancelStreaming()
        {
            try
            {
                _streamCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public AppSettings GetCurrentSettings()
        {
            return _settings;
        }

        public void ApplySettings(AppSettings settings)
        {
            if (settings == null)
                return;

            // 창 상태는 기존 값을 유지(설정 창은 이를 편집하지 않음).
            settings.WindowLeft = _settings.WindowLeft;
            settings.WindowTop = _settings.WindowTop;
            settings.WindowWidth = _settings.WindowWidth;
            settings.WindowHeight = _settings.WindowHeight;
            settings.WindowMaximized = _settings.WindowMaximized;

            _settings = settings;
            _settingsStore.Save(_settings);

            RaisePropertyChanged(nameof(HasApiKey));
            RaisePropertyChanged(nameof(SendOnEnter));
            RaisePropertyChanged(nameof(MinimizeToTrayOnClose));
            RaisePropertyChanged(nameof(Presets));
            RaiseCommandStates();

            if (HasStatusMessage && HasApiKey)
                StatusMessage = null;

            SettingsChanged?.Invoke(this, EventArgs.Empty);

            _ = RefreshModelsAsync();
        }

        public void SaveWindowState(double left, double top, double width, double height, bool maximized)
        {
            _settings.WindowLeft = left;
            _settings.WindowTop = top;
            _settings.WindowWidth = width;
            _settings.WindowHeight = height;
            _settings.WindowMaximized = maximized;
            _settingsStore.Save(_settings);
        }

        #endregion


        #region Helpers - Conversations

        private ICollectionView BuildConversationsView()
        {
            var view = CollectionViewSource.GetDefaultView(Conversations);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConversationViewModel.GroupName)));
            view.SortDescriptions.Add(new SortDescription(nameof(ConversationViewModel.GroupOrder), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(ConversationViewModel.UpdatedAt), ListSortDirection.Descending));
            view.Filter = FilterConversation;

            if (view is ICollectionViewLiveShaping live)
            {
                live.IsLiveGrouping = true;
                live.IsLiveSorting = true;
                live.IsLiveFiltering = true;
                foreach (var p in new[] { nameof(ConversationViewModel.GroupName), nameof(ConversationViewModel.GroupOrder), nameof(ConversationViewModel.UpdatedAt), nameof(ConversationViewModel.Pinned), nameof(ConversationViewModel.Title) })
                {
                    live.LiveGroupingProperties.Add(p);
                    live.LiveSortingProperties.Add(p);
                    live.LiveFilteringProperties.Add(p);
                }
            }

            return view;
        }

        private bool FilterConversation(object item)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
                return true;

            if (item is not ConversationViewModel c)
                return false;

            var q = _searchText.Trim();
            if ((c.Title ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return c.Messages.Any(m => (m.Content ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void OnNewConversation()
        {
            CreateConversation(select: true);
            SaveConversations();
        }

        private ConversationViewModel CreateConversation(bool select)
        {
            var conversation = new ConversationViewModel
            {
                ModelId = _selectedModel?.Id ?? _settings.DefaultModelId,
                SystemPrompt = _settings.DefaultSystemPrompt ?? string.Empty,
                Parameters = (_settings.DefaultParameters ?? new GenerationParameters()).Clone()
            };

            Conversations.Add(conversation);

            if (select)
                SelectedConversation = conversation;

            return conversation;
        }

        private void OnDeleteConversation(ConversationViewModel conversation)
        {
            if (conversation == null)
                return;

            if (ConfirmCallback != null && ConfirmCallback("대화 삭제", $"'{conversation.Title}' 대화를 삭제할까요?") == false)
                return;

            // 스트리밍 중인 대화를 지우면 스트림도 함께 취소해야 IsStreaming 이 풀린다.
            if (ReferenceEquals(conversation, _streamingConversation))
                CancelStreaming();

            var wasSelected = ReferenceEquals(conversation, _selectedConversation);
            Conversations.Remove(conversation);

            if (Conversations.Count == 0)
                CreateConversation(select: true);
            else if (wasSelected)
                SelectedConversation = Conversations.OrderByDescending(c => c.UpdatedAt).First();

            SaveConversations();
        }

        private void OnPinConversation(ConversationViewModel conversation)
        {
            if (conversation == null)
                return;

            conversation.Pinned = !conversation.Pinned;
            SaveConversations();
        }

        private void OnBeginRename(ConversationViewModel conversation)
        {
            if (conversation == null)
                return;

            conversation.RenameText = conversation.Title;
            conversation.IsRenaming = true;
        }

        private void OnCommitRename(ConversationViewModel conversation)
        {
            if (conversation == null || conversation.IsRenaming == false)
                return;

            var text = (conversation.RenameText ?? string.Empty).Trim();
            if (text.Length > 0)
            {
                conversation.Title = text;
                conversation.TitleLocked = true; // 사용자가 지정한 제목은 자동 생성이 덮지 않는다.
            }

            conversation.IsRenaming = false;
            SaveConversations();
        }

        private bool CanClearMessages()
        {
            return _selectedConversation != null && _selectedConversation.Messages.Count > 0 && _isStreaming == false;
        }

        private void OnClearMessages()
        {
            if (_selectedConversation == null)
                return;

            if (ConfirmCallback != null && ConfirmCallback("메시지 비우기", "이 대화의 모든 메시지를 지울까요?") == false)
                return;

            _selectedConversation.Messages.Clear();
            _selectedConversation.UpdatedAt = DateTime.Now;
            RaiseCommandStates();
            SaveConversations();
        }

        private void SaveConversations()
        {
            try
            {
                _conversationStore.SaveAll(Conversations.Select(c => c.ToData()));
            }
            catch
            {
                // 저장 실패가 흐름을 막지 않도록 무시.
            }
        }

        #endregion


        #region Helpers - Models

        private void OnRefreshModels()
        {
            _ = RefreshModelsAsync();
        }

        private async Task RefreshModelsAsync()
        {
            if (_refreshingModels)
                return;

            if (HasApiKey == false)
            {
                ReplaceModels(BuildFallbackModels());
                SyncSelectedModelToConversation();
                return;
            }

            _refreshingModels = true;
            IsModelLoading = true;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var models = await _client.GetModelsAsync(cts.Token);

                ReplaceModels(models.Count > 0 ? models : BuildFallbackModels());
            }
            catch (Exception ex)
            {
                ReplaceModels(BuildFallbackModels());
                StatusMessage = "모델 목록을 불러오지 못해 기본 목록을 사용합니다. " + ex.Message;
            }
            finally
            {
                IsModelLoading = false;
                _refreshingModels = false;
                SyncSelectedModelToConversation();
            }
        }

        private static IReadOnlyList<NvModel> BuildFallbackModels()
        {
            return _fallbackModelIds.Select(NvModel.FromId).ToList();
        }

        private void ReplaceModels(IReadOnlyList<NvModel> models)
        {
            Models.Clear();
            foreach (var model in models)
                Models.Add(model);
        }

        private void SyncSelectedModelToConversation()
        {
            var desiredId = _selectedConversation?.ModelId;
            if (string.IsNullOrWhiteSpace(desiredId))
                desiredId = _settings.DefaultModelId;

            NvModel target = null;

            if (string.IsNullOrWhiteSpace(desiredId) == false)
            {
                target = Models.FirstOrDefault(m => string.Equals(m.Id, desiredId, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    target = NvModel.FromId(desiredId);
                    Models.Insert(0, target);
                }
            }
            else if (Models.Count > 0)
            {
                target = Models[0];
            }

            _suppressModelSync = true;
            SelectedModel = target;
            _suppressModelSync = false;

            if (_selectedConversation != null && string.IsNullOrWhiteSpace(_selectedConversation.ModelId) && target != null)
                _selectedConversation.ModelId = target.Id;
        }

        #endregion


        #region Helpers - Chat

        private bool CanSend()
        {
            return _isStreaming == false
                && _selectedConversation != null
                && (string.IsNullOrWhiteSpace(_inputText) == false || Attachments.Count > 0);
        }

        private async Task SendAsync()
        {
            var text = (_inputText ?? string.Empty).Trim();
            var hasImages = Attachments.Count > 0;

            if ((text.Length == 0 && hasImages == false) || _selectedConversation == null || _isStreaming)
                return;

            if (HasApiKey == false)
            {
                StatusMessage = "API 키를 먼저 설정하세요.";
                OnOpenSettings();
                return;
            }

            var conversation = _selectedConversation;

            var userMessage = new ChatMessageViewModel(ChatRole.User, text);
            if (hasImages)
                userMessage.SetImages(Attachments.Select(a => a.DataUri).ToList());

            conversation.Messages.Add(userMessage);
            InputText = string.Empty;
            Attachments.Clear();

            if (conversation.Messages.Count(m => m.IsUser) == 1)
                conversation.Title = text.Length > 0 ? MakeTitle(text) : "이미지 대화";

            await GenerateResponseAsync(conversation);
        }

        private void OnAttachImage()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "이미지 (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|모든 파일 (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            foreach (var file in dialog.FileNames)
            {
                try
                {
                    var dataUri = ImageUtil.FileToDataUri(file);
                    Attachments.Add(new AttachmentViewModel(dataUri, ImageUtil.FromDataUri(dataUri)));
                }
                catch (Exception ex)
                {
                    StatusMessage = "이미지를 불러오지 못했습니다: " + ex.Message;
                }
            }
        }

        private void OnUseExamplePrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            InputText = prompt;
            if (SendCommand.CanExecute(null))
                SendCommand.Execute(null);
        }

        private void OnRegenerateMessage(ChatMessageViewModel message)
        {
            if (message == null || _selectedConversation == null || _isStreaming)
                return;

            var conversation = _selectedConversation;
            var index = conversation.Messages.IndexOf(message);
            if (index < 0)
                return;

            // 이 어시스턴트 메시지부터 끝까지 제거 후 재생성.
            for (var i = conversation.Messages.Count - 1; i >= index; i--)
                conversation.Messages.RemoveAt(i);

            _ = GenerateResponseAsync(conversation);
        }

        private bool CanRegenerateLast()
        {
            return _isStreaming == false
                && _selectedConversation != null
                && _selectedConversation.Messages.Count > 0
                && _selectedConversation.Messages[_selectedConversation.Messages.Count - 1].IsAssistant;
        }

        private void OnRegenerateLast()
        {
            if (_selectedConversation == null || _selectedConversation.Messages.Count == 0)
                return;

            var last = _selectedConversation.Messages[_selectedConversation.Messages.Count - 1];
            if (last.IsAssistant)
                OnRegenerateMessage(last);
        }

        private void OnDeleteMessage(ChatMessageViewModel message)
        {
            if (message == null || _selectedConversation == null || _isStreaming)
                return;

            _selectedConversation.Messages.Remove(message);
            _selectedConversation.UpdatedAt = DateTime.Now;
            RaiseCommandStates();
            SaveConversations();
        }

        private void OnSubmitEdit(ChatMessageViewModel message)
        {
            if (message == null || _selectedConversation == null || _isStreaming)
                return;

            var conversation = _selectedConversation;
            var index = conversation.Messages.IndexOf(message);
            if (index < 0)
                return;

            var text = (message.EditText ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                message.IsEditing = false;
                return;
            }

            message.Content = text;
            message.IsEditing = false;

            // 편집한 사용자 메시지 다음의 모든 메시지를 제거하고 재생성.
            for (var i = conversation.Messages.Count - 1; i > index; i--)
                conversation.Messages.RemoveAt(i);

            _ = GenerateResponseAsync(conversation);
        }

        /// <summary>
        /// 대화의 현재 메시지들(마지막이 사용자 메시지)에 대해 어시스턴트 응답을 스트리밍한다.
        /// </summary>
        private async Task GenerateResponseAsync(ConversationViewModel conversation)
        {
            if (conversation == null || _isStreaming)
                return;

            if (HasApiKey == false)
            {
                StatusMessage = "API 키를 먼저 설정하세요.";
                OnOpenSettings();
                return;
            }

            var modelId = _selectedModel?.Id ?? conversation.ModelId ?? _settings.DefaultModelId;
            conversation.ModelId = modelId;

            var apiMessages = BuildApiMessages(conversation);

            var assistant = new ChatMessageViewModel(ChatRole.Assistant, string.Empty) { IsStreaming = true };
            conversation.Messages.Add(assistant);

            _streamCts = new CancellationTokenSource();
            _streamingConversation = conversation;
            IsStreaming = true;
            StatusMessage = null;

            var aborted = false;

            try
            {
                var received = false;

                await foreach (var delta in _client.StreamChatAsync(modelId, apiMessages, conversation.Parameters, _streamCts.Token))
                {
                    received = true;

                    if (string.IsNullOrEmpty(delta.Reasoning) == false)
                        assistant.AppendReasoning(delta.Reasoning);

                    if (string.IsNullOrEmpty(delta.Content) == false)
                        assistant.AppendContent(delta.Content);
                }

                ExtractInlineThinking(assistant);

                if (received == false && string.IsNullOrEmpty(assistant.Content))
                    assistant.Content = "(빈 응답을 받았습니다)";
            }
            catch (OperationCanceledException)
            {
                ExtractInlineThinking(assistant);
                aborted = true;
            }
            catch (Exception ex)
            {
                assistant.HasError = true;
                assistant.Content = "⚠ 오류: " + ex.Message;
                StatusMessage = ex.Message;
            }
            finally
            {
                if (aborted)
                {
                    // 빈 채로 중단되면 자리표시자를 제거하고, 부분 응답이 있으면 유지하되 '중단됨'만 표시.
                    if (string.IsNullOrEmpty(assistant.Content))
                        conversation.Messages.Remove(assistant);
                    else
                        assistant.Aborted = true;
                }

                assistant.IsStreaming = false;
                assistant.Timestamp = DateTime.Now;

                _streamingConversation = null;
                IsStreaming = false;
                _streamCts?.Dispose();
                _streamCts = null;

                conversation.UpdatedAt = DateTime.Now;
                RaiseCommandStates();
                SaveConversations();
            }

            // 첫 응답이 정상 완료되면 모델로 제목 자동 생성(비차단).
            if (assistant.HasError == false)
                _ = MaybeGenerateTitleAsync(conversation, modelId);
        }

        private void OnStop()
        {
            try
            {
                _streamCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task MaybeGenerateTitleAsync(ConversationViewModel conversation, string modelId)
        {
            if (_settings.GenerateTitles == false)
                return;

            // 사용자가 직접 정했거나 이미 자동 생성이 끝난 제목은 다시 덮어쓰지 않는다(재생성 포함).
            if (conversation.TitleLocked)
                return;

            if (conversation.Messages.Count(m => m.IsAssistant) != 1)
                return;

            var firstUser = conversation.Messages.FirstOrDefault(m => m.IsUser)?.Content;
            if (string.IsNullOrWhiteSpace(firstUser))
                return;

            try
            {
                var prompt = new List<ChatMessage>
                {
                    new ChatMessage { Role = ChatRole.System, Content = "다음 사용자 메시지에 어울리는 아주 짧은 대화 제목을 사용자와 같은 언어로 3~6단어로만 생성하세요. 따옴표/마침표/접두어 없이 제목 텍스트만 출력." },
                    new ChatMessage { Role = ChatRole.User, Content = firstUser.Length > 500 ? firstUser.Substring(0, 500) : firstUser }
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var raw = await _client.CompleteChatAsync(modelId, prompt, new GenerationParameters { MaxTokens = 24, Temperature = 0.3, TopP = 1.0 }, cts.Token);

                var title = CleanTitle(raw);
                if (string.IsNullOrWhiteSpace(title) == false)
                {
                    conversation.Title = title;
                    conversation.TitleLocked = true;
                    SaveConversations();
                }
            }
            catch
            {
                // 제목 생성 실패는 무시(이미 요약 제목이 있음).
            }
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var title = raw.Trim();

            // <think> 흔적 제거
            var close = title.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (close >= 0)
                title = title.Substring(close + "</think>".Length).Trim();

            title = title.Replace("\r", " ").Replace("\n", " ").Trim();
            title = title.Trim('"', '\'', '「', '」', '“', '”', '.', ' ');

            if (title.Length > 40)
                title = title.Substring(0, 40).TrimEnd() + "…";

            return title.Length == 0 ? null : title;
        }

        private static void ExtractInlineThinking(ChatMessageViewModel message)
        {
            var content = message.Content;
            if (string.IsNullOrEmpty(content))
                return;

            const string open = "<think>";
            const string close = "</think>";

            var start = content.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            var end = content.IndexOf(close, StringComparison.OrdinalIgnoreCase);

            if (start >= 0 && end > start)
            {
                var think = content.Substring(start + open.Length, end - (start + open.Length)).Trim();
                var rest = (content.Substring(0, start) + content.Substring(end + close.Length)).Trim();

                if (string.IsNullOrEmpty(think) == false)
                    message.Reasoning = string.IsNullOrEmpty(message.Reasoning) ? think : (message.Reasoning + "\n" + think);

                message.Content = rest;
            }
        }

        private List<ChatMessage> BuildApiMessages(ConversationViewModel conversation)
        {
            var messages = new List<ChatMessage>();

            var system = ComposeSystemPrompt(conversation);
            if (string.IsNullOrWhiteSpace(system) == false)
                messages.Add(new ChatMessage { Role = ChatRole.System, Content = system });

            foreach (var message in conversation.Messages)
            {
                if (message.IsStreaming)
                    continue;

                if (message.HasError)
                    continue;

                if (string.IsNullOrEmpty(message.Content) && message.HasImages == false)
                    continue;

                messages.Add(new ChatMessage
                {
                    Role = message.Role,
                    Content = message.Content,
                    Images = message.HasImages ? message.Images.ToList() : null
                });
            }

            return messages;
        }

        /// <summary>커스텀 지침(나에 대해 + 응답 방식) + 대화별 시스템 프롬프트를 합친다.</summary>
        private string ComposeSystemPrompt(ConversationViewModel conversation)
        {
            var parts = new List<string>();

            var about = _settings.AboutYou?.Trim();
            if (string.IsNullOrEmpty(about) == false)
                parts.Add("[사용자에 대한 정보]\n" + about);

            var style = _settings.ResponseStyle?.Trim();
            if (string.IsNullOrEmpty(style) == false)
                parts.Add("[응답 방식]\n" + style);

            var sys = conversation.SystemPrompt?.Trim();
            if (string.IsNullOrEmpty(sys) == false)
                parts.Add(sys);

            return string.Join("\n\n", parts);
        }

        private void OnInsertPreset(PromptPreset preset)
        {
            if (preset == null)
                return;

            InputText = string.IsNullOrEmpty(_inputText) ? preset.Text : (_inputText + "\n" + preset.Text);
        }

        private ICollectionView BuildModelsView()
        {
            var view = CollectionViewSource.GetDefaultView(Models);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NvModel.Publisher)));
            view.SortDescriptions.Add(new SortDescription(nameof(NvModel.Publisher), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(NvModel.Name), ListSortDirection.Ascending));
            view.Filter = FilterModel;
            return view;
        }

        private bool FilterModel(object item)
        {
            if (string.IsNullOrWhiteSpace(_modelSearchText))
                return true;

            if (item is not NvModel model)
                return false;

            var q = _modelSearchText.Trim();
            return (model.Id ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnToggleModelPicker()
        {
            // Popup(StaysOpen=False)이 바깥 클릭으로 방금 닫힌 직후의 버튼 클릭은 무시한다.
            // (아니면 닫혔다가 곧바로 다시 열려 버튼으로는 닫을 수 없다.)
            if (_isModelPickerOpen == false && (DateTime.UtcNow - _modelPickerClosedAt).TotalMilliseconds < 300)
                return;

            IsModelPickerOpen = !_isModelPickerOpen;
        }

        private static string MakeTitle(string text)
        {
            var firstLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (firstLine.Length == 0)
                return "새 대화";

            const int maxLength = 30;
            if (firstLine.Length > maxLength)
                firstLine = firstLine.Substring(0, maxLength).TrimEnd() + "…";

            return firstLine;
        }

        #endregion


        #region Helpers - Export

        private void OnCopyConversation()
        {
            var text = BuildConversationMarkdown(_selectedConversation);
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                Clipboard.SetText(text);
                StatusMessage = "대화를 클립보드에 복사했습니다.";
            }
            catch
            {
            }
        }

        private void OnExportConversation()
        {
            if (_selectedConversation == null)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
                FileName = SanitizeFileName(_selectedConversation.Title) + ".md"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, BuildConversationMarkdown(_selectedConversation), Encoding.UTF8);
                StatusMessage = "대화를 저장했습니다: " + dialog.FileName;
            }
            catch (Exception ex)
            {
                StatusMessage = "내보내기 실패: " + ex.Message;
            }
        }

        private static string BuildConversationMarkdown(ConversationViewModel conversation)
        {
            if (conversation == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("# " + conversation.Title).AppendLine();

            foreach (var m in conversation.Messages)
            {
                var who = m.IsUser ? "🧑 사용자" : m.IsAssistant ? "🤖 어시스턴트" : "⚙ 시스템";
                sb.AppendLine("### " + who).AppendLine();
                sb.AppendLine(m.Content ?? string.Empty).AppendLine();
            }

            return sb.ToString();
        }

        private static string SanitizeFileName(string name)
        {
            name = name ?? "conversation";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "conversation" : name;
        }

        #endregion


        #region Helpers - Settings / commands

        private void OnOpenSettings()
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseCommandStates()
        {
            _sendCommand?.RaiseCanExecuteChanged();
            _stopCommand?.RaiseCanExecuteChanged();
            _clearMessagesCommand?.RaiseCanExecuteChanged();
            _deleteConversationCommand?.RaiseCanExecuteChanged();
            _refreshModelsCommand?.RaiseCanExecuteChanged();
            _regenerateMessageCommand?.RaiseCanExecuteChanged();
            _regenerateLastCommand?.RaiseCanExecuteChanged();
            _deleteMessageCommand?.RaiseCanExecuteChanged();
            _submitEditCommand?.RaiseCanExecuteChanged();
            _copyConversationCommand?.RaiseCanExecuteChanged();
            _exportConversationCommand?.RaiseCanExecuteChanged();
            _attachImageCommand?.RaiseCanExecuteChanged();
        }

        #endregion
    }
}

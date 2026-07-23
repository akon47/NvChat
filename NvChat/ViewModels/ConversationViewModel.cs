using NvChat.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 하나의 대화 세션.
    /// </summary>
    public class ConversationViewModel : ViewModel
    {
        #region Constructors

        public ConversationViewModel()
        {
            Id = Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.Now;
            _updatedAt = DateTime.Now;

            Messages = new ObservableCollection<ChatMessageViewModel>();
            Messages.CollectionChanged += Messages_CollectionChanged;

            Usage = new ObservableCollection<ModelUsage>();
        }

        #endregion


        #region Properties

        public string Id { get; set; }

        public DateTime CreatedAt { get; set; }

        public ObservableCollection<ChatMessageViewModel> Messages { get; }

        public GenerationParameters Parameters { get; set; } = new GenerationParameters();

        /// <summary>제목이 확정(사용자 지정 또는 자동 생성 완료)되어 더 이상 자동 생성하지 않음.</summary>
        public bool TitleLocked { get; set; }

        public bool IsEmpty => Messages.Count == 0;

        /// <summary>이 대화에서 모델별로 쓴 요청/토큰. 대화 중 모델을 바꾸면 항목이 늘어난다.</summary>
        public ObservableCollection<ModelUsage> Usage { get; }

        public bool HasUsage => Usage.Count > 0;

        /// <summary>"합계 12.4k 토큰 · 8회" 형태의 요약.</summary>
        public string UsageSummary
        {
            get
            {
                if (Usage.Count == 0)
                    return null;

                var tokens = Usage.Sum(u => u.TotalTokens);
                var requests = Usage.Sum(u => u.Requests);

                return Localization.LocalizationManager.Instance.Tr(
                    "UsageSummary", ModelUsage.FormatTokens(tokens), requests.ToString("N0"));
            }
        }

        /// <summary>
        /// 이 대화의 사용량에 한 건을 더한다. 모델별로 나누어 쌓는다.
        /// </summary>
        public void AddUsage(string modelId, int promptTokens, int completionTokens, bool hasTokens)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                modelId = Localization.LocalizationManager.Instance["Unknown"];

            var entry = Usage.FirstOrDefault(u => string.Equals(u.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                entry = new ModelUsage { ModelId = modelId };
                Usage.Add(entry);
            }

            entry.Requests++;

            if (hasTokens)
            {
                entry.PromptTokens += promptTokens;
                entry.CompletionTokens += completionTokens;
            }

            // ModelUsage 는 POCO 라 개별 알림이 없으므로 목록을 다시 그리게 한다.
            var index = Usage.IndexOf(entry);
            Usage[index] = entry;

            RaisePropertyChanged(nameof(HasUsage));
            RaisePropertyChanged(nameof(UsageSummary));
        }

        /// <summary>사이드바에 표시할 부제(모델명 · 메시지 수).</summary>
        public string Subtitle
        {
            get
            {
                var model = NvModel.FromId(_modelId ?? string.Empty).Name;
                if (string.IsNullOrEmpty(model))
                    model = Localization.LocalizationManager.Instance["ModelUnset"];

                return Localization.LocalizationManager.Instance.Tr("Subtitle", model, Messages.Count.ToString());
            }
        }

        /// <summary>사이드바 그룹 이름(고정/오늘/어제/…).</summary>
        public string GroupName
        {
            get
            {
                var L = Localization.LocalizationManager.Instance;

                if (_pinned)
                    return L["GroupPinned"];

                var today = DateTime.Now.Date;
                var day = _updatedAt.Date;

                if (day >= today) return L["GroupToday"];
                if (day == today.AddDays(-1)) return L["GroupYesterday"];
                if (day > today.AddDays(-7)) return L["GroupLast7"];
                if (day > today.AddDays(-30)) return L["GroupLast30"];
                return L["GroupOlder"];
            }
        }

        /// <summary>그룹 정렬 순서.</summary>
        public int GroupOrder
        {
            get
            {
                if (_pinned)
                    return 0;

                var today = DateTime.Now.Date;
                var day = _updatedAt.Date;

                if (day >= today) return 1;
                if (day == today.AddDays(-1)) return 2;
                if (day > today.AddDays(-7)) return 3;
                if (day > today.AddDays(-30)) return 4;
                return 5;
            }
        }

        #endregion


        #region Bindable Properties

        private string _title = Localization.LocalizationManager.Instance["NewChat"];

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }


        private string _modelId;

        public string ModelId
        {
            get => _modelId;
            set
            {
                if (SetProperty(ref _modelId, value))
                    RaisePropertyChanged(nameof(Subtitle));
            }
        }


        private string _systemPrompt = string.Empty;

        public string SystemPrompt
        {
            get => _systemPrompt;
            set => SetProperty(ref _systemPrompt, value);
        }


        private bool _pinned;

        public bool Pinned
        {
            get => _pinned;
            set
            {
                if (SetProperty(ref _pinned, value))
                {
                    RaisePropertyChanged(nameof(GroupName));
                    RaisePropertyChanged(nameof(GroupOrder));
                }
            }
        }


        private bool _isStreaming;

        /// <summary>이 대화에서 응답을 생성 중인지(사이드바 로딩 표시용).</summary>
        public bool IsStreaming
        {
            get => _isStreaming;
            set => SetProperty(ref _isStreaming, value);
        }


        private bool _isRenaming;

        public bool IsRenaming
        {
            get => _isRenaming;
            set => SetProperty(ref _isRenaming, value);
        }


        private string _renameText;

        public string RenameText
        {
            get => _renameText;
            set => SetProperty(ref _renameText, value);
        }


        private DateTime _updatedAt;

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set
            {
                if (SetProperty(ref _updatedAt, value))
                {
                    RaisePropertyChanged(nameof(GroupName));
                    RaisePropertyChanged(nameof(GroupOrder));
                }
            }
        }

        #endregion


        #region Helpers

        private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(Subtitle));
        }

        public Conversation ToData()
        {
            return new Conversation
            {
                Id = Id,
                Title = _title,
                ModelId = _modelId,
                SystemPrompt = _systemPrompt,
                Pinned = _pinned,
                TitleLocked = TitleLocked,
                Parameters = Parameters,
                Messages = Messages.Select(m => m.ToData()).ToList(),
                Usage = Usage.ToList(),
                CreatedAt = CreatedAt,
                UpdatedAt = _updatedAt
            };
        }

        public static ConversationViewModel FromData(Conversation data)
        {
            var vm = new ConversationViewModel
            {
                Id = string.IsNullOrEmpty(data.Id) ? Guid.NewGuid().ToString("N") : data.Id,
                Title = string.IsNullOrEmpty(data.Title) ? Localization.LocalizationManager.Instance["NewChat"] : data.Title,
                ModelId = data.ModelId,
                SystemPrompt = data.SystemPrompt ?? string.Empty,
                Pinned = data.Pinned,
                TitleLocked = data.TitleLocked,
                Parameters = data.Parameters ?? new GenerationParameters(),
                CreatedAt = data.CreatedAt == default ? DateTime.Now : data.CreatedAt,
                UpdatedAt = data.UpdatedAt == default ? DateTime.Now : data.UpdatedAt
            };

            if (data.Messages != null)
            {
                foreach (var message in data.Messages)
                    vm.Messages.Add(ChatMessageViewModel.FromData(message));
            }

            if (data.Usage != null)
            {
                foreach (var usage in data.Usage.Where(u => u != null && string.IsNullOrWhiteSpace(u.ModelId) == false))
                    vm.Usage.Add(usage);
            }

            return vm;
        }

        #endregion
    }
}

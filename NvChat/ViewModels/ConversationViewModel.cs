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
        }

        #endregion


        #region Properties

        public string Id { get; set; }

        public DateTime CreatedAt { get; set; }

        public ObservableCollection<ChatMessageViewModel> Messages { get; }

        public GenerationParameters Parameters { get; set; } = new GenerationParameters();

        public bool IsEmpty => Messages.Count == 0;

        /// <summary>사이드바에 표시할 부제(모델명 · 메시지 수).</summary>
        public string Subtitle
        {
            get
            {
                var model = NvModel.FromId(_modelId ?? string.Empty).Name;
                if (string.IsNullOrEmpty(model))
                    model = "모델 미지정";

                return $"{model}  ·  {Messages.Count}개";
            }
        }

        /// <summary>사이드바 그룹 이름(고정/오늘/어제/…).</summary>
        public string GroupName
        {
            get
            {
                if (_pinned)
                    return "고정됨";

                var today = DateTime.Now.Date;
                var day = _updatedAt.Date;

                if (day >= today) return "오늘";
                if (day == today.AddDays(-1)) return "어제";
                if (day > today.AddDays(-7)) return "지난 7일";
                if (day > today.AddDays(-30)) return "지난 30일";
                return "이전";
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

        private string _title = "새 대화";

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
                Parameters = Parameters,
                Messages = Messages.Select(m => m.ToData()).ToList(),
                CreatedAt = CreatedAt,
                UpdatedAt = _updatedAt
            };
        }

        public static ConversationViewModel FromData(Conversation data)
        {
            var vm = new ConversationViewModel
            {
                Id = string.IsNullOrEmpty(data.Id) ? Guid.NewGuid().ToString("N") : data.Id,
                Title = string.IsNullOrEmpty(data.Title) ? "새 대화" : data.Title,
                ModelId = data.ModelId,
                SystemPrompt = data.SystemPrompt ?? string.Empty,
                Pinned = data.Pinned,
                Parameters = data.Parameters ?? new GenerationParameters(),
                CreatedAt = data.CreatedAt == default ? DateTime.Now : data.CreatedAt,
                UpdatedAt = data.UpdatedAt == default ? DateTime.Now : data.UpdatedAt
            };

            if (data.Messages != null)
            {
                foreach (var message in data.Messages)
                    vm.Messages.Add(ChatMessageViewModel.FromData(message));
            }

            return vm;
        }

        #endregion
    }
}

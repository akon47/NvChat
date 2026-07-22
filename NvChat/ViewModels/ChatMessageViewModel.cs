using NvChat.Commands;
using NvChat.Models;
using NvChat.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 채팅창에 표시되는 하나의 메시지.
    /// </summary>
    public class ChatMessageViewModel : ViewModel
    {
        #region Constructors

        public ChatMessageViewModel(ChatRole role, string content)
        {
            _role = role;
            _content = content ?? string.Empty;
            _timestamp = DateTime.Now;
        }

        #endregion


        #region Fields

        private readonly ChatRole _role;
        private bool _collapsedReasoningOnContent;
        private List<string> _images;
        private IReadOnlyList<ImageSource> _imageSources;

        #endregion


        #region Properties

        public ChatRole Role => _role;

        public bool IsUser => _role == ChatRole.User;

        public bool IsAssistant => _role == ChatRole.Assistant;

        public bool IsSystem => _role == ChatRole.System;

        /// <summary>
        /// 마크다운으로 렌더링할지 여부. 스트리밍 중에도 렌더링해 클로드/챗GPT 처럼
        /// 처음부터 서식이 보이게 한다. (잦은 재구성은 MarkdownPresenter 가 묶어서 처리)
        /// </summary>
        public bool ShowMarkdown => IsAssistant && _hasError == false && _isEditing == false;

        public bool HasReasoning => string.IsNullOrWhiteSpace(_reasoning) == false;

        /// <summary>스트리밍이 시작됐지만 아직 본문/추론이 하나도 안 온 대기 상태(타이핑 애니메이션용).</summary>
        public bool IsWaiting => _isStreaming && string.IsNullOrEmpty(_content) && string.IsNullOrEmpty(_reasoning);

        public string TimestampText => _timestamp.ToString("HH:mm");

        public bool HasImages => _images != null && _images.Count > 0;

        public IReadOnlyList<string> Images => _images;

        /// <summary>표시용으로 디코딩된 이미지 소스(지연 생성).</summary>
        public IReadOnlyList<ImageSource> ImageSources
        {
            get
            {
                if (_imageSources == null && _images != null)
                    _imageSources = _images.Select(ImageUtil.FromDataUri).Where(s => s != null).ToList();

                return _imageSources ?? Array.Empty<ImageSource>();
            }
        }

        #endregion


        #region Bindable Properties

        private string _content;

        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                    RaisePropertyChanged(nameof(IsWaiting));
            }
        }


        private string _reasoning;

        /// <summary>추론(reasoning) 모델의 사고 과정.</summary>
        public string Reasoning
        {
            get => _reasoning;
            set
            {
                if (SetProperty(ref _reasoning, value))
                {
                    RaisePropertyChanged(nameof(HasReasoning));
                    RaisePropertyChanged(nameof(IsWaiting));
                }
            }
        }


        private bool _isReasoningExpanded;

        public bool IsReasoningExpanded
        {
            get => _isReasoningExpanded;
            set => SetProperty(ref _isReasoningExpanded, value);
        }


        private bool _isStreaming;

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (SetProperty(ref _isStreaming, value))
                {
                    RaisePropertyChanged(nameof(ShowMarkdown));

                    // 스트리밍이 끝나면 추론 섹션을 접는다.
                    if (value == false && HasReasoning)
                        IsReasoningExpanded = false;
                }
            }
        }


        private bool _hasError;

        public bool HasError
        {
            get => _hasError;
            set
            {
                if (SetProperty(ref _hasError, value))
                    RaisePropertyChanged(nameof(ShowMarkdown));
            }
        }


        private bool _aborted;

        /// <summary>사용자가 생성을 중단한 메시지인지. (본문은 부분적으로 유효)</summary>
        public bool Aborted
        {
            get => _aborted;
            set => SetProperty(ref _aborted, value);
        }


        private bool _isEditing;

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (SetProperty(ref _isEditing, value))
                    RaisePropertyChanged(nameof(ShowMarkdown));
            }
        }


        private string _editText;

        public string EditText
        {
            get => _editText;
            set => SetProperty(ref _editText, value);
        }


        private DateTime _timestamp;

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (SetProperty(ref _timestamp, value))
                    RaisePropertyChanged(nameof(TimestampText));
            }
        }

        #endregion


        #region Commands

        private DelegateCommand _copyCommand;

        public ICommand CopyCommand => _copyCommand ?? (_copyCommand = new DelegateCommand(OnCopy));


        private DelegateCommand _toggleReasoningCommand;

        public ICommand ToggleReasoningCommand => _toggleReasoningCommand ?? (_toggleReasoningCommand = new DelegateCommand(() => IsReasoningExpanded = !IsReasoningExpanded));


        private DelegateCommand _beginEditCommand;

        public ICommand BeginEditCommand => _beginEditCommand ?? (_beginEditCommand = new DelegateCommand(OnBeginEdit));


        private DelegateCommand _cancelEditCommand;

        public ICommand CancelEditCommand => _cancelEditCommand ?? (_cancelEditCommand = new DelegateCommand(() => IsEditing = false));

        #endregion


        #region Helpers

        /// <summary>본문 스트리밍 델타를 이어붙인다.</summary>
        public void AppendContent(string delta)
        {
            if (string.IsNullOrEmpty(delta))
                return;

            // 첫 본문 토큰이 오면 추론 섹션을 접는다.
            if (_collapsedReasoningOnContent == false && HasReasoning)
            {
                _collapsedReasoningOnContent = true;
                IsReasoningExpanded = false;
            }

            Content = (_content ?? string.Empty) + delta;
        }

        /// <summary>추론 스트리밍 델타를 이어붙인다.</summary>
        public void AppendReasoning(string delta)
        {
            if (string.IsNullOrEmpty(delta))
                return;

            var wasEmpty = string.IsNullOrEmpty(_reasoning);
            Reasoning = (_reasoning ?? string.Empty) + delta;

            // 아직 본문이 없으면 추론을 펼쳐서 실시간으로 보여준다.
            if (wasEmpty && string.IsNullOrEmpty(_content))
                IsReasoningExpanded = true;
        }

        private void OnBeginEdit()
        {
            EditText = _content;
            IsEditing = true;
        }

        private void OnCopy()
        {
            try
            {
                Clipboard.SetText(_content ?? string.Empty);
            }
            catch
            {
                // 클립보드 접근 실패는 무시.
            }
        }

        /// <summary>첨부 이미지(data URI)를 설정한다.</summary>
        public void SetImages(IEnumerable<string> images)
        {
            _images = images?.Where(s => string.IsNullOrEmpty(s) == false).ToList();
            _imageSources = null;
            RaisePropertyChanged(nameof(HasImages));
            RaisePropertyChanged(nameof(ImageSources));
        }

        public ChatMessage ToData()
        {
            return new ChatMessage
            {
                Role = _role,
                Content = _content,
                Reasoning = _reasoning,
                Images = _images,
                Timestamp = _timestamp
            };
        }

        public static ChatMessageViewModel FromData(ChatMessage data)
        {
            var viewModel = new ChatMessageViewModel(data.Role, data.Content)
            {
                Reasoning = data.Reasoning,
                Timestamp = data.Timestamp == default ? DateTime.Now : data.Timestamp
            };

            if (data.Images != null && data.Images.Count > 0)
                viewModel.SetImages(data.Images);

            return viewModel;
        }

        #endregion
    }
}

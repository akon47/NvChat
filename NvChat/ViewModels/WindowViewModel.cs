using NvChat.Commands;
using System;
using System.Windows.Input;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 창(Window)과 결합되는 뷰모델의 기반 클래스.
    /// 닫기 요청 프로토콜(<see cref="RequestClose"/> → <see cref="CloseRequested"/>)을 제공한다.
    /// </summary>
    public class WindowViewModel : ViewModel
    {
        #region Constructors

        protected WindowViewModel(ViewModel parent = null) : base(parent)
        {
        }

        #endregion


        #region Bindable Properties

        private bool _isActivated;

        /// <summary>
        /// 창이 활성 상태인지 여부. 바인딩된 경우 UI 스레드에서만 사용해야 한다.
        /// </summary>
        public bool IsActivated
        {
            get => _isActivated;
            set => SetProperty(ref _isActivated, value);
        }

        #endregion


        #region Commands

        private DelegateCommand _requestCloseCommand;

        /// <summary>
        /// 닫기를 요청하는 커맨드. 항상 null 이 아니다.
        /// </summary>
        public ICommand RequestCloseCommand => _requestCloseCommand ?? (_requestCloseCommand = new DelegateCommand(RequestClose));

        #endregion


        #region Helpers

        /// <summary>
        /// 닫기를 요청한다.
        /// </summary>
        public void RequestClose()
        {
            OnRequestClose();
        }

        /// <summary>
        /// 닫기를 요청한다. 파생 클래스에서 재정의하여 닫기 전 처리를 넣을 수 있다.
        /// </summary>
        protected virtual void OnRequestClose()
        {
            OnCloseRequested();
        }

        #endregion


        #region Events

        /// <summary>
        /// 닫기 요청이 확정되었을 때 UI 스레드에서 발생하는 이벤트.
        /// </summary>
        public event EventHandler CloseRequested;

        protected virtual void OnCloseRequested()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}

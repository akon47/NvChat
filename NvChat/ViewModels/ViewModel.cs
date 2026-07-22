using NvChat.ComponentModel;
using System;
using System.Windows;
using System.Windows.Threading;

namespace NvChat.ViewModels
{
    /// <summary>
    /// 모든 뷰모델의 기반 클래스.
    /// </summary>
    public abstract class ViewModel : ObservableObject
    {
        #region Constructors

        protected ViewModel()
        {
        }

        protected ViewModel(ViewModel parent)
        {
            Parent = parent;
        }

        #endregion


        #region Properties

        protected ViewModel Parent { get; }

        public Dispatcher Dispatcher => Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        #endregion


        #region Helpers

        /// <summary>
        /// Dispatcher.BeginInvoke 를 호출한다.
        /// </summary>
        protected void BeginInvoke(Action action)
        {
            Dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// Dispatcher.BeginInvoke 를 호출한다.
        /// </summary>
        protected void BeginInvoke(Action action, DispatcherPriority priority)
        {
            Dispatcher.BeginInvoke(action, priority);
        }

        #endregion
    }
}

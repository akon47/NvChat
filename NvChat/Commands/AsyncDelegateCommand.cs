using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NvChat.Commands
{
    /// <summary>
    /// 비동기 작업을 수행하는 <see cref="ICommand"/>. 실행 중에는 자동으로 비활성화된다.
    /// </summary>
    public class AsyncDelegateCommand : ICommand
    {
        #region Constructors

        public AsyncDelegateCommand(Func<Task> executeAsync, Func<bool> canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        #endregion


        #region Fields

        private readonly Func<Task> _executeAsync;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        #endregion


        #region Properties

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting == value)
                    return;

                _isExecuting = value;
                RaiseCanExecuteChanged();
            }
        }

        #endregion


        #region Helpers

        public bool CanExecute(object parameter)
        {
            return _isExecuting == false && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter) == false)
                return;

            IsExecuting = true;

            try
            {
                await _executeAsync();
            }
            finally
            {
                IsExecuting = false;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion


        #region Events

        public event EventHandler CanExecuteChanged;

        #endregion
    }
}

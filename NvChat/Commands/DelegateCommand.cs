using System;
using System.Windows.Input;

namespace NvChat.Commands
{
    /// <summary>
    /// 파라미터 없는 <see cref="ICommand"/> 구현.
    /// </summary>
    public class DelegateCommand : ICommand
    {
        #region Constructors

        public DelegateCommand(Action executeMethod) : this(executeMethod, () => true)
        {
        }

        public DelegateCommand(Action executeMethod, Func<bool> canExecuteMethod)
        {
            _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
            _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        }

        #endregion


        #region Fields

        private readonly Action _executeMethod;
        private readonly Func<bool> _canExecuteMethod;

        #endregion


        #region Helpers

        public bool CanExecute(object parameter)
        {
            return _canExecuteMethod();
        }

        public void Execute(object parameter)
        {
            _executeMethod();
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

    /// <summary>
    /// 파라미터를 받는 <see cref="ICommand"/> 구현.
    /// </summary>
    public class DelegateCommand<T> : ICommand
    {
        #region Constructors

        public DelegateCommand(Action<T> executeMethod) : this(executeMethod, _ => true)
        {
        }

        public DelegateCommand(Action<T> executeMethod, Func<T, bool> canExecuteMethod)
        {
            _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
            _canExecuteMethod = canExecuteMethod;
        }

        #endregion


        #region Fields

        private readonly Action<T> _executeMethod;
        private readonly Func<T, bool> _canExecuteMethod;

        #endregion


        #region Helpers

        public bool CanExecute(object parameter)
        {
            if (_canExecuteMethod == null)
                return true;

            if (parameter == null && typeof(T).IsValueType)
                return false;

            if (parameter is T typedParameter)
                return _canExecuteMethod(typedParameter);

            // T가 참조 형식인 경우 null은 유효한 파라미터이다.
            if (parameter == null)
                return _canExecuteMethod(default(T));

            return false;
        }

        public void Execute(object parameter)
        {
            if (parameter == null && typeof(T).IsValueType)
                return;

            if (parameter is T typedParameter)
            {
                _executeMethod(typedParameter);
                return;
            }

            if (parameter == null)
            {
                _executeMethod(default(T));
                return;
            }

            _executeMethod((T)parameter);
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

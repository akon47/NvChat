using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NvChat.ComponentModel
{
    /// <summary>
    /// INotifyPropertyChanging/Changed 를 구현하는 관찰 가능한 객체의 기반 클래스.
    /// (Gaia.ComponentModel.ObservableObject 스타일)
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
    {
        #region Helpers

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            OnPropertyChanging(propertyName);
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            OnPropertyChanged(propertyName);
        }

        #endregion


        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }


        public event PropertyChangingEventHandler PropertyChanging;

        protected void OnPropertyChanging(string propertyName)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName ?? string.Empty));
        }

        #endregion
    }
}

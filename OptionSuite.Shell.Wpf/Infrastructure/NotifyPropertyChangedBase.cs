using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OptionSuite.Shell.Wpf.Infrastructure
{
    /// <summary>
    /// Minimal bas för INotifyPropertyChanged så att Shell kan uppdatera UI vid selection,
    /// detached/attached-flöden och kommande state (busy, counts, etc.).
    /// </summary>
    public abstract class NotifyPropertyChangedBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}

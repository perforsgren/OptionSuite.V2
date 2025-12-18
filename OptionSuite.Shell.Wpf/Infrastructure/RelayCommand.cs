using System;
using System.Windows.Input;

namespace OptionSuite.Shell.Wpf.Infrastructure
{
    /// <summary>
    /// ICommand-implementation som stödjer både parameterlösa actions (Action)
    /// och actions med parameter (Action&lt;object&gt;).
    ///
    /// Syfte:
    /// - Kunna skriva: new RelayCommand(ExecuteRefresh) där ExecuteRefresh() är parameterlös
    /// - Kunna skriva: new RelayCommand(ExecuteSelectWorkspace) där ExecuteSelectWorkspace(object p) tar parameter
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        private readonly Action<object> _executeWithParam;
        private readonly Func<object, bool> _canExecuteWithParam;

        /// <summary>
        /// Skapar ett kommando för parameterlös execute (Action).
        /// </summary>
        public RelayCommand(Action execute)
            : this(execute, null)
        {
        }

        /// <summary>
        /// Skapar ett kommando för parameterlös execute (Action) med CanExecute (Func&lt;bool&gt;).
        /// </summary>
        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            _execute = execute;
            _canExecute = canExecute;

            _executeWithParam = null;
            _canExecuteWithParam = null;
        }

        /// <summary>
        /// Skapar ett kommando för execute med parameter (Action&lt;object&gt;).
        /// </summary>
        public RelayCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        /// <summary>
        /// Skapar ett kommando för execute med parameter (Action&lt;object&gt;) med CanExecute (Func&lt;object, bool&gt;).
        /// </summary>
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));

            _executeWithParam = execute;
            _canExecuteWithParam = canExecute;

            _execute = null;
            _canExecute = null;
        }

        /// <summary>
        /// Avgör om kommandot kan köras. Väljer rätt CanExecute beroende på vilken konstruktor som använts.
        /// </summary>
        public bool CanExecute(object parameter)
        {
            if (_executeWithParam != null)
            {
                return _canExecuteWithParam == null || _canExecuteWithParam(parameter);
            }

            return _canExecute == null || _canExecute();
        }

        /// <summary>
        /// Kör kommandot. Väljer rätt execute beroende på vilken konstruktor som använts.
        /// </summary>
        public void Execute(object parameter)
        {
            if (_executeWithParam != null)
            {
                _executeWithParam(parameter);
                return;
            }

            _execute();
        }

        /// <summary>
        /// Triggar att WPF frågar om CanExecute igen (för att enable/disable UI).
        /// </summary>
        public event EventHandler CanExecuteChanged;

        /// <summary>
        /// Begär att WPF uppdaterar CanExecute-status.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }

    // Lägg under samma region/område som RelayCommand (Infrastructure)

    /// <summary>
    /// Typad ICommand-implementation för commands som tar en parameter av typ T.
    ///
    /// Syfte:
    /// - Kunna skriva: new RelayCommand&lt;PricerInstanceViewModel&gt;(ClosePricer)
    ///   där ClosePricer(PricerInstanceViewModel vm) är starkt typad.
    ///
    /// Not:
    /// - Om parameter är null och T är en referenstyp, skickas default(T) (dvs null).
    /// - Om parameter inte kan castas till T, så körs inte Execute/CanExecute (returnerar false).
    /// </summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        /// <summary>
        /// Skapar ett typat kommando för execute (Action&lt;T&gt;).
        /// </summary>
        public RelayCommand(Action<T> execute)
            : this(execute, null)
        {
        }

        /// <summary>
        /// Skapar ett typat kommando för execute (Action&lt;T&gt;) med CanExecute (Func&lt;T, bool&gt;).
        /// </summary>
        public RelayCommand(Action<T> execute, Func<T, bool> canExecute)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            _execute = execute;
            _canExecute = canExecute;
        }

        /// <summary>
        /// Avgör om kommandot kan köras. Returnerar false om parameter inte är av typ T.
        /// </summary>
        public bool CanExecute(object parameter)
        {
            if (!TryGetParameter(parameter, out var value))
            {
                return false;
            }

            return _canExecute == null || _canExecute(value);
        }

        /// <summary>
        /// Kör kommandot. Om parameter inte är av typ T görs ingenting.
        /// </summary>
        public void Execute(object parameter)
        {
            if (!TryGetParameter(parameter, out var value))
            {
                return;
            }

            _execute(value);
        }

        /// <summary>
        /// Triggar att WPF frågar om CanExecute igen (för att enable/disable UI).
        /// </summary>
        public event EventHandler CanExecuteChanged;

        /// <summary>
        /// Begär att WPF uppdaterar CanExecute-status.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static bool TryGetParameter(object parameter, out T value)
        {
            if (parameter == null)
            {
                value = default(T);
                return true;
            }

            if (parameter is T cast)
            {
                value = cast;
                return true;
            }

            value = default(T);
            return false;
        }
    }
}

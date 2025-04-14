using System.ComponentModel;

namespace LabSystemApp.Helpers
{
    /// <summary>
    /// Содержит вспомогательные классы для работы с данными лабораторной системы.
    /// </summary>
    public static class Classes
    {
        /// <summary>
        /// Модель представления для отображения услуги в заказе в пользовательском интерфейсе.
        /// Реализует интерфейс <see cref="INotifyPropertyChanged"/> для уведомления об изменениях свойств.
        /// </summary>
        public class OrderServiceViewModel : INotifyPropertyChanged
        {
            private string _result;
            private double _progress;
            private string _statusName;
            private string _patientFullName;
            private bool _isProcessing;

            /// <summary>
            /// Инициализирует новый экземпляр класса <see cref="OrderServiceViewModel"/>.
            /// </summary>
            public OrderServiceViewModel()
            {
                _result = string.Empty;
                _statusName = string.Empty;
                _patientFullName = string.Empty;
            }

            /// <summary>
            /// Получает или задает данные услуги в заказе.
            /// </summary>
            public OrderService OrderService { get; set; }

            /// <summary>
            /// Получает или задает результат выполнения услуги.
            /// </summary>
            public string Result
            {
                get => _result;
                set
                {
                    if (_result != value)
                    {
                        _result = value;
                        OnPropertyChanged(nameof(Result));
                    }
                }
            }

            /// <summary>
            /// Получает или задает прогресс выполнения услуги (в процентах).
            /// </summary>
            public double Progress
            {
                get => _progress;
                set
                {
                    if (_progress != value)
                    {
                        _progress = value;
                        OnPropertyChanged(nameof(Progress));
                    }
                }
            }

            /// <summary>
            /// Получает или задает название текущего статуса услуги.
            /// </summary>
            public string StatusName
            {
                get => _statusName;
                set
                {
                    if (_statusName != value)
                    {
                        _statusName = value;
                        OnPropertyChanged(nameof(StatusName));
                    }
                }
            }

            /// <summary>
            /// Получает или задает полное имя пациента, связанного с заказом.
            /// </summary>
            public string PatientFullName
            {
                get => _patientFullName;
                set
                {
                    if (_patientFullName != value)
                    {
                        _patientFullName = value;
                        OnPropertyChanged(nameof(PatientFullName));
                    }
                }
            }

            /// <summary>
            /// Получает или задает значение, указывающее, находится ли услуга в процессе выполнения.
            /// </summary>
            public bool IsProcessing
            {
                get => _isProcessing;
                set
                {
                    if (_isProcessing != value)
                    {
                        _isProcessing = value;
                        OnPropertyChanged(nameof(IsProcessing));
                    }
                }
            }

            /// <summary>
            /// Событие, возникающее при изменении значения свойства.
            /// </summary>
            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>
            /// Вызывает событие <see cref="PropertyChanged"/> для уведомления об изменении свойства.
            /// </summary>
            /// <param name="propertyName">Имя изменившегося свойства.</param>
            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
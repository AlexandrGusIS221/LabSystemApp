using LabSystemApp.Scripts;
using LabSystemApp.Views;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LabSystemApp.Helpers
{
    /// <summary>
    /// Статический класс для управления сессиями пользователей и навигацией между окнами приложения.
    /// Отслеживает время активности сессии, выполняет автоматический выход и блокировку входа.
    /// </summary>
    public static class SessionManager
    {
        /// <summary>
        /// Таймер для отслеживания времени сессии.
        /// </summary>
        private static DispatcherTimer _sessionTimer;

        /// <summary>
        /// Оставшееся время до завершения текущей сессии.
        /// </summary>
        private static TimeSpan _remainingTime;

        /// <summary>
        /// Текущее активное окно приложения.
        /// </summary>
        private static Window _currentWindow;

        /// <summary>
        /// Флаг, указывающий, было ли отправлено уведомление о скором завершении сессии.
        /// </summary>
        private static bool _notified;

        /// <summary>
        /// Максимальная длительность сессии в минутах (150 минут = 2 часа 30 минут).
        /// </summary>
        private const int MaxSessionMinutes = 150;

        /// <summary>
        /// Время до завершения сессии, за которое отправляется уведомление (15 минут).
        /// </summary>
        private const int NotifyBeforeMinutes = 15;

        /// <summary>
        /// Длительность блокировки входа после завершения сессии (30 минут).
        /// </summary>
        private const int BlockDurationMinutes = 30;

        /// <summary>
        /// Указывает, заблокирован ли вход в систему.
        /// </summary>
        public static bool IsLoginBlocked { get; private set; }

        /// <summary>
        /// Время, до которого вход заблокирован (может быть null, если блокировка снята).
        /// </summary>
        public static DateTime? BlockedUntil { get; private set; }

        /// <summary>
        /// Событие, вызываемое при обновлении оставшегося времени сессии.
        /// </summary>
        public static event Action<TimeSpan> TimeUpdated;

        /// <summary>
        /// Запускает таймер для отслеживания длительности сессии пользователя в указанном окне.
        /// </summary>
        /// <param name="window">Окно, для которого запускается таймер сессии.</param>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="window"/> равен null.</exception>
        public static void StartSessionTimer(Window window)
        {
            StopTimer(); // Останавливаем предыдущий таймер, если есть

            _sessionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _sessionTimer.Tick += SessionTimer_Tick;

            _currentWindow = window;
            _remainingTime = TimeSpan.FromMinutes(MaxSessionMinutes);
            _notified = false;

            _sessionTimer.Start();
        }

        /// <summary>
        /// Обрабатывает событие тика таймера сессии, обновляя оставшееся время и выполняя действия при необходимости.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (таймер).</param>
        /// <param name="e">Аргументы события.</param>
        private static void SessionTimer_Tick(object sender, EventArgs e)
        {
            _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));
            TimeUpdated?.Invoke(_remainingTime);

            if (!_notified && _remainingTime <= TimeSpan.FromMinutes(NotifyBeforeMinutes))
            {
                _notified = true;
                ShowWarning($"Сеанс завершится через {_remainingTime:mm\\:ss}.");
            }

            if (_remainingTime <= TimeSpan.Zero)
            {
                EndSession();
            }
        }

        /// <summary>
        /// Завершает текущую сессию, блокирует вход и выполняет выход пользователя.
        /// </summary>
        private static async void EndSession()
        {
            _sessionTimer.Stop();
            IsLoginBlocked = true;
            BlockedUntil = DateTime.UtcNow.AddMinutes(BlockDurationMinutes);
            ShowInformation("Сеанс завершён. Вход заблокирован на 30 минут.");

            await LogoutAsync(_currentWindow);
        }

        /// <summary>
        /// Останавливает таймер сессии и отключает обработчик событий.
        /// </summary>
        public static void StopTimer()
        {
            if (_sessionTimer != null)
            {
                _sessionTimer.Stop();
                _sessionTimer.Tick -= SessionTimer_Tick;
            }
        }

        /// <summary>
        /// Проверяет, возможен ли вход в систему в текущий момент.
        /// </summary>
        /// <returns>True, если вход разрешен; иначе false.</returns>
        public static bool CanLogin()
        {
            if (!IsLoginBlocked)
                return true;

            if (BlockedUntil.HasValue && DateTime.UtcNow >= BlockedUntil.Value)
            {
                IsLoginBlocked = false;
                BlockedUntil = null;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Перенаправляет пользователя на соответствующее окно в зависимости от его роли.
        /// </summary>
        /// <param name="user">Объект пользователя с информацией о роли.</param>
        /// <exception cref="InvalidOperationException">Вызывается, если роль пользователя неизвестна.</exception>
        public static void RedirectByRole(User user)
        {
            if (!CanLogin())
            {
                ShowError("Вход заблокирован. Попробуйте позже.");
                return;
            }

            Window nextWindow = user.RoleID switch
            {
                1 => new PatientWindow(user),
                2 => new LabOrderWindow(user),
                3 => new AccountantWindow(user),
                4 => new AdminWindow(user),
                5 => new LabAnalysis(user),
                _ => throw new InvalidOperationException($"Неизвестная роль: {user.RoleID}")
            };

            StartSessionTimer(nextWindow);
            nextWindow.Show();
        }

        /// <summary>
        /// Асинхронно выполняет выход пользователя из системы и открывает окно авторизации.
        /// </summary>
        /// <param name="currentWindow">Текущее окно, которое будет закрыто.</param>
        /// <returns>Задача, представляющая асинхронную операцию выхода.</returns>
        public static async Task LogoutAsync(Window currentWindow)
        {
            StopTimer();
            await DatabaseManager.CloseCurrentSessionAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                new AuthWindow().Show();
                currentWindow?.Close();
            });
        }

        /// <summary>
        /// Отображает предупреждающее сообщение в диалоговом окне.
        /// </summary>
        /// <param name="message">Текст предупреждения для отображения.</param>
        private static void ShowWarning(string message)
        {
            MessageBox.Show(message, "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Отображает информационное сообщение в диалоговом окне.
        /// </summary>
        /// <param name="message">Текст информационного сообщения для отображения.</param>
        private static void ShowInformation(string message)
        {
            MessageBox.Show(message, "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Отображает сообщение об ошибке в диалоговом окне.
        /// </summary>
        /// <param name="message">Текст сообщения об ошибке для отображения.</param>
        private static void ShowError(string message)
        {
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
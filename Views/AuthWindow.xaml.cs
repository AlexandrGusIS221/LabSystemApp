using LabSystemApp.Helpers;
using LabSystemApp.Scripts;
using SkiaSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно авторизации пользователей медицинской лабораторной системы.
    /// Поддерживает проверку логина и пароля, капчу после неудачной попытки и временную блокировку входа.
    /// </summary>
    public partial class AuthWindow : Window
    {
        /// <summary>
        /// Таймер для отслеживания времени блокировки входа.
        /// </summary>
        private readonly DispatcherTimer _loginBlockTimer;

        /// <summary>
        /// Объект для проверки и хеширования паролей.
        /// </summary>
        private readonly PasswordHelper _passwordHelper;

        /// <summary>
        /// Генератор случайных чисел для создания капчи.
        /// </summary>
        private readonly Random _random = new Random();

        /// <summary>
        /// Оставшееся время блокировки входа.
        /// </summary>
        private TimeSpan _loginBlockTime;

        /// <summary>
        /// Текущий код капчи.
        /// </summary>
        private string _currentCaptcha = string.Empty;

        /// <summary>
        /// Количество неудачных попыток входа.
        /// </summary>
        private int _failedAttempts;

        /// <summary>
        /// Инициализирует новое окно авторизации, настраивает таймер и генерирует капчу.
        /// </summary>
        public AuthWindow()
        {
            InitializeComponent();
            _passwordHelper = new PasswordHelper();
            _loginBlockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _loginBlockTimer.Tick += LoginBlockTimer_Tick;
            GenerateCaptcha();
        }

        // ==== АВТОРИЗАЦИЯ ====

        /// <summary>
        /// Обрабатывает попытку входа пользователя, проверяя логин и пароль.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loginBlockTimer.IsEnabled || !LoginButton.IsEnabled)
                return;

            LoginButton.IsEnabled = false;
            ErrorLabel.Text = string.Empty;

            string login = LoginInput.Text.Trim();
            string password = PasswordBox.Password;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                ShowError("Введите логин и пароль.");
                LoginButton.IsEnabled = true;
                return;
            }

            var user = await DatabaseManager.FindUserByLoginAsync(login);
            bool isPasswordValid = user != null && _passwordHelper.VerifyPassword(password, user.Password);

            var session = new SessionHistory
            {
                UserID = user?.UserID,
                LoginTime = DateTime.UtcNow,
                WasSuccessful = isPasswordValid,
                ip = GetLocalIpAddress()
            };

            await DatabaseManager.RecordSessionAsync(session);

            if (!isPasswordValid)
            {
                HandleFailedLogin();
                return;
            }

            await HandleSuccessfulLoginAsync(user);
        }

        /// <summary>
        /// Обрабатывает неудачную попытку входа, активируя капчу или блокировку.
        /// </summary>
        private void HandleFailedLogin()
        {
            _failedAttempts++;

            if (_failedAttempts == 1)
            {
                ShowError("Неверный логин или пароль. Введите капчу.");
                CaptchaPanel.Visibility = Visibility.Visible;
                GenerateCaptcha();
                LoginButton.IsEnabled = false;
                return;
            }

            ShowError("Слишком много попыток. Вход заблокирован на 10 секунд.");
            CaptchaPanel.Visibility = Visibility.Collapsed;
            CaptchaInput.Text = string.Empty;
            BlockLoginFor(TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Асинхронно обрабатывает успешный вход, закрывая предыдущие сессии и перенаправляя по роли.
        /// </summary>
        /// <param name="user">Объект пользователя, прошедшего авторизацию.</param>
        /// <returns>Задача, представляющая асинхронную операцию.</returns>
        private async Task HandleSuccessfulLoginAsync(User user)
        {
            _failedAttempts = 0;
            await DatabaseManager.ClosePreviousSessionsAsync(user.UserID);
            SessionManager.RedirectByRole(user);
            Close();
        }

        // ==== CAPTCHA ====

        /// <summary>
        /// Проверяет введенную капчу и управляет доступом к кнопке входа.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void ConfirmCaptcha_Click(object sender, RoutedEventArgs e)
        {
            if (CaptchaInput.Text.Trim().Equals(_currentCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                ErrorLabel.Text = string.Empty;
                CaptchaInput.Text = string.Empty;
                LoginButton.IsEnabled = true;
                CaptchaPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowError("Неверная капча.");
                CaptchaInput.Text = string.Empty;
                GenerateCaptcha();
            }
        }

        /// <summary>
        /// Генерирует новую капчу и отображает её в интерфейсе.
        /// </summary>
        private void GenerateCaptcha()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            _currentCaptcha = new string(Enumerable.Repeat(chars, 4)
                .Select(s => s[_random.Next(s.Length)])
                .ToArray());

            using var bitmap = new Bitmap(200, 80);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.LightGray);

            DrawCaptchaNoise(graphics, bitmap);
            DrawCaptchaText(graphics);
            DrawCaptchaGrid(graphics, bitmap);

            CaptchaImage.Source = ConvertBitmapToImageSource(bitmap);
        }

        /// <summary>
        /// Добавляет шум на изображение капчи (точки и кривые).
        /// </summary>
        /// <param name="graphics">Объект для рисования.</param>
        /// <param name="bitmap">Изображение капчи.</param>
        private void DrawCaptchaNoise(Graphics graphics, Bitmap bitmap)
        {
            for (int i = 0; i < 20; i++)
            {
                int x = _random.Next(bitmap.Width);
                int y = _random.Next(bitmap.Height);
                int radius = _random.Next(5, 10);
                graphics.DrawEllipse(
                    new Pen(Color.FromArgb(150, _random.Next(255), _random.Next(255), _random.Next(255)), 1),
                    x, y, radius, radius);
            }

            for (int i = 0; i < 2; i++)
            {
                int x1 = 0, y1 = _random.Next(20, 60);
                int x2 = bitmap.Width, y2 = _random.Next(20, 60);
                graphics.DrawCurve(
                    new Pen(Color.FromArgb(100, Color.DarkBlue), 2),
                    new[]
                    {
                        new System.Drawing.Point(x1, y1),
                        new System.Drawing.Point(x1 + 50, y1 + _random.Next(-10, 10)),
                        new System.Drawing.Point(x2 - 50, y2 + _random.Next(-10, 10)),
                        new System.Drawing.Point(x2, y2)
                    });
            }
        }

        /// <summary>
        /// Рисует текст капчи с случайным расположением и поворотом символов.
        /// </summary>
        /// <param name="graphics">Объект для рисования.</param>
        private void DrawCaptchaText(Graphics graphics)
        {
            for (int i = 0; i < _currentCaptcha.Length; i++)
            {
                int x = 25 + i * 40 + _random.Next(-5, 5);
                int y = _random.Next(25, 45);
                float angle = _random.Next(-20, 20);

                using var font = new Font("Verdana", 22, System.Drawing.FontStyle.Italic);
                graphics.TranslateTransform(x, y);
                graphics.RotateTransform(angle);
                graphics.DrawString(_currentCaptcha[i].ToString(), font, Brushes.DarkRed, 0, 0);
                graphics.RotateTransform(-angle);
                graphics.TranslateTransform(-x, -y);
            }
        }

        /// <summary>
        /// Рисует сетку на изображении капчи для усложнения чтения.
        /// </summary>
        /// <param name="graphics">Объект для рисования.</param>
        /// <param name="bitmap">Изображение капчи.</param>
        private void DrawCaptchaGrid(Graphics graphics, Bitmap bitmap)
        {
            using var pen = new Pen(Color.FromArgb(50, Color.Black), 1);
            for (int i = 0; i < bitmap.Width; i += 20)
                graphics.DrawLine(pen, i, 0, i, bitmap.Height);
            for (int i = 0; i < bitmap.Height; i += 20)
                graphics.DrawLine(pen, 0, i, bitmap.Width, i);
        }

        /// <summary>
        /// Преобразует объект Bitmap в BitmapImage для отображения в WPF.
        /// </summary>
        /// <param name="bitmap">Исходное изображение в формате Bitmap.</param>
        /// <returns>Объект <see cref="BitmapImage"/> для использования в интерфейсе.</returns>
        private static BitmapImage ConvertBitmapToImageSource(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        /// <summary>
        /// Обновляет изображение капчи по запросу пользователя.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void RefreshCaptcha_Click(object sender, RoutedEventArgs e)
        {
            GenerateCaptcha();
            CaptchaInput.Text = string.Empty; // Очищаем поле ввода
            ErrorLabel.Text = string.Empty; // Убираем сообщение об ошибке
        }

        /// <summary>
        /// Блокирует возможность входа на указанное время.
        /// </summary>
        /// <param name="time">Продолжительность блокировки.</param>
        private void BlockLoginFor(TimeSpan time)
        {
            _loginBlockTime = time;
            LoginButton.IsEnabled = false;
            UpdateLoginButtonText();
            _loginBlockTimer.Start();
        }

        /// <summary>
        /// Обновляет текст кнопки входа, отображая время блокировки.
        /// </summary>
        private void UpdateLoginButtonText()
        {
            LoginButton.Content = _loginBlockTime.TotalSeconds > 0
                ? $"Заблокировано ({(int)_loginBlockTime.TotalSeconds})"
                : "Войти";
        }

        /// <summary>
        /// Обрабатывает тик таймера блокировки, обновляя состояние интерфейса.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (таймер).</param>
        /// <param name="e">Аргументы события.</param>
        private void LoginBlockTimer_Tick(object sender, EventArgs e)
        {
            if (_loginBlockTime.TotalSeconds <= 1)
            {
                _loginBlockTimer.Stop();
                _failedAttempts = 0;
                ErrorLabel.Text = string.Empty;
                LoginButton.IsEnabled = true;
                UpdateLoginButtonText();
                return;
            }

            _loginBlockTime = _loginBlockTime.Subtract(TimeSpan.FromSeconds(1));
            UpdateLoginButtonText();
        }

        // ==== ПАРОЛЬ ====

        /// <summary>
        /// Показывает пароль в открытом виде при нажатии на элемент.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void ShowPassword_Pressed(object sender, MouseButtonEventArgs e)
        {
            VisiblePasswordBox.Text = PasswordBox.Password;
            VisiblePasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Скрывает пароль, возвращая защищенное поле ввода.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void HidePassword_Pressed(object sender, MouseButtonEventArgs e)
        {
            PasswordBox.Password = VisiblePasswordBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            VisiblePasswordBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Получает локальный IP-адрес устройства.
        /// </summary>
        /// <returns>IP-адрес в виде строки или "127.0.0.1" при ошибке.</returns>
        private static string GetLocalIpAddress()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        /// <summary>
        /// Отображает сообщение об ошибке в интерфейсе.
        /// </summary>
        /// <param name="message">Текст ошибки для отображения.</param>
        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
        }

        /// <summary>
        /// Останавливает таймер блокировки при закрытии окна.
        /// </summary>
        /// <param name="e">Аргументы события закрытия.</param>
        protected override void OnClosed(EventArgs e)
        {
            _loginBlockTimer.Stop();
            _loginBlockTimer.Tick -= LoginBlockTimer_Tick;
            base.OnClosed(e);
        }
    }
}
using LabSystemApp.Helpers;
using LabSystemApp.Scripts;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно для просмотра пациентом завершённых лабораторных исследований.
    /// Отображает информацию о пользователе и позволяет завершить сессию.
    /// </summary>
    public partial class PatientWindow : Window
    {
        /// <summary>
        /// Текущий пользователь (пациент), вошедший в систему.
        /// </summary>
        private readonly User _currentUser;

        /// <summary>
        /// Инициализирует новое окно для пациента.
        /// </summary>
        /// <param name="currentUser">Объект пользователя с данными пациента.</param>
        public PatientWindow(User currentUser)
        {
            InitializeComponent();
            _currentUser = currentUser;
            InitializeUserInterface();
        }

        /// <summary>
        /// Настраивает пользовательский интерфейс, отображая данные текущего пациента.
        /// Устанавливает имя, роль и изображение профиля.
        /// </summary>
        private void InitializeUserInterface()
        {
            FullNameText.Text = _currentUser.FullName ?? "Неизвестно";
            RoleText.Text = _currentUser.Role?.Name ?? "Неизвестно";
            string imageName = string.IsNullOrEmpty(_currentUser.Image) ? "default.gif" : _currentUser.Image;

            try
            {
                ProfileImage.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{imageName}"));
            }
            catch
            {
                ProfileImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/default.gif"));
            }
        }

        /// <summary>
        /// Выполняет выход из текущей сессии и возвращает пользователя к окну авторизации.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            _ = SessionManager.LogoutAsync(this);
        }

        /// <summary>
        /// Завершает текущую сессию при закрытии окна.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            _ = DatabaseManager.CloseCurrentSessionAsync();
        }
    }
}
using LabSystemApp.Helpers;
using LabSystemApp.Scripts;
using MAIL_LIB;
using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно администратора для управления пользователями, ролями и историей входов в систему.
    /// Поддерживает добавление сотрудников, фильтрацию данных и хеширование паролей.
    /// </summary>
    public partial class AdminWindow : Window
    {
        /// <summary>
        /// Текущий пользователь (администратор), вошедший в систему.
        /// </summary>
        private readonly User _currentUser;

        /// <summary>
        /// Объект для хеширования паролей пользователей.
        /// </summary>
        private readonly PasswordHelper _passwordHelper = new PasswordHelper();

        /// <summary>
        /// Путь к выбранному изображению профиля сотрудника.
        /// </summary>
        private string _selectedImagePath;

        /// <summary>
        /// Изображение профиля по умолчанию, используемое при отсутствии фото.
        /// </summary>
        private readonly BitmapImage _defaultImage = new BitmapImage(
            new Uri("pack://application:,,,/Assets/default.gif"));

        /// <summary>
        /// Инициализирует новое окно администратора для указанного пользователя.
        /// </summary>
        /// <param name="currentUser">Объект пользователя с данными администратора.</param>
        public AdminWindow(User currentUser)
        {
            InitializeComponent();
            _currentUser = currentUser;
            InitializeUserInterface();
            LoadData();
        }

        /// <summary>
        /// Настраивает пользовательский интерфейс, отображая данные текущего администратора.
        /// Устанавливает имя, роль и изображение профиля.
        /// </summary>
        private void InitializeUserInterface()
        {
            FullNameText.Text = _currentUser.FullName;
            RoleText.Text = _currentUser.Role?.Name ?? "Неизвестно";
            ProfileImage.Source = LoadUserImage(_currentUser.Image);
        }

        /// <summary>
        /// Загружает изображение профиля пользователя или возвращает изображение по умолчанию.
        /// </summary>
        /// <param name="imageName">Имя файла изображения профиля.</param>
        /// <returns>Объект <see cref="BitmapImage"/> с изображением или значением по умолчанию.</returns>
        private BitmapImage LoadUserImage(string imageName)
        {
            try
            {
                if (!string.IsNullOrEmpty(imageName))
                {
                    return new BitmapImage(new Uri($"pack://application:,,,/Assets/{imageName}"));
                }
            }
            catch { }
            return _defaultImage;
        }

        /// <summary>
        /// Загружает данные для таблиц пользователей, ролей и истории входов.
        /// </summary>
        private void LoadData()
        {
            LoadUsersAsync();
            _ = LoadRolesAsync();
            LoadLoginHistory();
        }

        /// <summary>
        /// Асинхронно загружает список всех пользователей и отображает их в таблице.
        /// </summary>
        private async void LoadUsersAsync()
        {
            UsersDataGrid.ItemsSource = await DatabaseManager.RetrieveUsersAsync();
        }

        /// <summary>
        /// Асинхронно загружает список ролей (кроме роли пациента) и заполняет выпадающее меню.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию загрузки.</returns>
        private async Task LoadRolesAsync()
        {
            EmployeeRoleCombo.ItemsSource = await DatabaseManager.RetrieveRolesAsync();
        }

        /// <summary>
        /// Обновляет таблицу истории входов при изменении фильтров (логин, даты).
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, поле ввода или выбор даты).</param>
        /// <param name="e">Аргументы события.</param>
        private void FilterChanged(object sender, EventArgs e)
        {
            LoadFilteredHistory();
        }

        /// <summary>
        /// Асинхронно загружает полный список истории входов пользователей.
        /// </summary>
        private async void LoadLoginHistory()
        {
            LoginHistoryGrid.ItemsSource = await DatabaseManager.RetrieveSessionHistoryAsync();
        }

        /// <summary>
        /// Асинхронно загружает отфильтрованную историю входов по логину и диапазону дат.
        /// </summary>
        private async void LoadFilteredHistory()
        {
            string query = HistoryLoginSearchBox.Text.ToLower();
            DateTime? startDate = StartDatePicker.SelectedDate;
            DateTime? endDate = EndDatePicker.SelectedDate;

            LoginHistoryGrid.ItemsSource = await DatabaseManager.RetrieveFilteredSessionHistoryAsync(query, startDate, endDate);
        }

        /// <summary>
        /// Открывает диалоговое окно для выбора изображения профиля сотрудника.
        /// Отображает предварительный просмотр выбранного изображения.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SelectEmployeeImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets")
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedImagePath = dialog.FileName;
                ImagePathText.Text = Path.GetFileName(_selectedImagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_selectedImagePath);
                bitmap.EndInit();
                EmployeePreviewImage.Source = bitmap;
            }
        }

        /// <summary>
        /// Выполняет добавление нового сотрудника в базу данных после проверки данных.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void CreateEmployee_Click(object sender, RoutedEventArgs e)
        {
            EmployeeErrorText.Text = string.Empty;

            if (!(EmployeeRoleCombo.SelectedValue is int roleId))
            {
                EmployeeErrorText.Text = "Выберите должность.";
                return;
            }

            string fullName = EmployeeFullName.Text.Trim();
            string phone = EmployeePhone.Text.Trim();
            string email = EmployeeEmail.Text.Trim();
            string login = EmployeeLogin.Text.Trim();
            string password = EmployeePassword.Password;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(login) ||
                string.IsNullOrWhiteSpace(password))
            {
                EmployeeErrorText.Text = "Заполните все поля.";
                return;
            }

            if (!MailValidator.check_login(login) || !MailValidator.check_password(password) || !MailValidator.check_Mail(email))
            {
                EmployeeErrorText.Text = "Неверный логин, пароль или почта.";
                return;
            }

            if (await DatabaseManager.UserExistsAsync(login))
            {
                EmployeeErrorText.Text = "Пользователь с таким логином уже существует.";
                return;
            }

            var newUser = new User
            {
                FullName = fullName,
                Phone = phone,
                Email = email,
                Login = login,
                Password = _passwordHelper.HashPassword(password),
                RoleID = roleId,
                Image = ProcessEmployeeImage()
            };

            await DatabaseManager.AddUserAsync(newUser);

            MessageBox.Show("Сотрудник успешно добавлен!");
            ClearAddEmployeeFields();
        }

        /// <summary>
        /// Копирует выбранное изображение сотрудника в папку Assets и возвращает его имя.
        /// </summary>
        /// <returns>Имя скопированного файла или "default.png", если изображение не выбрано или произошла ошибка.</returns>
        private string ProcessEmployeeImage()
        {
            if (string.IsNullOrEmpty(_selectedImagePath))
                return "default.png";

            try
            {
                string root = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName;
                string assets = Path.Combine(root, "Assets");
                if (!Directory.Exists(assets)) Directory.CreateDirectory(assets);

                string fileName = $"user_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(_selectedImagePath)}";
                File.Copy(_selectedImagePath, Path.Combine(assets, fileName), true);
                return fileName;
            }
            catch
            {
                return "default.png";
            }
        }

        /// <summary>
        /// Очищает поля формы добавления нового сотрудника.
        /// </summary>
        private void ClearAddEmployeeFields()
        {
            EmployeeFullName.Text = "";
            EmployeePhone.Text = "";
            EmployeeEmail.Text = "";
            EmployeeLogin.Text = "";
            EmployeePassword.Password = "";
            EmployeeRoleCombo.SelectedIndex = -1;
            _selectedImagePath = null;
            EmployeePreviewImage.Source = _defaultImage;
            ImagePathText.Text = "";
        }

        /// <summary>
        /// Фильтрует список пользователей в таблице по введенному логину.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (поле ввода).</param>
        /// <param name="e">Аргументы события.</param>
        private async void LoginSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = LoginSearchBox.Text.ToLower();
            UsersDataGrid.ItemsSource = await DatabaseManager.FindUsersByLoginAsync(query);
        }

        /// <summary>
        /// Запускает процесс хеширования всех нехешированных паролей в базе данных.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void HashAllPasswords_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _passwordHelper.HashAllPlainPasswords();
                MessageBox.Show("Все пароли успешно захешированы!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при хешировании: {ex.Message}");
            }
        }

        /// <summary>
        /// Выполняет выход из текущей сессии и возвращает пользователя к окну авторизации.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.StopTimer();
            _ = SessionManager.LogoutAsync(this);
        }

        /// <summary>
        /// Завершает текущую сессию при закрытии окна.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            SessionManager.StopTimer();
            _ = DatabaseManager.CloseCurrentSessionAsync();
        }
    }
}
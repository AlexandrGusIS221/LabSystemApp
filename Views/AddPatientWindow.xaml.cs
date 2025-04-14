using LabSystemApp.Helpers;
using LabSystemApp.Scripts;
using MAIL_LIB;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно для регистрации нового пациента в системе медицинской лаборатории.
    /// Позволяет вводить личные данные, включая паспорт, полис и фото, а также сохраняет информацию в базе данных.
    /// </summary>
    public partial class AddPatientWindow : Window
    {
        /// <summary>
        /// Объект для хеширования паролей пациентов.
        /// </summary>
        private readonly PasswordHelper _passwordHelper = new PasswordHelper();

        /// <summary>
        /// Путь к выбранному изображению профиля пациента.
        /// </summary>
        private string _selectedImagePath;

        /// <summary>
        /// Инициализирует новое окно для регистрации пациента.
        /// </summary>
        public AddPatientWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Открывает диалоговое окно для выбора изображения профиля и отображает его предварительный просмотр.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Выберите фото профиля"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedImagePath = openFileDialog.FileName;
                SelectedImageText.Text = Path.GetFileName(_selectedImagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_selectedImagePath);
                bitmap.EndInit();
                PatientImagePreview.Source = bitmap;
            }
        }

        /// <summary>
        /// Выполняет регистрацию нового пациента после проверки введенных данных.
        /// Сохраняет данные в базе, хеширует пароль и копирует фото в папку Assets.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            string login = RegLogin.Text.Trim();
            string pass = RegPassword.Text.Trim();
            string email = RegMail.Text.Trim();
            string fullName = RegFullName.Text.Trim();
            string phone = RegPhone.Text.Trim();
            string passportNumber = RegPassportNumber.Text.Trim();
            string passportSeries = RegPassportSeries.Text.Trim();
            string policyNumber = RegPolicyNumber.Text.Trim();
            DateTime? birthDate = RegBirthDate.SelectedDate;

            // Проверка заполнения всех полей
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(passportNumber) ||
                string.IsNullOrWhiteSpace(passportSeries) || string.IsNullOrWhiteSpace(policyNumber) ||
                !birthDate.HasValue)
            {
                RegError.Text = "Заполните все поля.";
                return;
            }

            if (!MailValidator.check_login(login))
            {
                RegError.Text = "Некорректный логин.";
                return;
            }

            if (!MailValidator.check_password(pass))
            {
                RegError.Text = "Некорректный пароль.";
                return;
            }

            if (!MailValidator.check_Mail(email))
            {
                RegError.Text = "Некорректный email.";
                return;
            }

            if (birthDate.Value.Year < 1900 || birthDate.Value > DateTime.Now)
            {
                RegError.Text = "Некорректная дата рождения.";
                return;
            }

            // Проверка уникальности логина
            if (await DatabaseManager.UserExistsAsync(login))
            {
                RegError.Text = "Такой логин уже занят.";
                return;
            }

            string imageFileName = ProcessPatientImage();

            // Создание записи о пациенте
            var user = new User
            {
                Login = login,
                Password = _passwordHelper.HashPassword(pass),
                FullName = fullName,
                Email = email,
                Phone = phone,
                RoleID = 1, // Роль пациента по умолчанию
                Image = imageFileName,
                BirthDate = birthDate,
                PassportNumber = passportNumber,
                PassportSeries = passportSeries,
                PolicyNumber = policyNumber,
                PolicyTypeID = 1, // Тип полиса по умолчанию
                InsuranceCompanyID = 1 // Страховая компания по умолчанию
            };

            await DatabaseManager.AddUserAsync(user);

            MessageBox.Show("Регистрация успешна.");
            Close();
        }

        /// <summary>
        /// Обрабатывает выбранное изображение профиля, копируя его в папку Assets.
        /// </summary>
        /// <returns>Имя скопированного файла или "default.png", если изображение не выбрано или произошла ошибка.</returns>
        private string ProcessPatientImage()
        {
            if (string.IsNullOrEmpty(_selectedImagePath))
                return "default.png";

            try
            {
                string projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName;
                string assetsDir = Path.Combine(projectRoot, "Assets");

                if (!Directory.Exists(assetsDir))
                    Directory.CreateDirectory(assetsDir);

                string fileName = $"patient_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(_selectedImagePath)}";
                string destPath = Path.Combine(assetsDir, fileName);

                File.Copy(_selectedImagePath, destPath, true);
                return fileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении фото: {ex.Message}");
                return "default.png";
            }
        }
    }
}
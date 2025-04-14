using LabSystemApp.Helpers;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Data.Entity;
using LabSystemApp.Scripts;
using System.Threading.Tasks;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно для работы бухгалтера, обеспечивающее просмотр отчетов и создание счетов для страховых компаний.
    /// Позволяет фильтровать заказы по компании и датам, сохранять отчеты в файлы и управлять сессией.
    /// </summary>
    public partial class AccountantWindow : Window
    {
        /// <summary>
        /// Текущий пользователь (бухгалтер), вошедший в систему.
        /// </summary>
        private readonly User _currentUser;

        /// <summary>
        /// Инициализирует новое окно бухгалтера для указанного пользователя.
        /// </summary>
        /// <param name="currentUser">Объект пользователя с данными бухгалтера.</param>
        public AccountantWindow(User currentUser)
        {
            InitializeComponent();
            _currentUser = currentUser;

            InitializeUserInterface();
            _ = LoadCompaniesAsync();
            _ = LoadFilteredReportsAsync();
            LoadSavedReports();
        }

        /// <summary>
        /// Асинхронно загружает список страховых компаний и заполняет выпадающее меню.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию загрузки.</returns>
        private async Task LoadCompaniesAsync()
        {
            var companies = await DatabaseManager.RetrieveInsuranceCompaniesAsync();
            CompanyFilterCombo.ItemsSource = companies;
            CompanyFilterCombo.SelectedIndex = 0;
        }

        /// <summary>
        /// Асинхронно загружает список сохраненных отчетов для отображения в выпадающем меню.
        /// </summary>
        private async void LoadSavedReports()
        {
            ReportsCombo.ItemsSource = await DatabaseManager.RetrieveReportSummariesAsync();
        }

        /// <summary>
        /// Настраивает пользовательский интерфейс, отображая информацию о текущем пользователе.
        /// Устанавливает имя, роль и изображение профиля.
        /// </summary>
        private void InitializeUserInterface()
        {
            if (_currentUser == null)
            {
                MessageBox.Show("Ошибка: пользователь не определен.");
                return;
            }

            FullNameText.Text = _currentUser.FullName ?? "Неизвестно";
            RoleText.Text = _currentUser.Role?.Name ?? "Неизвестно";

            string imgName = string.IsNullOrEmpty(_currentUser.Image) ? "default.gif" : _currentUser.Image;
            try
            {
                ProfileImage.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{imgName}"));
            }
            catch
            {
                ProfileImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/default.gif"));
            }
        }

        /// <summary>
        /// Внутренний класс модели представления для отображения данных заказов в таблице отчетов.
        /// </summary>
        public class OrderReportViewModel
        {
            /// <summary>
            /// Уникальный идентификатор заказа.
            /// </summary>
            public int OrderID { get; set; }

            /// <summary>
            /// Пользователь (пациент), связанный с заказом.
            /// </summary>
            public User Patient { get; set; }

            /// <summary>
            /// Общая стоимость заказа в рублях.
            /// </summary>
            public decimal TotalPrice { get; set; }

            /// <summary>
            /// Дата и время создания заказа.
            /// </summary>
            public DateTime CreatedAt { get; set; }

            /// <summary>
            /// Полное имя пациента для отображения в таблице.
            /// </summary>
            public string PatientFullName => Patient?.FullName ?? "Неизвестно";
        }

        /// <summary>
        /// Асинхронно загружает заказы в таблицу с учетом фильтров по страховой компании и диапазону дат.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию загрузки данных.</returns>
        private async Task LoadFilteredReportsAsync()
        {
            if (CompanyFilterCombo.SelectedItem == null) return;

            var selectedCompany = CompanyFilterCombo.SelectedItem as InsuranceCompany;
            DateTime? startDate = StartDatePicker.SelectedDate;
            DateTime? endDate = EndDatePicker.SelectedDate;

            var orders = await DatabaseManager.RetrieveOrdersAsync(selectedCompany, startDate, endDate);

            var reportData = orders.Select(o => new OrderReportViewModel
            {
                OrderID = o.OrderID,
                Patient = o.User,
                TotalPrice = (decimal)(o.TotalPrice ?? 0),
                CreatedAt = (DateTime)o.CreatedAt
            }).ToList();

            ReportsTable.ItemsSource = reportData;
        }

        /// <summary>
        /// Обрабатывает изменение фильтров и обновляет данные в таблице отчетов.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, выпадающее меню или выбор даты).</param>
        /// <param name="e">Аргументы события.</param>
        private void FilterChanged(object sender, EventArgs e)
        {
            _ = LoadFilteredReportsAsync();
        }

        /// <summary>
        /// Создает бухгалтерский отчет на основе текущих данных таблицы и фильтров.
        /// Сохраняет отчет в базе данных и обновляет список отчетов.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void GenerateInvoiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (ReportsTable.ItemsSource == null || !ReportsTable.Items.OfType<OrderReportViewModel>().Any())
            {
                MessageBox.Show("Нет данных для создания счета.");
                return;
            }

            if (!(CompanyFilterCombo.SelectedItem is InsuranceCompany selectedCompany))
            {
                MessageBox.Show("Выберите страховую компанию.");
                return;
            }

            var totalSum = ReportsTable.Items.OfType<OrderReportViewModel>().Sum(r => r.TotalPrice);
            var report = new AccountantReport
            {
                InsuranceCompanyID = selectedCompany.InsuranceCompanyID,
                UserID = _currentUser.UserID,
                CreatedAt = DateTime.Now,
                PeriodStart = StartDatePicker.SelectedDate ?? new DateTime(2000, 1, 1),
                PeriodEnd = EndDatePicker.SelectedDate ?? DateTime.Today,
                TotalCost = totalSum
            };

            _ = DatabaseManager.AddAccountantReportAsync(report);

            MessageBox.Show($"Счет создан для {selectedCompany.Name} на сумму {totalSum:0.00} ₽");
            LoadSavedReports();
        }

        /// <summary>
        /// Сохраняет текущий отчет в текстовый файл, выбранный пользователем.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SaveReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (ReportsTable.ItemsSource == null || !ReportsTable.Items.OfType<OrderReportViewModel>().Any())
            {
                MessageBox.Show("Нет данных для сохранения.");
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var reportData = ReportsTable.Items.OfType<OrderReportViewModel>().ToList();
                var selectedCompany = CompanyFilterCombo.SelectedItem as InsuranceCompany;

                using (var writer = new StreamWriter(saveFileDialog.FileName))
                {
                    writer.WriteLine($"Отчет для компании: {selectedCompany?.Name}");
                    writer.WriteLine($"Период: с {StartDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "начала"} по {EndDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "сегодня"}");
                    writer.WriteLine("ID Заказа | Пациент | Общая стоимость | Дата создания");
                    writer.WriteLine(new string('-', 60));

                    foreach (var item in reportData)
                    {
                        writer.WriteLine($"{item.OrderID} | {item.PatientFullName} | {item.TotalPrice:0.00} ₽ | {item.CreatedAt:yyyy-MM-dd HH:mm}");
                    }

                    var totalSum = reportData.Sum(r => r.TotalPrice);
                    writer.WriteLine(new string('-', 60));
                    writer.WriteLine($"Итого: {totalSum:0.00} ₽");
                }

                MessageBox.Show($"Отчет сохранен: {saveFileDialog.FileName}");
            }
        }

        /// <summary>
        /// Выполняет выход из текущей сессии и возвращает пользователя к окну авторизации.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.StopTimer();
            _ = SessionManager.LogoutAsync(this);
        }

        /// <summary>
        /// Завершает сессию пользователя при закрытии окна.
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
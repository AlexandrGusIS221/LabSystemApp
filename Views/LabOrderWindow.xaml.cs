using LabSystemApp.Helpers;
using LabSystemApp.Scripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно для оформления лабораторных заказов, выбора услуг и создания штрихкодов для пробирок.
    /// Позволяет добавлять новых пациентов и сохранять заказы в базе данных.
    /// </summary>
    public partial class LabOrderWindow : Window
    {
        /// <summary>
        /// Текущий пользователь, вошедший в систему.
        /// </summary>
        private readonly User _currentUser;

        /// <summary>
        /// Изображение профиля по умолчанию, используемое при отсутствии фото.
        /// </summary>
        private readonly BitmapImage _defaultImage = new BitmapImage(
            new Uri("pack://application:,,,/Assets/default.gif"));

        /// <summary>
        /// Инициализирует новое окно для оформления лабораторных заказов.
        /// </summary>
        /// <param name="currentUser">Объект пользователя, выполняющего оформление заказов.</param>
        public LabOrderWindow(User currentUser)
        {
            InitializeComponent();
            _currentUser = currentUser;

            InitializeUserInterface();
            _ = LoadData();

            SessionManager.TimeUpdated += remaining =>
            {
                TimerTextBlock.Text = remaining.ToString(@"hh\:mm\:ss");
            };
            SessionManager.StartSessionTimer(this);
        }

        /// <summary>
        /// Настраивает пользовательский интерфейс, отображая данные текущего пользователя.
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
            catch
            {
                // Ошибка логируется при необходимости
            }
            return _defaultImage;
        }

        /// <summary>
        /// Асинхронно загружает данные для выпадающих списков и текстовых полей.
        /// </summary>
        private async Task LoadData()
        {
            PatientCombo.ItemsSource = await DatabaseManager.LoadPatients();
            ServicesListBox.ItemsSource = await DatabaseManager.LoadServices();
            ServicesListBox.SelectionChanged += RecalculateTotalPrice;
            BiomaterialCodeInput.Text = (await DatabaseManager.GetLastOrderId() + 1).ToString();
        }

        /// <summary>
        /// Открывает окно для регистрации нового пациента.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void AddNewPatient_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddPatientWindow();
            _ = addWindow.ShowDialog();
            _ = LoadData();
        }

        /// <summary>
        /// Пересчитывает и отображает общую стоимость выбранных услуг.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, список услуг).</param>
        /// <param name="e">Аргументы события.</param>
        private void RecalculateTotalPrice(object sender, SelectionChangedEventArgs e)
        {
            decimal sum = (decimal)ServicesListBox.SelectedItems.Cast<Service>().Sum(svc => svc.Price ?? 0);
            TotalPriceText.Text = $"{sum:0.00} ₽";
        }

        /// <summary>
        /// Сохраняет заказ в базе данных и генерирует штрихкоды для пробирок в формате PDF.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SubmitOrder_Click(object sender, RoutedEventArgs e)
        {
            if (!(PatientCombo.SelectedItem is User selectedPatient))
            {
                _ = MessageBox.Show("Выберите пациента.");
                return;
            }

            if (ServicesListBox.SelectedItems.Count == 0)
            {
                ErrorTextBlock.Text = "Выберите услугу.";
                return;
            }

            try
            {
                var order = new Order
                {
                    UserID = selectedPatient.UserID,
                    StatusID = 1,
                    CreatedAt = DateTime.Now,
                    ExecutionTimeDays = 0,
                    TotalPrice = 0
                };

                var orderServices = new List<OrderService>();
                decimal totalPrice = 0;

                foreach (Service svc in ServicesListBox.SelectedItems)
                {
                    string baseText = BiomaterialCodeInput.Text.Trim();

                    if (!int.TryParse(baseText, out int orderBaseId))
                    {
                        _ = MessageBox.Show("Некорректный код пробирки. Введите число.");
                        return;
                    }

                    string barcode = Barcode.Barcode.GenerateRandomCode(DateTime.Now, orderBaseId);

                    try
                    {
                        var barcodeImage = Barcode.Barcode.GenerateBarcode(barcode);
                        string pdfFileName = $"barcode_{barcode}.pdf";
                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LabBarcodes");
                        _ = Directory.CreateDirectory(folder);
                        string fullPath = Path.Combine(folder, pdfFileName);
                        Barcode.Barcode.SaveBarcodeToPdf(barcodeImage, barcode, fullPath);
                    }
                    catch (Exception ex)
                    {
                        _ = MessageBox.Show($"Ошибка при сохранении штрихкода: {ex.Message}");
                        return;
                    }

                    orderServices.Add(new OrderService
                    {
                        ServiceID = svc.ServiceID,
                        StatusID = 1,
                        Barcode = barcode,
                        Result = null
                    });

                    totalPrice += (decimal?)svc.Price ?? 0;
                }

                _ = DatabaseManager.SaveOrder(order, orderServices);
                _ = DatabaseManager.UpdateOrderTotalPrice(order.OrderID, totalPrice);

                _ = MessageBox.Show("Заказ сохранён. Штрихкоды созданы в PDF.");
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Ошибка при сохранении: {ex.Message}");
            }
        }

        /// <summary>
        /// Выполняет выход из текущей сессии и возвращает пользователя к окну авторизации.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.StopTimer();
            await SessionManager.LogoutAsync(this);
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
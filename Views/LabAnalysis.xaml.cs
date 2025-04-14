using LabSystemApp.Helpers;
using LabSystemApp.Scripts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static LabSystemApp.Helpers.Classes;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно для обработки лабораторных анализов, управления заказами услуг и получения результатов от анализаторов.
    /// Отображает таблицу заказов, прогресс выполнения и позволяет отправлять услуги на анализ.
    /// </summary>
    public partial class LabAnalysis : Window
    {
        /// <summary>
        /// Текущий пользователь, вошедший в систему.
        /// </summary>
        private readonly User _currentUser;

        /// <summary>
        /// Идентификатор текущего пользователя.
        /// </summary>
        private readonly int _userId;

        /// <summary>
        /// Словарь для хранения прогресса выполнения услуг по их идентификаторам.
        /// </summary>
        private readonly Dictionary<int, double> _progressValues = new Dictionary<int, double>();

        /// <summary>
        /// Таймер для обновления прогресса выполнения услуг.
        /// </summary>
        private readonly DispatcherTimer _progressUpdateTimer = new DispatcherTimer();

        /// <summary>
        /// Инициализирует новое окно для обработки лабораторных анализов.
        /// </summary>
        /// <param name="currentUser">Объект пользователя, выполняющего анализ.</param>
        public LabAnalysis(User currentUser)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _userId = currentUser.UserID;

            InitializeUserInterface();
            _ = UpdateOrderTableAsync();

            _progressUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            _progressUpdateTimer.Tick += ProgressUpdateTimer_Tick;
            _progressUpdateTimer.Start();

            SessionManager.TimeUpdated += remainingTime =>
            {
                TimerTextBlock.Text = remainingTime.ToString(@"hh\:mm\:ss");
            };
            SessionManager.StartSessionTimer(this);
        }

        /// <summary>
        /// Настраивает пользовательский интерфейс, отображая данные пользователя и инициализируя элементы управления.
        /// </summary>
        private void InitializeUserInterface()
        {
            if (_currentUser == null)
            {
                _ = MessageBox.Show("Ошибка: пользователь не определен.");
                return;
            }

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

            _ = DatabaseManager.SetAllAnalyzersAvailableAsync();

            getAnalysis.IsEnabled = false;
        }

        /// <summary>
        /// Асинхронно обновляет таблицу заказов услуг, загружая данные из базы данных.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию обновления таблицы.</returns>
        private async Task UpdateOrderTableAsync()
        {
            try
            {
                var selectedId = (ordersTable.SelectedItem as OrderServiceViewModel)?.OrderService.OrderServiceID;

                // Получаем данные из базы с фильтром, как в рабочем коде
                List<OrderService> services = await DatabaseManager.GetOrderServicesAsync();

                // Преобразуем данные в ViewModel
                var viewModels = new List<OrderServiceViewModel>();
                foreach (var os in services)
                {
                    double progress = 0;
                    bool isProcessing = os.StatusID == 2;

                    if (_progressValues.ContainsKey(os.OrderServiceID))
                    {
                        progress = _progressValues[os.OrderServiceID];
                        isProcessing = progress < 100;
                    }
                    else if (os.StatusID == 3)
                    {
                        progress = 100; // Для выполненных услуг устанавливаем прогресс 100
                    }

                    viewModels.Add(new OrderServiceViewModel
                    {
                        OrderService = os,
                        Result = os.Result ?? "Ожидается",
                        IsProcessing = isProcessing,
                        Progress = progress,
                        StatusName = await DatabaseManager.GetStatusNameAsync(os.StatusID) ?? "Неизвестно",
                        PatientFullName = os.Order?.User?.FullName ?? "Неизвестно"
                    });
                }

                // Обновляем источник данных таблицы
                ordersTable.ItemsSource = viewModels;

                // Восстанавливаем выбранный элемент
                if (selectedId.HasValue)
                {
                    var newSelectedItem = viewModels.FirstOrDefault(x => x.OrderService.OrderServiceID == selectedId.Value);
                    if (newSelectedItem != null)
                    {
                        ordersTable.SelectedItem = newSelectedItem;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при обновлении таблицы заказов: {ex.Message}");
            }
        }

        /// <summary>
        /// Обновляет прогресс выполнения услуг в таблице по тику таймера.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (таймер).</param>
        /// <param name="e">Аргументы события.</param>
        private async void ProgressUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!(ordersTable.ItemsSource is List<OrderServiceViewModel> viewModels)) return;

            foreach (var kvp in _progressValues.ToList())
            {
                int orderServiceId = kvp.Key;
                double progress = kvp.Value;
                var item = viewModels.FirstOrDefault(vm => vm.OrderService.OrderServiceID == orderServiceId);

                if (item != null && item.IsProcessing)
                {
                    var service = await DatabaseManager.GetOrderServiceByIdAsync(orderServiceId);
                    if (service == null) continue;

                    double executionTime = 5 * (double)(service.Service.ExecutionTime > 0 ? service.Service.ExecutionTime : 30);
                    double step = 100.0 / executionTime;
                    progress += step;

                    if (progress >= 100)
                    {
                        progress = 100;
                        await DatabaseManager.UpdateOrderServiceStatusAsync(service.OrderServiceID, 3);
                        item.IsProcessing = false;
                        item.Progress = 100;
                        item.StatusName = "Выполнен";
                        _progressValues[orderServiceId] = 100; // Сохраняем прогресс
                        getAnalysis.IsEnabled = true;
                        ordersTable.Items.Refresh();
                    }
                    else
                    {
                        _progressValues[orderServiceId] = progress;
                        item.Progress = progress;
                        ordersTable.Items.Refresh();
                    }
                }
            }
        }

        /// <summary>
        /// Отправляет выбранную услугу на анализ, взаимодействуя с анализатором через API.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void PostAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (!(ordersTable.SelectedItem is OrderServiceViewModel selectedServiceVM))
            {
                _ = MessageBox.Show("Услуга не выбрана.");
                return;
            }

            var selectedService = selectedServiceVM.OrderService;
            int analyzerId = selectedService.Service.AnalyzerID ?? 1;

            var availableAnalyzer = await DatabaseManager.GetAvailableAnalyzerAsync(analyzerId);
            if (availableAnalyzer == null)
            {
                _ = MessageBox.Show("Нет доступных анализаторов.");
                return;
            }
            analyzerId = availableAnalyzer.AnalyzerID;

            var analyzer = await DatabaseManager.GetAnalyzerByIdAsync(analyzerId);
            if (analyzer == null || !analyzer.isAvaible)
            {
                _ = MessageBox.Show("Выбранный анализатор недоступен.");
                return;
            }

            try
            {
                using var httpClient = new HttpClient();
                string requestUrl = $"http://localhost:5000/api/analyzer/{analyzer.Name}";

                var payload = new
                {
                    patient = selectedService.OrderID.ToString(),
                    services = new[] { new { serviceCode = selectedService.ServiceID } }
                };

                string json = new JavaScriptSerializer().Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(requestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    await DatabaseManager.UpdateAnalyzerAndOrderServiceAsync(analyzer.AnalyzerID, selectedService.OrderServiceID);
                    await DatabaseManager.AddAnalyzerWorkAsync(selectedService.OrderServiceID, analyzer.AnalyzerID, _userId);

                    selectedServiceVM.Progress = 0;
                    selectedServiceVM.IsProcessing = true;
                    _progressValues[selectedService.OrderServiceID] = 0;
                    selectedServiceVM.StatusName = await DatabaseManager.GetStatusNameAsync(selectedService.StatusID);

                    await UpdateOrderTableAsync();
                }
                else
                {
                    _ = MessageBox.Show($"Ошибка при отправке: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Ошибка при отправке: {ex.Message}");
                Debug.WriteLine($"Ошибка в PostAnalysis_Click: {ex}");
            }
        }

        /// <summary>
        /// Ответ анализатора, содержащий результаты обработки.
        /// </summary>
        private class AnalyzerResponse
        {
            /// <summary>
            /// Идентификатор пациента (заказа).
            /// </summary>
            public string Patient { get; set; }

            /// <summary>
            /// Массив результатов услуг.
            /// </summary>
            public ServiceResult[] Services { get; set; }
        }

        /// <summary>
        /// Результат выполнения отдельной услуги.
        /// </summary>
        private class ServiceResult
        {
            /// <summary>
            /// Код услуги.
            /// </summary>
            public int ServiceCode { get; set; }

            /// <summary>
            /// Результат анализа услуги.
            /// </summary>
            public string Result { get; set; }
        }

        /// <summary>
        /// Получает результаты анализа для выбранной услуги и обновляет данные в базе.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (например, кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void GetAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (!(ordersTable.SelectedItem is OrderServiceViewModel selectedServiceVM))
            {
                _ = MessageBox.Show("Услуга не выбрана.");
                return;
            }

            var selectedService = selectedServiceVM.OrderService;
            int analyzerId = selectedService.Service.AnalyzerID ?? 1;
            if (analyzerId == 3)
            {
                var work = await DatabaseManager.GetAnalyzerByIdAsync(selectedService.OrderServiceID);
                analyzerId = work?.AnalyzerID ?? 1;
            }

            var analyzer = await DatabaseManager.GetAnalyzerByIdAsync(analyzerId);
            if (analyzer == null)
            {
                _ = MessageBox.Show("Анализатор не найден.");
                return;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create($"http://localhost:5000/api/analyzer/{analyzer.Name}");
                request.ContentType = "application/json";
                request.Method = "GET";

                using var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK) return;

                using var reader = new StreamReader(response.GetResponseStream());
                string json = reader.ReadToEnd();

                var data = new JavaScriptSerializer().Deserialize<AnalyzerResponse>(json);
                if (string.IsNullOrEmpty(data.Patient))
                {
                    _ = MessageBox.Show("В ответе отсутствует orderID.");
                    return;
                }

                int orderId = int.Parse(data.Patient);
                var result = data.Services.FirstOrDefault(s => s.ServiceCode == selectedService.ServiceID);
                if (result == null)
                {
                    _ = MessageBox.Show($"Результат для услуги {selectedService.ServiceID} не найден.");
                    return;
                }

                bool approve = true;
                string serviceName = selectedService.Service.Nme;
                string resultText = result.Result;

                if (double.TryParse(resultText, out double numericResult))
                {
                    if (double.TryParse(selectedService.Service.NormalRangeStart, out double min) &&
                        double.TryParse(selectedService.Service.NormalRangeEnd, out double max))
                    {
                        if (numericResult < min || numericResult > max)
                        {
                            approve = MessageBox.Show(
                                $"Результат услуги «{serviceName}» вне допустимого диапазона ({min} - {max}): {numericResult}\nПодтвердить результат?",
                                "Аномалия",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning
                            ) == MessageBoxResult.Yes;
                        }
                    }
                }
                else
                {
                    approve = MessageBox.Show($"Результат услуги «{serviceName}»: {resultText}\nПодтвердить результат?",
                        "Подтверждение результата", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                }

                if (approve)
                {
                    selectedService.Result = resultText;
                    selectedService.StatusID = 3;

                    var order = selectedService.Order;
                    order.ExecutionTimeDays = (int)(DateTime.Now - order.CreatedAt)?.TotalDays;

                    await DatabaseManager.AddAnalyzerWorkAsync(selectedService.OrderServiceID, analyzer.AnalyzerID, _userId);

                    selectedServiceVM.Result = resultText;
                    selectedServiceVM.StatusName = "Выполнен";
                    selectedServiceVM.IsProcessing = false;
                    selectedServiceVM.Progress = 100;

                    bool allDone = await DatabaseManager.AreAllOrderServicesCompletedAsync(orderId);

                    if (allDone)
                    {
                        order.StatusID = 3;
                        _ = MessageBox.Show($"Все услуги в заказе №{orderId} завершены.");
                    }

                    // Удаляем прогресс только после подтверждения
                    if (_progressValues.ContainsKey(selectedService.OrderServiceID))
                    {
                        _progressValues.Remove(selectedService.OrderServiceID);
                    }
                }
                else
                {
                    // Возвращаем услугу на повторный анализ
                    selectedService.StatusID = 1;
                    selectedService.Result = null; // Очищаем предыдущий результат
                    await DatabaseManager.UpdateOrderServiceStatusAsync(selectedService.OrderServiceID, 1); // Обновляем статус в базе
                    selectedServiceVM.StatusName = "Повторная поаытка";
                    selectedServiceVM.Progress = 0;
                    selectedServiceVM.IsProcessing = false;
                    selectedServiceVM.Result = "Ожидается";

                    // Очищаем прогресс для повторного анализа
                    if (_progressValues.ContainsKey(selectedService.OrderServiceID))
                    {
                        _progressValues.Remove(selectedService.OrderServiceID);
                    }
                }

                await DatabaseManager.SetAnalyzerAvailableAsync(analyzerId);
                await UpdateOrderTableAsync();
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show($"Ошибка при получении результатов: {ex.Message}");
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
        /// Завершает сессию и останавливает таймеры при закрытии окна.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            _progressUpdateTimer.Stop();
            SessionManager.StopTimer();
            _ = DatabaseManager.CloseCurrentSessionAsync();
        }
    }
}
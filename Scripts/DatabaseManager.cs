using ScottPlot.AxisPanels.Experimental;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows;

namespace LabSystemApp.Scripts
{
    /// <summary>
    /// Статический класс для управления взаимодействием с базой данных медицинской лаборатории.
    /// Предоставляет методы для работы с сессиями пользователей, данными пользователей, заказами, анализаторами и отчетами.
    /// </summary>
    public static class DatabaseManager
    {
        /// <summary>
        /// Хранит текущую активную сессию пользователя.
        /// </summary>
        private static SessionHistory _currentSession;

        // --- Управление сессиями ---

        /// <summary>
        /// Асинхронно регистрирует новую сессию пользователя в базе данных.
        /// </summary>
        /// <param name="session">Объект сессии, содержащий данные для регистрации.</param>
        /// <exception cref="ArgumentNullException">Вызывается, если параметр <paramref name="session"/> равен null.</exception>
        public static async Task RecordSessionAsync(SessionHistory session)
        {
            _currentSession = session ?? throw new ArgumentNullException(nameof(session));

            using var db = new MedLabDBEntities();
            try
            {
                db.SessionHistories.Add(session);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DisplayError($"Ошибка при регистрации сессии: {ex.Message}");
            }
        }

        /// <summary>
        /// Асинхронно завершает текущую сессию пользователя, устанавливая время выхода.
        /// Если сессия не существует или уже завершена, метод не выполняет изменений.
        /// </summary>
        public static async Task CloseCurrentSessionAsync()
        {
            if (_currentSession?.SessionHistoryID == null)
                return;

            using var db = new MedLabDBEntities();
            var session = await db.SessionHistories
                .FirstOrDefaultAsync(s => s.SessionHistoryID == _currentSession.SessionHistoryID)
                .ConfigureAwait(false);

            if (session?.LogoutTime == null)
            {
                session.LogoutTime = DateTime.UtcNow;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            _currentSession = null;
        }

        /// <summary>
        /// Асинхронно завершает все незакрытые сессии указанного пользователя, кроме текущей.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя, чьи сессии необходимо закрыть.</param>
        /// <exception cref="ArgumentException">Вызывается, если <paramref name="userId"/> меньше или равен нулю.</exception>
        public static async Task ClosePreviousSessionsAsync(int userId)
        {
            if (userId <= 0)
                throw new ArgumentException("Идентификатор пользователя должен быть положительным числом.", nameof(userId));

            using var db = new MedLabDBEntities();
            await db.SessionHistories
                .Where(s => s.UserID == userId && s.LogoutTime == null && s.SessionHistoryID != _currentSession.SessionHistoryID)
                .ForEachAsync(s => s.LogoutTime = DateTime.UtcNow)
                .ConfigureAwait(false);

            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // --- Работа с пользователями ---

        /// <summary>
        /// Асинхронно ищет пользователя по его логину.
        /// </summary>
        /// <param name="login">Логин пользователя для поиска.</param>
        /// <returns>Объект пользователя с данными роли или null, если пользователь не найден.</returns>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="login"/> пустой или null.</exception>
        public static async Task<User> FindUserByLoginAsync(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                throw new ArgumentNullException(nameof(login));

            using var db = new MedLabDBEntities();
            return await db.Users
                .Include(u => u.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Login == login)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно получает список пользователей, чьи логины содержат указанную подстроку.
        /// </summary>
        /// <param name="login">Подстрока для поиска в логинах пользователей (поиск без учета регистра).</param>
        /// <returns>Список пользователей, соответствующих критерию поиска.</returns>
        public static async Task<List<User>> FindUsersByLoginAsync(string login)
        {
            using var db = new MedLabDBEntities();
            if (string.IsNullOrEmpty(login))
            {
                return await db.Users
                .ToListAsync()
                .ConfigureAwait(false);
            }

            return await db.Users
                .Include(u => u.Role)
                .Where(u => u.Login.ToLower().Contains(login.ToLower()))
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно проверяет, существует ли пользователь с указанным логином.
        /// </summary>
        /// <param name="login">Логин пользователя для проверки.</param>
        /// <returns>True, если пользователь с таким логином существует; иначе false.</returns>
        public static async Task<bool> UserExistsAsync(string login)
        {
            using var db = new MedLabDBEntities();
            return await db.Users
                .AsNoTracking()
                .AnyAsync(u => u.Login == login)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно добавляет нового пользователя в базу данных.
        /// </summary>
        /// <param name="user">Объект пользователя для добавления.</param>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="user"/> равен null.</exception>
        public static async Task AddUserAsync(User user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            using var db = new MedLabDBEntities();
            db.Users.Add(user);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // --- Получение списков данных ---

        /// <summary>
        /// Асинхронно получает полный список истории сессий пользователей.
        /// </summary>
        /// <returns>Список объектов истории сессий, отсортированный по времени входа в порядке убывания.</returns>
        public static async Task<List<SessionHistory>> RetrieveSessionHistoryAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.SessionHistories
                .Include(x => x.User)
                .AsNoTracking()
                .OrderByDescending(sh => sh.LoginTime)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно получает отфильтрованный список истории входов пользователей по логину и диапазону дат.
        /// Если логин не указан, применяет только фильтры по датам, если они заданы.
        /// </summary>
        /// <param name="loginFilter">Строка для фильтрации по логину пользователя (может быть пустой).</param>
        /// <param name="startDate">Начальная дата для фильтрации (может быть null).</param>
        /// <param name="endDate">Конечная дата для фильтрации (может быть null).</param>
        /// <returns>Задача, возвращающая список объектов <see cref="SessionHistory"/> с учётом фильтров.</returns>
        public static async Task<List<SessionHistory>> RetrieveFilteredSessionHistoryAsync(string loginFilter, DateTime? startDate, DateTime? endDate)
        {
            using var db = new MedLabDBEntities();
            var query = db.SessionHistories.Include(h => h.User).AsQueryable();

            if (!string.IsNullOrEmpty(loginFilter))
            {
                query = query.Where(h => h.User.Login.ToLower().Contains(loginFilter.ToLower()));
            }

            if (startDate.HasValue)
            {
                query = query.Where(h => h.LoginTime >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(h => h.LoginTime <= endDate.Value);
            }

            return await query.OrderByDescending(x => x.LoginTime).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно получает список всех пользователей системы.
        /// </summary>
        /// <returns>Список объектов пользователей с данными об их ролях.</returns>
        public static async Task<List<User>> RetrieveUsersAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.Users
                .Include(x => x.Role)
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно получает список всех ролей, кроме роли с идентификатором 1.
        /// </summary>
        /// <returns>Список объектов ролей.</returns>
        public static async Task<List<Role>> RetrieveRolesAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.Roles
                .Where(x => x.RoleID != 1)
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно получает список всех страховых компаний.
        /// </summary>
        /// <returns>Список объектов страховых компаний.</returns>
        public static async Task<List<InsuranceCompany>> RetrieveInsuranceCompaniesAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.InsuranceCompanies
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно получает список всех анализаторов.
        /// </summary>
        /// <returns>Список объектов анализаторов.</returns>
        public static async Task<List<Analyzer>> GetAnalyzersListAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.Analyzers.ToListAsync();
        }

        /// <summary>
        /// Вспомогательный класс для представления краткой информации об отчете бухгалтера.
        /// </summary>
        public class ReportSummary
        {
            /// <summary>
            /// Идентификатор отчета.
            /// </summary>
            public int ReportID { get; set; }

            /// <summary>
            /// Описание отчета, включая дату, страховую компанию и сумму.
            /// </summary>
            public string ReportDescription { get; set; }
        }

        /// <summary>
        /// Асинхронно получает краткую информацию обо всех отчетах бухгалтера.
        /// </summary>
        /// <returns>Список объектов с краткой информацией об отчетах.</returns>
        public static async Task<List<ReportSummary>> RetrieveReportSummariesAsync()
        {
            using var db = new MedLabDBEntities();
            try
            {
                // Загружаем данные из базы без форматирования
                var reports = await db.AccountantReports
                    .Include(r => r.InsuranceCompany)
                    .Select(r => new
                    {
                        r.ReportID,
                        r.CreatedAt,
                        InsuranceCompanyName = r.InsuranceCompany.Name,
                        r.TotalCost
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Форматируем данные в памяти
                var summaries = reports.Select(r => new ReportSummary
                {
                    ReportID = r.ReportID,
                    ReportDescription = $"Отчет #{r.ReportID} от {r.CreatedAt:yyyy-MM-dd} для {r.InsuranceCompanyName} ({r.TotalCost:0.00} ₽)"
                }).ToList();

                return summaries;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при получении отчетов: {ex.Message}");
                throw; // Или обработать ошибку в зависимости от требований
            }
        }

        /// <summary>
        /// Асинхронно получает список заказов, отфильтрованных по страховой компании и диапазону дат.
        /// </summary>
        /// <param name="insuranceCompany">Объект страховой компании для фильтрации.</param>
        /// <param name="startDate">Начальная дата для фильтрации заказов (может быть null).</param>
        /// <param name="endDate">Конечная дата для фильтрации заказов (может быть null).</param>
        /// <returns>Список объектов заказов, соответствующих критериям.</returns>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="insuranceCompany"/> равен null.</exception>
        public static async Task<List<Order>> RetrieveOrdersAsync(InsuranceCompany insuranceCompany, DateTime? startDate, DateTime? endDate)
        {
            if (insuranceCompany == null)
                throw new ArgumentNullException(nameof(insuranceCompany));

            using var db = new MedLabDBEntities();
            return await db.Orders
                .Include(o => o.User)
                .Where(o =>
                    o.User.InsuranceCompanyID == insuranceCompany.InsuranceCompanyID &&
                    (!startDate.HasValue || o.CreatedAt >= startDate.Value) &&
                    (!endDate.HasValue || o.CreatedAt <= endDate.Value))
                .ToListAsync()
                .ConfigureAwait(false);
        }

        // --- Добавление данных ---

        /// <summary>
        /// Асинхронно добавляет новый отчет бухгалтера в базу данных.
        /// </summary>
        /// <param name="report">Объект отчета бухгалтера для добавления.</param>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="report"/> равен null.</exception>
        public static async Task AddAccountantReportAsync(AccountantReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            using var db = new MedLabDBEntities();
            db.AccountantReports.Add(report);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        // --- Вспомогательные методы ---

        /// <summary>
        /// Отображает сообщение об ошибке в пользовательском интерфейсе с помощью диалогового окна.
        /// </summary>
        /// <param name="message">Текст сообщения об ошибке для отображения.</param>
        private static void DisplayError(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
        }

        /// <summary>
        /// Асинхронно устанавливает флаг доступности всех анализаторов в значение true.
        /// </summary>
        public static async Task SetAllAnalyzersAvailableAsync()
        {
            using var db = new MedLabDBEntities();
            await db.Analyzers
                .ForEachAsync(analyzer => analyzer.isAvaible = true);
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Асинхронно получает список услуг заказа, находящихся в статусе выполнения (StatusID = 1 или 2).
        /// </summary>
        /// <returns>Список объектов услуг заказа с данными о заказе, пользователе, услуге и анализаторе.</returns>
        public static async Task<List<OrderService>> GetOrderServicesAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.OrderServices
                .Include("Order.User")
                .Include("Service")
                .Include("Service.Analyzer")
                .Include("AnalyzerWorks")
                .Where(x => x.StatusID == 1 || x.StatusID == 2)
                .OrderBy(x => x.Order.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Асинхронно получает словарь статусов, где ключ — идентификатор статуса, а значение — его название.
        /// </summary>
        /// <returns>Словарь с парами идентификатор-название для всех статусов.</returns>
        public static async Task<Dictionary<int, string>> GetStatusesAsync()
        {
            using var db = new MedLabDBEntities();
            return await db.Statuses
                .Select(s => new { s.StatusID, s.Name })
                .ToDictionaryAsync(s => s.StatusID, s => s.Name);
        }

        /// <summary>
        /// Асинхронно получает услугу заказа по её идентификатору.
        /// </summary>
        /// <param name="orderServiceId">Идентификатор услуги заказа.</param>
        /// <returns>Объект услуги заказа с данными об услуге или null, если услуга не найдена.</returns>
        public static async Task<OrderService> GetOrderServiceByIdAsync(int orderServiceId)
        {
            using var db = new MedLabDBEntities();
            return await db.OrderServices
                .Include("Service")
                .FirstOrDefaultAsync(os => os.OrderServiceID == orderServiceId);
        }

        /// <summary>
        /// Асинхронно обновляет статус услуги заказа.
        /// </summary>
        /// <param name="orderServiceId">Идентификатор услуги заказа.</param>
        /// <param name="statusId">Новый идентификатор статуса.</param>
        public static async Task UpdateOrderServiceStatusAsync(int orderServiceId, int statusId)
        {
            using var db = new MedLabDBEntities();
            var service = await db.OrderServices
                .FirstOrDefaultAsync(os => os.OrderServiceID == orderServiceId);
            if (service != null)
            {
                service.StatusID = statusId;
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Асинхронно получает доступный анализатор по его идентификатору.
        /// </summary>
        /// <param name="analyzerId">Идентификатор анализатора.</param>
        /// <returns>Объект анализатора или null, если доступный анализатор не найден.</returns>
        public static async Task<Analyzer> GetAvailableAnalyzerAsync(int analyzerId)
        {
            using var db = new MedLabDBEntities();
            return await db.Analyzers
                .FirstOrDefaultAsync(a => a.AnalyzerID == analyzerId && a.isAvaible);
        }

        /// <summary>
        /// Асинхронно обновляет статус анализатора (устанавливает isAvaible в false) и статус услуги заказа (устанавливает StatusID=2).
        /// </summary>
        /// <param name="analyzerId">Идентификатор анализатора.</param>
        /// <param name="orderServiceId">Идентификатор услуги заказа.</param>
        public static async Task UpdateAnalyzerAndOrderServiceAsync(int analyzerId, int orderServiceId)
        {
            using var db = new MedLabDBEntities();
            var analyzer = await db.Analyzers
                .FirstOrDefaultAsync(a => a.AnalyzerID == analyzerId);
            var service = await db.OrderServices
                .FirstOrDefaultAsync(os => os.OrderServiceID == orderServiceId);

            if (analyzer != null && service != null)
            {
                analyzer.isAvaible = false;
                service.StatusID = 2;
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Асинхронно добавляет запись о работе анализатора в базу данных.
        /// </summary>
        /// <param name="orderServiceId">Идентификатор услуги заказа.</param>
        /// <param name="analyzerId">Идентификатор анализатора.</param>
        /// <param name="userId">Идентификатор пользователя, выполнившего операцию.</param>
        public static async Task AddAnalyzerWorkAsync(int orderServiceId, int analyzerId, int userId)
        {
            using var db = new MedLabDBEntities();
            db.AnalyzerWorks.Add(new AnalyzerWork
            {
                OrderServiceID = orderServiceId,
                AnalyzerID = analyzerId,
                UserID = userId,
                PerformedAt = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Асинхронно получает название статуса по его идентификатору.
        /// </summary>
        /// <param name="statusId">Идентификатор статуса (может быть null).</param>
        /// <returns>Название статуса или "Неизвестно", если статус не найден или не указан.</returns>
        public static async Task<string> GetStatusNameAsync(int? statusId)
        {
            if (statusId == null) return "Неизвестно";
            using var db = new MedLabDBEntities();
            return await db.Statuses
                .Where(s => s.StatusID == statusId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Асинхронно получает последнюю запись о работе анализатора для указанной услуги заказа.
        /// </summary>
        /// <param name="orderServiceId">Идентификатор услуги заказа.</param>
        /// <returns>Объект записи о работе анализатора или null, если запись не найдена.</returns>
        public static async Task<AnalyzerWork> GetLatestAnalyzerWorkAsync(int orderServiceId)
        {
            using var db = new MedLabDBEntities();
            return await db.AnalyzerWorks
                .Where(aw => aw.OrderServiceID == orderServiceId)
                .OrderByDescending(aw => aw.PerformedAt)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Асинхронно получает анализатор по его идентификатору.
        /// </summary>
        /// <param name="analyzerId">Идентификатор анализатора.</param>
        /// <returns>Объект анализатора или null, если анализатор не найден.</returns>
        public static async Task<Analyzer> GetAnalyzerByIdAsync(int analyzerId)
        {
            using var db = new MedLabDBEntities();
            return await db.Analyzers
                .FirstOrDefaultAsync(a => a.AnalyzerID == analyzerId);
        }

        /// <summary>
        /// Асинхронно обновляет результат выполнения и статус услуги заказа.
        /// </summary>
        /// <param name="orderServiceId">Идентификатор услуги заказа.</param>
        /// <param name="result">Результат выполнения услуги.</param>
        /// <param name="statusId">Новый идентификатор статуса.</param>
        public static async Task UpdateOrderServiceResultAsync(int orderServiceId, string result, int statusId)
        {
            using var db = new MedLabDBEntities();
            var service = await db.OrderServices
                .FirstOrDefaultAsync(os => os.OrderServiceID == orderServiceId);
            if (service != null)
            {
                service.Result = result;
                service.StatusID = statusId;
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Асинхронно обновляет время выполнения заказа и, при необходимости, его статус.
        /// </summary>
        /// <param name="orderId">Идентификатор заказа.</param>
        /// <param name="createdAt">Дата создания заказа.</param>
        /// <param name="statusId">Новый идентификатор статуса (0, если статус не обновляется).</param>
        public static async Task UpdateOrderAsync(int orderId, DateTime createdAt, int statusId = 0)
        {
            using var db = new MedLabDBEntities();
            var order = await db.Orders
                .FirstOrDefaultAsync(o => o.OrderID == orderId);
            if (order != null)
            {
                order.ExecutionTimeDays = (int)(DateTime.Now - createdAt).TotalDays;
                if (statusId > 0)
                {
                    order.StatusID = statusId;
                }
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Асинхронно проверяет, все ли услуги в заказе имеют статус завершения (StatusID = 3).
        /// </summary>
        /// <param name="orderId">Идентификатор заказа.</param>
        /// <returns>True, если все услуги завершены; иначе false.</returns>
        public static async Task<bool> AreAllOrderServicesCompletedAsync(int orderId)
        {
            using var db = new MedLabDBEntities();
            return await db.OrderServices
                .Where(x => x.OrderID == orderId)
                .AllAsync(x => x.StatusID == 3);
        }

        /// <summary>
        /// Асинхронно устанавливает флаг доступности указанного анализатора в значение true.
        /// </summary>
        /// <param name="analyzerId">Идентификатор анализатора.</param>
        public static async Task SetAnalyzerAvailableAsync(int analyzerId)
        {
            using var db = new MedLabDBEntities();
            var analyzer = await db.Analyzers
                .FirstOrDefaultAsync(a => a.AnalyzerID == analyzerId);
            if (analyzer != null)
            {
                analyzer.isAvaible = true;
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Асинхронно загружает список пользователей с ролью пациента (RoleID = 1).
        /// </summary>
        /// <returns>Список объектов пользователей, являющихся пациентами.</returns>
        public static async Task<List<User>> LoadPatients()
        {
            using var db = new MedLabDBEntities();
            return await db.Users
                .Where(u => u.RoleID == 1)
                .ToListAsync();
        }

        /// <summary>
        /// Асинхронно загружает список всех доступных услуг.
        /// </summary>
        /// <returns>Список объектов услуг.</returns>
        public static async Task<List<Service>> LoadServices()
        {
            using var db = new MedLabDBEntities();
            return await db.Services
                .ToListAsync();
        }

        /// <summary>
        /// Асинхронно получает идентификатор последнего созданного заказа.
        /// </summary>
        /// <returns>Идентификатор последнего заказа или 0, если заказов нет.</returns>
        public static async Task<int> GetLastOrderId()
        {
            using var db = new MedLabDBEntities();
            return await db.Orders.AnyAsync() ? await db.Orders.MaxAsync(o => o.OrderID) : 0;
        }

        /// <summary>
        /// Сохраняет новый заказ и связанные с ним услуги в базе данных с использованием транзакции.
        /// </summary>
        /// <param name="order">Объект заказа для сохранения.</param>
        /// <param name="orderServices">Список объектов услуг, связанных с заказом.</param>
        public static async Task SaveOrder(Order order, List<OrderService> orderServices)
        {
            using var db = new MedLabDBEntities();
            using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            db.Orders.Add(order);
            await db.SaveChangesAsync(); // Save order to generate OrderID

            foreach (var orderService in orderServices)
            {
                orderService.OrderID = order.OrderID; // Set the foreign key
            }

            db.OrderServices.AddRange(orderServices);
            await db.SaveChangesAsync();

            transaction.Complete();
        }

        /// <summary>
        /// Асинхронно обновляет общую стоимость указанного заказа.
        /// </summary>
        /// <param name="orderId">Идентификатор заказа.</param>
        /// <param name="totalPrice">Новая общая стоимость заказа.</param>
        public static async Task UpdateOrderTotalPrice(int orderId, decimal? totalPrice)
        {
            using var db = new MedLabDBEntities();
            var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderID == orderId);
            if (order != null)
            {
                order.TotalPrice = (double?)totalPrice;
                await db.SaveChangesAsync();
            }
        }
    }
}
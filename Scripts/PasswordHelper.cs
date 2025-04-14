using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LabSystemApp.Helpers
{
    /// <summary>
    /// Статический класс для работы с хешированием и проверкой паролей пользователей.
    /// Использует алгоритм SHA256 для создания хешей паролей и предоставляет методы для их верификации.
    /// </summary>
    public class PasswordHelper
    {
        /// <summary>
        /// Выполняет хеширование пароля с использованием алгоритма SHA256.
        /// </summary>
        /// <param name="password">Пароль в виде открытого текста для хеширования.</param>
        /// <returns>Хеш пароля, преобразованный в строку формата Base64.</returns>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="password"/> равен null.</exception>
        public string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Хеширует все нехешированные пароли пользователей в базе данных.
        /// Обрабатывает данные постранично для оптимизации использования памяти.
        /// </summary>
        /// <param name="pageSize">Количество пользователей, обрабатываемых за одну итерацию (по умолчанию 100).</param>
        /// <exception cref="Exception">Пробрасывается при ошибке взаимодействия с базой данных.</exception>
        public void HashAllPlainPasswords(int pageSize = 100)
        {
            using var db = new MedLabDBEntities();
            try
            {
                int totalUsers = db.Users.Count(u => !string.IsNullOrEmpty(u.Password));
                int totalPages = (int)Math.Ceiling((double)totalUsers / pageSize);
                int hashedCount = 0;

                for (int page = 0; page < totalPages; page++)
                {
                    // Загружаем пользователей постранично
                    var usersPage = db.Users
                        .Where(u => !string.IsNullOrEmpty(u.Password))
                        .OrderBy(u => u.UserID)
                        .Skip(page * pageSize)
                        .Take(pageSize)
                        .ToList();

                    // Фильтруем пользователей с нехешированными паролями
                    var usersToUpdate = usersPage
                        .Where(u => !IsHash(u.Password))
                        .ToList();

                    foreach (var user in usersToUpdate)
                    {
                        user.Password = HashPassword(user.Password);
                        hashedCount++;
                    }

                    // Сохраняем изменения для текущей страницы
                    db.SaveChanges();
                }

                Debug.WriteLine($"Готово! Захешировано пользователей: {hashedCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при хешировании паролей: {ex.Message}");
                throw; // Re-throw to allow caller to handle
            }
        }

        /// <summary>
        /// Проверяет соответствие введенного пароля сохраненному хешу.
        /// </summary>
        /// <param name="inputPassword">Пароль, введенный пользователем для проверки.</param>
        /// <param name="hashedPassword">Сохраненный хеш пароля из базы данных.</param>
        /// <returns>True, если введенный пароль соответствует хешу; иначе false.</returns>
        public bool VerifyPassword(string inputPassword, string hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword))
                return false;

            try
            {
                // Хешируем введенный пароль тем же алгоритмом
                var inputHash = HashPassword(inputPassword);

                // Сравниваем хеши с использованием безопасного метода
                return SecureCompare(inputHash, hashedPassword);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка проверки пароля: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Выполняет безопасное сравнение двух строк хешей для защиты от атак по времени.
        /// </summary>
        /// <param name="a">Первая строка (хеш введенного пароля).</param>
        /// <param name="b">Вторая строка (сохраненный хеш пароля).</param>
        /// <returns>True, если строки идентичны; иначе false.</returns>
        private bool SecureCompare(string a, string b)
        {
            if (a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }

        /// <summary>
        /// Проверяет, является ли строка валидным хешем SHA256 в формате Base64.
        /// </summary>
        /// <param name="value">Строка для проверки.</param>
        /// <returns>True, если строка соответствует формату хеша SHA256; иначе false.</returns>
        private static bool IsHash(string value)
        {
            // Хеш SHA256 в Base64: длина 44 символа, допустимы буквы, цифры, +, / и =
            return value.Length >= 44 && value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
        }
    }
}
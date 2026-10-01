using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WitchRusPatcher
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch { }

            Console.Title = "Русификатор Witch's Apocalyptic Journey";
            PrintBanner();

            // 1. Locate game directory
            string? gameRoot = null;
            if (args.Length > 0 && Directory.Exists(args[0]))
            {
                if (GamePatcher.IsValidGameDirectory(args[0]))
                    gameRoot = Path.GetFullPath(args[0]);
            }

            if (gameRoot == null)
            {
                gameRoot = GamePatcher.DetectGameDirectory();
            }

            while (gameRoot == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Не удалось автоматически определить папку с игрой.");
                Console.ResetColor();
                Console.Write("Введите путь к папке игры (или перетащите папку сюда): ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Выход...");
                    return;
                }

                input = input.Trim().Trim('"', '\'');
                if (GamePatcher.IsValidGameDirectory(input))
                {
                    gameRoot = Path.GetFullPath(input);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Указанная папка не содержит нужных файлов игры (отсутствует catalog.bin или папка _Data). Попробуйте снова.\n");
                    Console.ResetColor();
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Папка игры: {gameRoot}\n");
            Console.ResetColor();

            // 2. Ensure translation database is present & up to date
            bool dbReady = await DatabaseManager.EnsureDatabaseAsync(gameRoot);
            if (!dbReady)
            {
                WaitAndExit();
                return;
            }

            string dbPath = Path.Combine(gameRoot, DatabaseManager.DbFileName);
            Console.WriteLine("Чтение базы данных переводов...");
            TranslationData translations;
            try
            {
                translations = DatabaseManager.LoadTranslations(dbPath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ОШИБКА] Не удалось прочитать базу данных: {ex.Message}");
                Console.ResetColor();
                WaitAndExit();
                return;
            }

            Console.WriteLine($"Загружено переводов интерфейса: {translations.StringTables.Count}");
            int csvCount = 0;
            foreach (var b in translations.DataConfigs.Values)
                foreach (var a in b.Values)
                    csvCount += a.Count;
            Console.WriteLine($"Загружено переводов игровых CSV: {csvCount}\n");

            // 3. Pre-check: existing Russian translation in game bundles
            Console.WriteLine("Проверка файлов игры на наличие старого перевода...");
            bool hasRussian = GamePatcher.CheckForExistingRussian(gameRoot);
            if (hasRussian)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[ВНИМАНИЕ] В файлах игры обнаружены следы предыдущей установки русификатора!");
                Console.WriteLine("Для корректной и стабильной установки патча рекомендуется сначала");
                Console.WriteLine("восстановить оригинальные файлы игры:");
                Console.WriteLine("  Steam -> Свойства игры -> Установленные файлы -> Проверить целостность файлов игры.\n");
                Console.ResetColor();

                bool continueAnyway = ConsoleMenu.AskYesNo("Продолжить установку поверх текущих файлов?", defaultYes: false);
                if (!continueAnyway)
                {
                    Console.WriteLine("\nУстановка отменена пользователем.");
                    WaitAndExit();
                    return;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Оригинальные файлы игры проверены (чистая установка).\n");
                Console.ResetColor();
            }

            // 4. Verification of Chinese original strings vs Database
            Console.WriteLine("Проверка соответствия китайского оригинала игры с базой перевода...");
            var stats = GamePatcher.AnalyzeTranslations(gameRoot, translations);

            Console.WriteLine($"  - Актуальных строк для перевода: {stats.TotalMatched}");
            if (stats.TotalStale > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  - Изменившихся строк (оригинал обновлён разработчиками): {stats.TotalStale}");
                Console.ResetColor();
            }
            if (stats.TotalNew > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  - Новых строк (отсутствуют в базе): {stats.TotalNew}");
                Console.ResetColor();
            }

            if (stats.TotalStale > 0 || stats.TotalNew > 0)
            {
                Console.WriteLine();
                if (!DatabaseManager.IsDatabaseUpToDate)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[!] В игре обнаружены несоответствия, при этом локальная база перевода ещё не обновлена до версии GitHub.");
                    Console.ResetColor();

                    bool updateNow = ConsoleMenu.AskYesNo("Обновить базу перевода с GitHub прямо сейчас?", defaultYes: true);
                    if (updateNow)
                    {
                        bool ok = await DatabaseManager.ForceUpdateDatabaseAsync(gameRoot);
                        if (ok)
                        {
                            try
                            {
                                translations = DatabaseManager.LoadTranslations(dbPath);
                                stats = GamePatcher.AnalyzeTranslations(gameRoot, translations);
                                Console.WriteLine("\nПовторный анализ после обновления базы:");
                                Console.WriteLine($"  - Актуальных строк для перевода: {stats.TotalMatched}");
                                Console.WriteLine($"  - Изменившихся строк: {stats.TotalStale}");
                                Console.WriteLine($"  - Новых строк: {stats.TotalNew}\n");
                            }
                            catch { }
                        }
                    }
                }

                if (stats.TotalStale > 0 || stats.TotalNew > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[ВНИМАНИЕ] Все новые и изменившиеся строки ({stats.TotalNew + stats.TotalStale}) будут оставлены на оригинальном английском языке во избежание ошибок.");
                    Console.ResetColor();
                }

                bool proceed = ConsoleMenu.AskYesNo("Продолжить установку русификатора?", defaultYes: true);
                if (!proceed)
                {
                    Console.WriteLine("\nУстановка отменена пользователем.");
                    WaitAndExit();
                    return;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Все строки игры полностью совпадают с базой перевода!\n");
                Console.ResetColor();

                bool readyToInstall = ConsoleMenu.AskYesNo("Начать установку русификатора?", defaultYes: true);
                if (!readyToInstall)
                {
                    Console.WriteLine("\nУстановка отменена пользователем.");
                    WaitAndExit();
                    return;
                }
            }

            Console.WriteLine();

            // 5. Execute patch
            bool success = GamePatcher.PatchGame(gameRoot, translations, out var finalStats);
            if (!success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nВо время установки произошла ошибка.");
                Console.ResetColor();
                WaitAndExit();
                return;
            }

            // 6. Print detailed report
            Console.WriteLine("\n" + new string('=', 60));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("РУСИФИКАЦИЯ УСПЕШНО ЗАВЕРШЕНА!");
            Console.ResetColor();
            Console.WriteLine($"Строк совпало и переведено: {finalStats.TotalMatched}");
            Console.WriteLine($"  - Интерфейс (StringTables): {finalStats.MatchedStringTables}");
            Console.WriteLine($"  - Игровые тексты (CSV): {finalStats.MatchedDataConfigs}");
            if (finalStats.TotalNew > 0)
            {
                Console.WriteLine($"Новых строк (оставлено на англ.): {finalStats.TotalNew}");
            }
            if (finalStats.TotalStale > 0)
            {
                Console.WriteLine($"Устаревших строк (изменился оригинал, оставлено на англ.): {finalStats.TotalStale}");
            }
            Console.WriteLine($"Бандлов обновлено в catalog.bin: {finalStats.ModifiedBundlesCount}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nВ настройках игры выберите язык 'English' — игра будет на русском языке.");
            Console.ResetColor();

            if (DatabaseManager.IsDatabaseUpToDate && (finalStats.TotalNew > 0 || finalStats.TotalStale > 0))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[i] База перевода актуальна на GitHub, но в текущей версии игры обнаружены новые или изменившиеся строки.");
                Console.WriteLine("    Об обновлении игры можно сообщить автору русификатора в Telegram: @ctak_co6ak");
                Console.ResetColor();
            }

            Console.WriteLine(new string('=', 60) + "\n");
            WaitAndExit();
        }

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine("        РУСИФИКАТОР ДЛЯ ИГРЫ WITCH'S APOCALYPTIC JOURNEY          ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void WaitAndExit()
        {
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            try
            {
                if (!Console.IsInputRedirected)
                    Console.ReadKey(true);
            }
            catch { }
        }
    }
}

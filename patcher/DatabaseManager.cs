using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WitchRusPatcher
{
    public class DbTranslationEntry
    {
        public string Zh { get; set; } = "";
        public string Ru { get; set; } = "";
    }

    public class TranslationData
    {
        // stringtable: game_id -> DbTranslationEntry
        public Dictionary<long, DbTranslationEntry> StringTables { get; } = new Dictionary<long, DbTranslationEntry>();

        // dataconfig: shortName -> assetName -> (game_id, key_column) -> DbTranslationEntry
        public Dictionary<string, Dictionary<string, Dictionary<(string id, string col), DbTranslationEntry>>> DataConfigs { get; }
            = new Dictionary<string, Dictionary<string, Dictionary<(string id, string col), DbTranslationEntry>>>(StringComparer.OrdinalIgnoreCase);
    }

    public static class DatabaseManager
    {
        public const string RemoteDbUrl = "https://github.com/CTAK-CO6AK/rus-witchs-apocalyptic-journey/raw/refs/heads/main/translations.db";
        public const string RemoteCommitsApiUrl = "https://api.github.com/repos/CTAK-CO6AK/rus-witchs-apocalyptic-journey/commits?path=translations.db&page=1&per_page=1";
        public const string DbFileName = "translations.db";
        public const string VersionFileName = ".rus-version";

        public static string? LatestRemoteCommitSha { get; private set; }
        public static string? CurrentLocalCommitSha { get; private set; }
        public static bool IsDatabaseUpToDate =>
            !string.IsNullOrEmpty(LatestRemoteCommitSha) &&
            string.Equals(CurrentLocalCommitSha, LatestRemoteCommitSha, StringComparison.OrdinalIgnoreCase);

        public static async Task<bool> EnsureDatabaseAsync(string gameRoot)
        {
            string dbPath = Path.Combine(gameRoot, DbFileName);
            string versionPath = Path.Combine(gameRoot, VersionFileName);

            bool dbExists = File.Exists(dbPath);
            CurrentLocalCommitSha = null;
            if (File.Exists(versionPath))
            {
                try
                {
                    CurrentLocalCommitSha = (await File.ReadAllTextAsync(versionPath)).Trim();
                }
                catch { }
            }

            Console.WriteLine("Проверка обновлений перевода на GitHub...");
            LatestRemoteCommitSha = null;

            using (var client = CreateHttpClient(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    var response = await client.GetAsync(RemoteCommitsApiUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                        {
                            var first = root[0];
                            if (first.TryGetProperty("sha", out var shaProp))
                            {
                                LatestRemoteCommitSha = shaProp.GetString();
                            }
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[!] Ответ сервера GitHub: {(int)response.StatusCode} {response.ReasonPhrase}");
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[!] Не удалось связаться с GitHub: {ex.Message}");
                    Console.ResetColor();
                }
            }

            if (LatestRemoteCommitSha == null)
            {
                // GitHub unreachable
                if (!dbExists)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[ОШИБКА] База данных перевода не найдена, и не удалось скачать её с GitHub.");
                    Console.ResetColor();
                    Console.WriteLine("Пожалуйста, скачайте файл translations.db вручную по ссылке:");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(RemoteDbUrl);
                    Console.ResetColor();
                    Console.WriteLine($"и поместите его в папку игры: {gameRoot}\n");
                    return false;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[ВНИМАНИЕ] Не удалось подключиться к GitHub для проверки обновлений базы перевода.");
                    Console.WriteLine("Будет использована существующая локальная база translations.db.\n");
                    Console.ResetColor();
                    return true;
                }
            }

            // GitHub is reachable and we have LatestRemoteCommitSha
            if (!dbExists)
            {
                Console.WriteLine("\nБаза данных перевода отсутствует. Скачивание базы с GitHub...");
                bool ok = await DownloadDatabaseAsync(dbPath);
                if (!ok)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[ОШИБКА] Не удалось скачать базу данных перевода.");
                    Console.ResetColor();
                    return false;
                }
                try
                {
                    await File.WriteAllTextAsync(versionPath, LatestRemoteCommitSha);
                    CurrentLocalCommitSha = LatestRemoteCommitSha;
                }
                catch { }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("База данных перевода успешно загружена.\n");
                Console.ResetColor();
                return true;
            }

            // dbExists and we have LatestRemoteCommitSha
            if (!IsDatabaseUpToDate)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\nНа GitHub найдена более новая версия базы перевода.");
                Console.ResetColor();

                bool update = ConsoleMenu.AskYesNo("Обновить локальную базу перевода?", defaultYes: true);
                if (update)
                {
                    await ForceUpdateDatabaseAsync(gameRoot);
                }
                else
                {
                    Console.WriteLine("Обновление пропущено. Используется текущая локальная база.\n");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Локальная база перевода актуальна.\n");
                Console.ResetColor();
            }

            return true;
        }

        public static async Task<bool> ForceUpdateDatabaseAsync(string gameRoot)
        {
            string dbPath = Path.Combine(gameRoot, DbFileName);
            string versionPath = Path.Combine(gameRoot, VersionFileName);

            Console.WriteLine("Загрузка обновления базы перевода...");
            bool ok = await DownloadDatabaseAsync(dbPath);
            if (ok)
            {
                if (!string.IsNullOrEmpty(LatestRemoteCommitSha))
                {
                    try
                    {
                        await File.WriteAllTextAsync(versionPath, LatestRemoteCommitSha);
                        CurrentLocalCommitSha = LatestRemoteCommitSha;
                    }
                    catch { }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("База данных перевода успешно обновлена.\n");
                Console.ResetColor();
                return true;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Не удалось скачать обновление. Будет использована текущая локальная база.\n");
                Console.ResetColor();
                return false;
            }
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = true
            };
            var client = new HttpClient(handler)
            {
                Timeout = timeout
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Witch-Rus-Patcher");
            return client;
        }

        private static async Task<bool> DownloadDatabaseAsync(string targetPath)
        {
            string tempPath = targetPath + ".tmp";
            string[] downloadUrls = new[]
            {
                RemoteDbUrl,
                "https://raw.githubusercontent.com/CTAK-CO6AK/rus-witchs-apocalyptic-journey/refs/heads/main/translations.db",
                "https://raw.githubusercontent.com/CTAK-CO6AK/rus-witchs-apocalyptic-journey/main/translations.db"
            };

            foreach (var url in downloadUrls)
            {
                try
                {
                    using var client = CreateHttpClient(TimeSpan.FromMinutes(2));
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    long? totalBytes = response.Content.Headers.ContentLength;

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double percent = (double)totalRead / totalBytes.Value * 100;
                            Console.Write($"\rПрогресс загрузки: {percent:F1}% ({totalRead / 1024} KB / {totalBytes.Value / 1024} KB)");
                        }
                        else
                        {
                            Console.Write($"\rЗагружено: {totalRead / 1024} KB");
                        }
                    }
                    Console.WriteLine();

                    fileStream.Close();
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);
                    File.Move(tempPath, targetPath);
                    return true;
                }
                catch
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n[ОШИБКА] Не удалось скачать базу переводов ни по одной из доступных ссылок.");
            Console.ResetColor();
            return false;
        }

        public static TranslationData LoadTranslations(string dbPath)
        {
            var data = new TranslationData();
            if (!File.Exists(dbPath))
                throw new FileNotFoundException($"Файл базы данных не найден: {dbPath}");

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            };

            using var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();

            // 1. StringTables
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT game_id, zh, ru FROM translations WHERE type='stringtable'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string idStr = reader.GetString(0);
                    string zh = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    string ru = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    if (long.TryParse(idStr, out long gid))
                    {
                        data.StringTables[gid] = new DbTranslationEntry { Zh = zh, Ru = ru };
                    }
                }
            }

            // 2. DataConfigs
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT bundle, game_id, key_column, zh, ru FROM translations WHERE type='dataconfig'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string bundleFull = reader.GetString(0);
                    string gameId = reader.GetString(1);
                    string keyCol = reader.GetString(2);
                    string zh = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    string ru = reader.IsDBNull(4) ? "" : reader.GetString(4);

                    if (bundleFull.Contains("__"))
                    {
                        int idx = bundleFull.IndexOf("__");
                        string shortName = bundleFull.Substring(0, idx);
                        string assetName = bundleFull.Substring(idx + 2);

                        if (!data.DataConfigs.TryGetValue(shortName, out var perAsset))
                        {
                            perAsset = new Dictionary<string, Dictionary<(string id, string col), DbTranslationEntry>>(StringComparer.OrdinalIgnoreCase);
                            data.DataConfigs[shortName] = perAsset;
                        }

                        if (!perAsset.TryGetValue(assetName, out var trMap))
                        {
                            trMap = new Dictionary<(string id, string col), DbTranslationEntry>();
                            perAsset[assetName] = trMap;
                        }

                        trMap[(gameId, keyCol)] = new DbTranslationEntry { Zh = zh, Ru = ru };
                    }
                }
            }

            return data;
        }
    }
}

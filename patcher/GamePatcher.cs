using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace WitchRusPatcher
{
    public class PatchStats
    {
        public int MatchedStringTables { get; set; }
        public int MatchedDataConfigs { get; set; }
        public int TotalMatched => MatchedStringTables + MatchedDataConfigs;

        public int NewStringTables { get; set; }
        public int NewDataConfigs { get; set; }
        public int TotalNew => NewStringTables + NewDataConfigs;

        public int StaleStringTables { get; set; }
        public int StaleDataConfigs { get; set; }
        public int TotalStale => StaleStringTables + StaleDataConfigs;

        public int ModifiedBundlesCount { get; set; }
        public int PatchedCrcCount { get; set; }
    }

    public static class GamePatcher
    {
        private const string EnStringtablesBundleName = "localization-string-tables-english(en)_assets_all.bundle";
        private const string ZhStringtablesBundleName = "localization-string-tables-chinese(simplified)(zh-cn)_assets_all.bundle";
        private static readonly Regex BundleKeyRegex = new Regex(@"_[0-9a-f]{32}\.bundle$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RussianWordRegex = new Regex(@"[а-яА-ЯёЁ]{2,}", RegexOptions.Compiled);

        public static string? DetectGameDirectory()
        {
            var candidates = new List<string>
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."))
            };

            foreach (var dir in candidates.Distinct())
            {
                if (IsValidGameDirectory(dir))
                    return dir;
            }

            return null;
        }

        public static bool IsValidGameDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return false;

            string dataDir = GetDataDirectory(dir);
            if (string.IsNullOrEmpty(dataDir)) return false;

            string catalogPath = Path.Combine(dataDir, "StreamingAssets", "aa", "catalog.bin");
            return File.Exists(catalogPath);
        }

        public static string GetDataDirectory(string gameRoot)
        {
            string candidate1 = Path.Combine(gameRoot, "Witch's Apocalyptic Journey_Data");
            if (Directory.Exists(candidate1)) return candidate1;

            string candidate2 = Path.Combine(gameRoot, "Witchs Apocalyptic Journey_Data");
            if (Directory.Exists(candidate2)) return candidate2;

            var dataDirs = Directory.GetDirectories(gameRoot, "*_Data");
            if (dataDirs.Length > 0)
                return dataDirs[0];

            return string.Empty;
        }

        public static bool CheckForExistingRussian(string gameRoot)
        {
            string dataDir = GetDataDirectory(gameRoot);
            string aaDir = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");
            string strBundlePath = Path.Combine(aaDir, EnStringtablesBundleName);

            if (File.Exists(strBundlePath))
            {
                try
                {
                    var am = new AssetsManager();
                    var bunInst = am.LoadBundleFile(strBundlePath, false);
                    var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

                    int ruMatches = 0;
                    foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                    {
                        var baseField = am.GetBaseField(fileInst, info);
                        var mTableData = baseField["m_TableData"];
                        if (mTableData.IsDummy) continue;

                        var array = mTableData["Array"];
                        if (array.IsDummy) continue;

                        for (int i = 0; i < array.Children.Count; i++)
                        {
                            string text = array[i]["m_Localized"].AsString;
                            if (HasRussianWords(text))
                            {
                                ruMatches++;
                                if (ruMatches >= 3)
                                {
                                    am.UnloadAllBundleFiles();
                                    return true;
                                }
                            }
                        }
                    }
                    am.UnloadAllBundleFiles();
                }
                catch { }
            }

            string textBundlesDir = Path.Combine(aaDir, "dataconfig_assets_dataconfigs", "text");
            if (Directory.Exists(textBundlesDir))
            {
                var files = Directory.GetFiles(textBundlesDir, "*.bundle");
                int totalRuWords = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var am = new AssetsManager();
                        var bunInst = am.LoadBundleFile(file, false);
                        var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

                        foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.TextAsset))
                        {
                            var baseField = am.GetBaseField(fileInst, info);
                            string script = baseField["m_Script"].AsString;
                            var (header, rows) = CsvHelper.ParseCsv(script);
                            var enCols = header.Select((col, idx) => (col, idx)).Where(c => c.col.EndsWith("_en")).Select(c => c.idx).ToList();

                            foreach (var row in rows)
                            {
                                foreach (var colIdx in enCols)
                                {
                                    if (colIdx < row.Count && HasRussianWords(row[colIdx]))
                                    {
                                        totalRuWords++;
                                        if (totalRuWords >= 5)
                                        {
                                            am.UnloadAllBundleFiles();
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        am.UnloadAllBundleFiles();
                    }
                    catch { }
                }
            }

            return false;
        }

        public static bool HasRussianWords(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return RussianWordRegex.IsMatch(text);
        }

        public static Dictionary<long, string> LoadGameZhStringTables(string bundlesDir)
        {
            var map = new Dictionary<long, string>();
            string zhPath = Path.Combine(bundlesDir, ZhStringtablesBundleName);
            if (!File.Exists(zhPath)) return map;

            try
            {
                var am = new AssetsManager();
                var bunInst = am.LoadBundleFile(zhPath, false);
                var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

                foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var baseField = am.GetBaseField(fileInst, info);
                    var mTableData = baseField["m_TableData"];
                    if (mTableData.IsDummy) continue;

                    var array = mTableData["Array"];
                    if (array.IsDummy) continue;

                    for (int i = 0; i < array.Children.Count; i++)
                    {
                        long id = array[i]["m_Id"].AsLong;
                        string localized = array[i]["m_Localized"].AsString;
                        map[id] = localized;
                    }
                }
                am.UnloadAllBundleFiles();
            }
            catch { }
            return map;
        }

        public static PatchStats AnalyzeTranslations(string gameRoot, TranslationData translations)
        {
            var stats = new PatchStats();
            string dataDir = GetDataDirectory(gameRoot);
            string bundlesDir = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");
            string textBundlesDir = Path.Combine(bundlesDir, "dataconfig_assets_dataconfigs", "text");

            // 1. Analyze StringTables
            var gameZhMap = LoadGameZhStringTables(bundlesDir);
            string enStrBundle = Path.Combine(bundlesDir, EnStringtablesBundleName);

            if (File.Exists(enStrBundle))
            {
                try
                {
                    var am = new AssetsManager();
                    var bunInst = am.LoadBundleFile(enStrBundle, false);
                    var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

                    foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                    {
                        var baseField = am.GetBaseField(fileInst, info);
                        var mTableData = baseField["m_TableData"];
                        if (mTableData.IsDummy) continue;

                        var array = mTableData["Array"];
                        if (array.IsDummy) continue;

                        for (int i = 0; i < array.Children.Count; i++)
                        {
                            long id = array[i]["m_Id"].AsLong;
                            string gameZh = gameZhMap.TryGetValue(id, out var gz) ? gz : "";

                            if (!translations.StringTables.TryGetValue(id, out var dbEntry))
                            {
                                stats.NewStringTables++;
                            }
                            else if (!string.IsNullOrEmpty(gameZh) && !string.Equals(dbEntry.Zh, gameZh, StringComparison.Ordinal))
                            {
                                stats.StaleStringTables++;
                            }
                            else if (!string.IsNullOrWhiteSpace(dbEntry.Ru))
                            {
                                stats.MatchedStringTables++;
                            }
                            else
                            {
                                stats.NewStringTables++;
                            }
                        }
                    }
                    am.UnloadAllBundleFiles();
                }
                catch { }
            }

            // 2. Analyze CSVs
            if (Directory.Exists(textBundlesDir))
            {
                foreach (var bFile in Directory.GetFiles(textBundlesDir, "*.bundle"))
                {
                    try
                    {
                        var fi = new FileInfo(bFile);
                        string shortName = BundleKeyRegex.Replace(fi.Name, "");
                        translations.DataConfigs.TryGetValue(shortName, out var perAsset);

                        var am = new AssetsManager();
                        var bunInst = am.LoadBundleFile(bFile, false);
                        var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

                        foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.TextAsset))
                        {
                            var baseField = am.GetBaseField(fileInst, info);
                            string aname = baseField["m_Name"].AsString;
                            Dictionary<(string id, string col), DbTranslationEntry>? trMap = null;
                            perAsset?.TryGetValue(aname, out trMap);

                            string raw = baseField["m_Script"].AsString;
                            var (header, rows) = CsvHelper.ParseCsv(raw);

                            var enCols = new List<(int Index, string ColName, int ZhIndex)>();
                            for (int i = 0; i < header.Count; i++)
                            {
                                if (header[i].EndsWith("_en"))
                                {
                                    string baseCol = header[i].Substring(0, header[i].Length - 3);
                                    int zhIdx = header.IndexOf(baseCol);
                                    enCols.Add((i, header[i], zhIdx));
                                }
                            }

                            foreach (var row in rows)
                            {
                                if (row.Count == 0 || string.IsNullOrEmpty(row[0])) continue;
                                string gameId = row[0];

                                foreach (var (colIdx, colName, zhIdx) in enCols)
                                {
                                    if (colIdx >= row.Count) continue;
                                    string gameZh = (zhIdx >= 0 && zhIdx < row.Count) ? row[zhIdx] : "";
                                    string gameEn = row[colIdx];

                                    // Skip empty cells where both English and Chinese are empty (identical to sync_db.py)
                                    if (string.IsNullOrWhiteSpace(gameEn) && string.IsNullOrWhiteSpace(gameZh))
                                        continue;

                                    if (trMap == null || !trMap.TryGetValue((gameId, colName), out var dbEntry))
                                    {
                                        stats.NewDataConfigs++;
                                    }
                                    else if (!string.IsNullOrEmpty(gameZh) && !string.Equals(dbEntry.Zh, gameZh, StringComparison.Ordinal))
                                    {
                                        stats.StaleDataConfigs++;
                                    }
                                    else if (!string.IsNullOrWhiteSpace(dbEntry.Ru))
                                    {
                                        stats.MatchedDataConfigs++;
                                    }
                                    else
                                    {
                                        stats.NewDataConfigs++;
                                    }
                                }
                            }
                        }
                        am.UnloadAllBundleFiles();
                    }
                    catch { }
                }
            }

            return stats;
        }

        public static bool PatchGame(string gameRoot, TranslationData translations, out PatchStats stats)
        {
            stats = new PatchStats();
            string dataDir = GetDataDirectory(gameRoot);
            string aaDir = Path.Combine(dataDir, "StreamingAssets", "aa");
            string bundlesDir = Path.Combine(aaDir, "StandaloneWindows64");
            string textBundlesDir = Path.Combine(bundlesDir, "dataconfig_assets_dataconfigs", "text");
            string catalogPath = Path.Combine(aaDir, "catalog.bin");

            if (!File.Exists(catalogPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ОШИБКА] Файл catalog.bin не найден: {catalogPath}");
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("Подготовка к установке перевода...");

            // 1. Record original sizes of bundles before any modification
            var originalSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var modifiedBundles = new List<string>();

            string strBundlePath = Path.Combine(bundlesDir, EnStringtablesBundleName);
            if (File.Exists(strBundlePath))
            {
                originalSizes[EnStringtablesBundleName] = new FileInfo(strBundlePath).Length;
            }

            if (Directory.Exists(textBundlesDir))
            {
                foreach (var bFile in Directory.GetFiles(textBundlesDir, "*.bundle"))
                {
                    var fi = new FileInfo(bFile);
                    originalSizes[fi.Name] = fi.Length;
                }
            }

            // 2. Load game Chinese string tables for verification
            var gameZhMap = LoadGameZhStringTables(bundlesDir);

            // 3. Patch stringtables
            if (File.Exists(strBundlePath))
            {
                Console.Write("Патчинг интерфейса (StringTables)... ");
                int count = PatchStringTablesBundle(strBundlePath, translations, gameZhMap, stats);
                if (count > 0)
                {
                    modifiedBundles.Add(EnStringtablesBundleName);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Готово (переведено {count} строк)");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("Пропущено (нет актуальных строк для перевода)");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[!] Бандл интерфейса {EnStringtablesBundleName} не найден, пропускаем.");
                Console.ResetColor();
            }

            // 4. Patch dataconfig text bundles
            if (Directory.Exists(textBundlesDir))
            {
                var bundleFiles = Directory.GetFiles(textBundlesDir, "*.bundle");
                Console.WriteLine($"Патчинг игровых текстов (диалоги, карты, реликвии)... Всего бандлов: {bundleFiles.Length}");

                foreach (var bFile in bundleFiles)
                {
                    var fi = new FileInfo(bFile);
                    string shortName = BundleKeyRegex.Replace(fi.Name, "");

                    if (!translations.DataConfigs.TryGetValue(shortName, out var perAsset))
                        continue;

                    int count = PatchTextBundle(bFile, shortName, perAsset, stats);
                    if (count > 0)
                    {
                        modifiedBundles.Add(fi.Name);
                        Console.WriteLine($"  {shortName}: переведено {count} строк");
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Текстовые бандлы обработаны (всего переведено {stats.MatchedDataConfigs} ячеек).");
                Console.ResetColor();
            }

            stats.ModifiedBundlesCount = modifiedBundles.Count;

            if (modifiedBundles.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[!] Не найдено актуальных изменений для применения.");
                Console.ResetColor();
                return true;
            }

            // 5. Patch catalog.bin CRC
            Console.Write("\nПатчинг контрольных сумм в catalog.bin... ");
            byte[] catalogData = File.ReadAllBytes(catalogPath);
            var records = CatalogCrcPatcher.FindAbroRecords(catalogData);

            int totalPatchedCrc = 0;
            var missingCrc = new List<string>();

            foreach (var bName in modifiedBundles)
            {
                if (!originalSizes.TryGetValue(bName, out long origSize))
                {
                    missingCrc.Add($"{bName} (неизвестен исходный размер)");
                    continue;
                }

                var (found, patched) = CatalogCrcPatcher.ZeroCrc(catalogData, bName, origSize, records);
                if (found != 1)
                {
                    missingCrc.Add($"{bName} (найдено записей в каталоге: {found})");
                }
                totalPatchedCrc += patched;
            }

            if (missingCrc.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ОШИБКА] Не удалось найти записи CRC для: {string.Join(", ", missingCrc)}");
                Console.WriteLine("Файл catalog.bin НЕ был перезаписан во избежание сбоев игры.");
                Console.ResetColor();
                return false;
            }

            File.WriteAllBytes(catalogPath, catalogData);
            stats.PatchedCrcCount = totalPatchedCrc;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Готово (занулено CRC для {totalPatchedCrc} бандлов)");
            Console.ResetColor();

            return true;
        }

        private static int PatchStringTablesBundle(string bundlePath, TranslationData translations, Dictionary<long, string> gameZhMap, PatchStats stats)
        {
            var am = new AssetsManager();
            var bunInst = am.LoadBundleFile(bundlePath, false);
            var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

            int patchedInBundle = 0;
            bool modified = false;

            foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var baseField = am.GetBaseField(fileInst, info);
                var mTableData = baseField["m_TableData"];
                if (mTableData.IsDummy) continue;

                var array = mTableData["Array"];
                if (array.IsDummy) continue;

                bool tableModified = false;
                for (int i = 0; i < array.Children.Count; i++)
                {
                    var item = array[i];
                    long id = item["m_Id"].AsLong;
                    string gameZh = gameZhMap.TryGetValue(id, out var gz) ? gz : "";

                    if (!translations.StringTables.TryGetValue(id, out var dbEntry))
                    {
                        stats.NewStringTables++;
                        continue;
                    }

                    // Check if Chinese original changed
                    if (!string.IsNullOrEmpty(gameZh) && !string.Equals(dbEntry.Zh, gameZh, StringComparison.Ordinal))
                    {
                        stats.StaleStringTables++;
                        continue; // Do NOT patch stale translation! Keep English.
                    }

                    if (string.IsNullOrWhiteSpace(dbEntry.Ru))
                    {
                        stats.NewStringTables++;
                        continue;
                    }

                    stats.MatchedStringTables++;
                    string current = item["m_Localized"].AsString;
                    if (current != dbEntry.Ru)
                    {
                        item["m_Localized"].AsString = dbEntry.Ru;
                        tableModified = true;
                        patchedInBundle++;
                    }
                }

                if (tableModified)
                {
                    info.SetNewData(baseField);
                    modified = true;
                }
            }

            if (modified)
            {
                SaveRepackedBundle(am, bunInst, fileInst, bundlePath);
            }
            else
            {
                am.UnloadAllBundleFiles();
            }

            return patchedInBundle;
        }

        private static int PatchTextBundle(string bundlePath, string shortName, Dictionary<string, Dictionary<(string id, string col), DbTranslationEntry>> perAsset, PatchStats stats)
        {
            var am = new AssetsManager();
            var bunInst = am.LoadBundleFile(bundlePath, false);
            var fileInst = am.LoadAssetsFileFromBundle(bunInst, 0, false);

            int patchedInBundle = 0;
            bool modified = false;

            foreach (var info in fileInst.file.GetAssetsOfType(AssetClassID.TextAsset))
            {
                var baseField = am.GetBaseField(fileInst, info);
                string aname = baseField["m_Name"].AsString;

                perAsset.TryGetValue(aname, out var trMap);

                string raw = baseField["m_Script"].AsString;
                bool crlf = raw.Contains("\r\n");
                var (header, rows) = CsvHelper.ParseCsv(raw);

                var enCols = new List<(int Index, string ColName, int ZhIndex)>();
                for (int i = 0; i < header.Count; i++)
                {
                    if (header[i].EndsWith("_en"))
                    {
                        string baseCol = header[i].Substring(0, header[i].Length - 3);
                        int zhIdx = header.IndexOf(baseCol);
                        enCols.Add((i, header[i], zhIdx));
                    }
                }

                if (enCols.Count == 0) continue;

                bool assetModified = false;
                foreach (var row in rows)
                {
                    if (row.Count == 0 || string.IsNullOrEmpty(row[0])) continue;
                    string gameId = row[0];

                    foreach (var (colIdx, colName, zhIdx) in enCols)
                    {
                        if (colIdx >= row.Count) continue;
                        string gameZh = (zhIdx >= 0 && zhIdx < row.Count) ? row[zhIdx] : "";
                        string gameEn = row[colIdx];

                        // Skip empty cells where both English and Chinese are empty (identical to sync_db.py)
                        if (string.IsNullOrWhiteSpace(gameEn) && string.IsNullOrWhiteSpace(gameZh))
                            continue;

                        if (trMap == null || !trMap.TryGetValue((gameId, colName), out var dbEntry))
                        {
                            stats.NewDataConfigs++;
                            continue;
                        }

                        // Check if Chinese original changed
                        if (!string.IsNullOrEmpty(gameZh) && !string.Equals(dbEntry.Zh, gameZh, StringComparison.Ordinal))
                        {
                            stats.StaleDataConfigs++;
                            continue; // Do NOT patch stale translation! Keep English.
                        }

                        if (string.IsNullOrWhiteSpace(dbEntry.Ru))
                        {
                            stats.NewDataConfigs++;
                            continue;
                        }

                        stats.MatchedDataConfigs++;
                        if (row[colIdx] != dbEntry.Ru)
                        {
                            row[colIdx] = dbEntry.Ru;
                            patchedInBundle++;
                            assetModified = true;
                        }
                    }
                }

                if (assetModified)
                {
                    baseField["m_Script"].AsString = CsvHelper.DumpCsv(header, rows, crlf);
                    info.SetNewData(baseField);
                    modified = true;
                }
            }

            if (modified)
            {
                SaveRepackedBundle(am, bunInst, fileInst, bundlePath);
            }
            else
            {
                am.UnloadAllBundleFiles();
            }

            return patchedInBundle;
        }

        private static void SaveRepackedBundle(AssetsManager am, BundleFileInstance bunInst, AssetsFileInstance fileInst, string bundlePath)
        {
            byte[] newAssetData;
            using (var ms = new MemoryStream())
            {
                var writer = new AssetsFileWriter(ms);
                fileInst.file.Write(writer);
                newAssetData = ms.ToArray();
            }

            int dirIndex = bunInst.file.GetFileIndex(fileInst.name);
            if (dirIndex < 0) dirIndex = 0;

            bunInst.file.BlockAndDirInfo.DirectoryInfos[dirIndex].Replacer = new ContentReplacerFromBuffer(newAssetData);

            byte[] unpackedBytes;
            using (var unpackedMs = new MemoryStream())
            {
                var unpackedWriter = new AssetsFileWriter(unpackedMs);
                bunInst.file.Write(unpackedWriter);
                unpackedBytes = unpackedMs.ToArray();
            }

            using var unpackedReader = new AssetsFileReader(new MemoryStream(unpackedBytes));
            var unpackedBundle = new AssetBundleFile();
            unpackedBundle.Read(unpackedReader);

            string tempPath = bundlePath + ".tmp";
            using (var fs = File.Create(tempPath))
            {
                var writer = new AssetsFileWriter(fs);
                unpackedBundle.Pack(writer, AssetBundleCompressionType.LZ4);
            }

            am.UnloadAllBundleFiles();

            File.Delete(bundlePath);
            File.Move(tempPath, bundlePath);
        }
    }
}

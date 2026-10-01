# Русификатор Witch's Apocalyptic Journey (Патчер)

Автономный установщик и обновитель русификатора для игры **Witch's Apocalyptic Journey** (Unity 6 / Addressables).

- **Размер бинарника:** ~12 МБ (Single-File, Self-Contained)
- **Платформа:** Windows 10 / 11 (x64)
- **База переводов:** [CTAK-CO6AK/rus-witchs-apocalyptic-journey](https://github.com/CTAK-CO6AK/rus-witchs-apocalyptic-journey)

---

## Для игроков: Быстрый старт

Готовый исполняемый файл не требует установки дополнительных программ и фреймворков.

1. **Скачайте** актуальную версию `Witch-Rus-Patcher.exe` со страницы [Releases](https://github.com/CTAK-CO6AK/rus-witchs-apocalyptic-journey/releases).
2. **Поместите** скачанный файл `Witch-Rus-Patcher.exe` в корневую папку игры (туда, где находится `Witch's Apocalyptic Journey.exe` и папка `Witch's Apocalyptic Journey_Data`).
3. **Запустите** `Witch-Rus-Patcher.exe`:
   - Патчер автоматически проверит обновления базы данных перевода на GitHub.
   - Скачает актуальную SQLite-базу `translations.db` (при первом запуске или наличии обновлений).
   - Проверит целостность игровых файлов и соответствие китайского оригинала.
   - Применит перевод к игровым архивам (StringTables и CSV) и скорректирует контрольные суммы в `catalog.bin`.
4. В настройках игры выберите язык **English** — интерфейс и игровые тексты будут на русском языке.

> **Обновление перевода:** Чтобы обновить русификатор при выходе новой версии перевода, просто запустите `Witch-Rus-Patcher.exe` ещё раз — он автоматически загрузит изменения с GitHub и обновит файлы игры.

---

## Особенности и возможности

- **Полная автономность:** собран в режиме `Self-Contained` со сжатием и IL-триммингом. Работает на чистой системе без необходимости устанавливать .NET Runtime.
- **Интеллектуальная сверка строк:** патчер не просто слепо заменяет английские строки, а сверяет оригинальный китайский текст в бандлах игры со столбцом `zh` в базе. Если строка была изменена разработчиками в обновлении игры или ещё не переведена, она безопасно остаётся на английском языке.
- **Патчинг Addressables Unity 6:** распаковка и перепаковка бандлов интерфейса (`localization-string-tables-english(en)_assets_all.bundle`) и игровых текстов (`dataconfig_assets_dataconfigs/text/*.bundle`) с сохранением сжатия LZ4.
- **Коррекция CRC в `catalog.bin`:** зануление 4-байтовых контрольных сумм в бинарном каталоге Addressables для предотвращения конфликтов целостности.
- **Безопасность:** предварительная проверка файлов игры на наличие остатков старого перевода с предложением проверить целостность в Steam при обнаружении конфликтов.
- **Интерактивный UI:** удобное управление в терминале с навигацией стрелками или клавишами W/S.

---

## Сборка из исходников (для разработчиков)

В репозитории содержатся исключительно исходные коды без скомпилированных сторонних бинарников (`.dll`).

### Требования
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (или новее)
- Windows x64

### Внешние зависимости
Для работы с ассетами Unity патчер использует библиотеку **AssetsTools.NET v3** ([nesrak1/AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET)). 

Перед сборкой скопируйте следующие DLL в папку с проектом (рядом с `Patcher.csproj`):
- `AssetsTools.NET.dll`
- `AssetsTools.NET.MonoCecil.dll`
- `AssetsTools.NET.Texture.dll`
- `AssetsTools.NET.Cpp2IL.dll`

Пакет `Microsoft.Data.Sqlite` будет загружен менеджером NuGet автоматически при сборке.

### Сборка проекта

**Вариант 1 (через пакетный скрипт):**
Запустите файл `build.bat`.

**Вариант 2 (через консоль):**
```bash
dotnet publish -c Release -o bin
```

Готовый автономный файл `Witch-Rus-Patcher.exe` размером ~12 МБ появится в папке `bin/`.

---

## Архитектура и структура файлов

```
patcher/
├── Program.cs             # Точка входа, оркестрация шагов, интерактивное меню
├── GamePatcher.cs         # Логика поиска игры, проверок, распаковки и патчинга бандлов
├── DatabaseManager.cs     # Работа с GitHub API (проверка SHA, скачивание) и чтение SQLite DB
├── CatalogCrcPatcher.cs   # Поиск блоков ABRO и зануление CRC в catalog.bin
├── CsvHelper.cs           # Парсер и генератор CSV (RFC 4180) с поддержкой UTF-8 BOM и CRLF
├── ConsoleMenu.cs         # Реализация интерактивного меню (стрелки / W-S / Enter / пайпы)
├── Patcher.csproj         # Конфигурация проекта .NET 8 (Single-File, Trimming, компрессия, метаданные)
├── build.bat              # Скрипт сборки в 1 клик
├── app.ico                # Иконка исполняемого файла
└── README.md              # Документация проекта
```

---

## Авторство и контакты

- **Перевод и разработка патчера:** [CTAK-CO6AK](https://github.com/CTAK-CO6AK)
- **Telegram:** [@ctak_co6ak](https://t.me/ctak_co6ak)
- **Репозиторий:** [rus-witchs-apocalyptic-journey](https://github.com/CTAK-CO6AK/rus-witchs-apocalyptic-journey)

# -*- coding: utf-8 -*-
"""Шаг 3. Собирает готовый русификатор из базы данных SQLite (workspace/translations.db).

Русский текст кладётся в АНГЛИЙСКИЙ слот игры (в игре нужно выбрать язык English).
Имена файлов бандлов не меняются — заменяется только содержимое; catalog.bin
патчится точечно (зануляется CRC).

Использование:
    python build_patch.py [путь к папке текущей игры]

Результат: dist/RUS-patch/ — папка, содержимое которой копируется в папку игры.
"""
import sqlite3
import shutil
import sys
from pathlib import Path

from common import (DEFAULT_GAME_DIR, AA_SUBPATH, BUNDLES_SUBPATH, TEXT_BUNDLES_SUBPATH,
                    EN_STRINGTABLES_BUNDLE, DIST_DIR, WORKSPACE_DIR,
                    dump_csv, find_text_bundles, parse_csv, read_string_tables)
import UnityPy

DB_PATH = WORKSPACE_DIR / "translations.db"

def get_db():
    if not DB_PATH.exists():
        sys.exit(f"ОШИБКА: База данных {DB_PATH} не найдена. Нечего собирать.")
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn

def patch_stringtables(game_dir: Path, out_dir: Path) -> int:
    src = game_dir / BUNDLES_SUBPATH / EN_STRINGTABLES_BUNDLE
    if not src.exists():
        print(f"Файл {src.name} не найден, пропускаем интерфейс.")
        return 0
        
    env, tables = read_string_tables(src)
    
    # 1. Достаем все переводы интерфейса из базы
    conn = get_db()
    cursor = conn.cursor()
    cursor.execute("SELECT game_id, ru FROM translations WHERE type='stringtable' AND ru != ''")
    ru_by_id = {str(row['game_id']): row['ru'] for row in cursor.fetchall()}
    conn.close()

    # 2. Вшиваем в бандл
    n = 0
    for name, t in tables.items():
        tt = t["tree"]
        changed = False
        for e in tt["m_TableData"]:
            ru = ru_by_id.get(str(e["m_Id"]))
            if ru and ru != e["m_Localized"]:
                e["m_Localized"] = ru
                changed = True
                n += 1
        if changed:
            t["obj"].save_typetree(tt)
            
    dst = out_dir / BUNDLES_SUBPATH / EN_STRINGTABLES_BUNDLE
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(env.file.save(packer="lz4"))
    print(f"Строковые таблицы: заменено {n} строк -> {dst.name}")
    return n

def patch_dataconfig(game_dir: Path, out_dir: Path) -> int:
    # 1. Достаем все переводы конфигов и диалогов из базы
    conn = get_db()
    cursor = conn.cursor()
    cursor.execute("SELECT bundle, game_id, key_column, ru FROM translations WHERE type='dataconfig' AND ru != ''")
    
    # Структура: tr[short_name][asset_name] = {(id, column): ru}
    tr = {}
    for row in cursor.fetchall():
        bundle_full = row['bundle']
        # В базе бандл записан как "short__aname", разбиваем обратно
        if "__" in bundle_full:
            short, aname = bundle_full.split("__", 1)
            tr.setdefault(short, {}).setdefault(aname, {})[(str(row['game_id']), row['key_column'])] = row['ru']
    conn.close()

    # 2. Вшиваем в бандлы
    total = 0
    for short, bpath in sorted(find_text_bundles(game_dir).items()):
        per_asset = tr.get(short)
        if not per_asset:
            continue
            
        env = UnityPy.load(str(bpath))
        n_bundle = 0
        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            d = obj.read()
            m = per_asset.get(d.m_Name)
            if not m:
                continue
                
            raw = d.m_Script
            crlf = "\r\n" in raw
            header, rows = parse_csv(raw)
            for row in rows:
                if not row or not row[0]:
                    continue
                for i, col in enumerate(header):
                    if not col.endswith("_en") or i >= len(row):
                        continue
                    ru = m.get((str(row[0]), col))
                    if ru:
                        row[i] = ru
                        n_bundle += 1
                        
            d.m_Script = dump_csv(header, rows, crlf)
            d.save()
            
        if n_bundle:
            dst = out_dir / TEXT_BUNDLES_SUBPATH / bpath.name
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(env.file.save(packer="lz4"))
            total += n_bundle
            print(f"  {bpath.name}: {n_bundle} ячеек")
            
    print(f"CSV-тексты: заменено {total} ячеек")
    return total

def patch_catalog(game_dir: Path, out_dir: Path):
    import patch_catalog_crc
    src = game_dir / AA_SUBPATH / "catalog.bin"
    dst = out_dir / AA_SUBPATH / "catalog.bin"
    
    names = [p.name for p in (out_dir / BUNDLES_SUBPATH).rglob("*.bundle")]
    if not names:
        return
        
    data = bytearray(src.read_bytes())
    records = patch_catalog_crc.find_abro_records(bytes(data))
    total, missing = 0, []
    
    for name in names:
        orig = next((game_dir / BUNDLES_SUBPATH).rglob(name))
        found, patched = patch_catalog_crc.zero_crc(data, name, orig.stat().st_size, records)
        if found != 1:
            missing.append(f"{name} (найдено записей: {found})")
        total += patched
        
    if missing:
        sys.exit(f"ОШИБКА: не найдены CRC-записи для {missing} — catalog.bin не записан.")
        
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(bytes(data))
    n_diff = sum(a != b for a, b in zip(src.read_bytes(), bytes(data)))
    print(f"catalog.bin: занулено CRC для {total} бандлов "
          f"(изменено байт: {n_diff}, размер не менялся)")

README_USER = """РУСИФИКАТОР Witch's Apocalyptic Journey
=======================================

ЕСЛИ СТОЯЛА ПРЕДЫДУЩАЯ ВЕРСИЯ ПАТЧА
-----------------------------------
Сначала восстановите оригинальные файлы: Steam -> свойства игры ->
Установленные файлы -> Проверить целостность файлов (или верните свою
резервную копию папки "aa"). Затем ставьте этот патч.

УСТАНОВКА (автоматически)
-------------------------
Запустите install.bat и укажите путь к папке игры (или нажмите Enter,
если игра в стандартной папке Steam). Скрипт сам сделает резервную копию
и скопирует файлы.

УСТАНОВКА (вручную)
-------------------
1. Откройте папку с игрой (там, где "Witch's Apocalyptic Journey.exe").
2. Желательно: сохраните копию папки
   "Witch's Apocalyptic Journey_Data\\StreamingAssets\\aa" для отката.
3. Скопируйте содержимое этой папки в папку игры, согласившись на замену.
4. В игре выберите язык English — игра будет на русском.

УДАЛЕНИЕ
--------
Steam -> свойства игры -> Установленные файлы -> Проверить целостность файлов,
либо верните резервную копию (папка backup_aa рядом с install.bat).
"""

INSTALL_BAT = r"""@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
set "DEFAULT=C:\Program Files (x86)\Steam\steamapps\common\Witch's Apocalyptic Journey"
echo Русификатор Witch's Apocalyptic Journey
echo.
set /p GAME="Путь к папке игры [Enter = %DEFAULT%]: "
if "!GAME!"=="" set "GAME=%DEFAULT%"
if not exist "!GAME!\Witch's Apocalyptic Journey.exe" (
    echo ОШИБКА: в "!GAME!" не найдена игра.
    pause & exit /b 1
)
echo Резервная копия -> "%~dp0backup_aa"
robocopy "!GAME!\Witch's Apocalyptic Journey_Data\StreamingAssets\aa" "%~dp0backup_aa" /E /NFL /NDL /NJH /NJS >nul
echo Копирование файлов перевода...
robocopy "%~dp0Witch's Apocalyptic Journey_Data" "!GAME!\Witch's Apocalyptic Journey_Data" /E /NFL /NDL /NJH /NJS >nul
if errorlevel 8 (
    echo ОШИБКА копирования. Возможно, нужен запуск от администратора.
    pause & exit /b 1
)
echo.
echo Готово! В игре выберите язык English.
pause
"""

def main():
    game_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_GAME_DIR
    out_dir = DIST_DIR / "RUS-patch"
    
    if not game_dir.exists():
        sys.exit(f"ОШИБКА: Папка игры {game_dir} не найдена.")
        
    if out_dir.exists():
        shutil.rmtree(out_dir)

    print(f"Сборка русификатора в {out_dir}...")
    n1 = patch_stringtables(game_dir, out_dir)
    n2 = patch_dataconfig(game_dir, out_dir)
    
    if n1 + n2 > 0:
        patch_catalog(game_dir, out_dir)
        (out_dir / "README.txt").write_text(README_USER, encoding="utf-8")
        (out_dir / "install.bat").write_bytes(INSTALL_BAT.replace("\n", "\r\n").encode("utf-8"))
        print(f"\nГотово! Вы можете найти ваш патч в: {out_dir}")
        print(f"Всего заменено строк в игре: {n1 + n2}")
    else:
        print("\nНет переведенных строк для сборки. Проверьте базу данных.")

if __name__ == "__main__":
    main()
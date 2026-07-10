# -*- coding: utf-8 -*-
"""Утилита для синхронизации игрового текста с базой данных SQLite.

Использование:
    1. Первичная миграция (извлечь ваш текущий перевод из патча в БД):
       python sync_db.py init --patch ../dist/RUS-patch --game "../Witch's Apocalyptic Journey"
       
    2. Синхронизация с игрой (найти новые строки, обновить EN, пометить STALE):
       python sync_db.py sync --game "../Witch's Apocalyptic Journey"
"""
import sys
import sqlite3
import argparse
import itertools
from pathlib import Path

from common import (DEFAULT_GAME_DIR, BUNDLES_SUBPATH, EN_STRINGTABLES_BUNDLE,
                    ZH_STRINGTABLES_BUNDLE, SHARED_ASSETS_BUNDLE, WORKSPACE_DIR,
                    find_text_bundles, has_cyrillic, parse_csv,
                    read_shared_key_names, read_string_tables, read_text_assets)

DB_PATH = WORKSPACE_DIR / "translations.db"

def setup_db():
    WORKSPACE_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute('''
        CREATE TABLE IF NOT EXISTS translations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            bundle TEXT NOT NULL,
            type TEXT NOT NULL,
            game_id TEXT NOT NULL,
            key_column TEXT NOT NULL,
            zh TEXT DEFAULT '',
            en TEXT DEFAULT '',
            ru TEXT DEFAULT '',
            status TEXT DEFAULT 'NEW',
            UNIQUE(bundle, type, game_id, key_column)
        )
    ''')
    # Индексы для быстрого поиска при сборке и в вебе
    conn.execute('CREATE INDEX IF NOT EXISTS idx_status ON translations(status)')
    conn.execute('CREATE INDEX IF NOT EXISTS idx_bundle ON translations(bundle)')
    conn.commit()
    return conn

def scan_stringtables(en_dir: Path, zh_dir: Path):
    """Генератор, читающий строковые таблицы (интерфейс)."""
    zh_path = zh_dir / BUNDLES_SUBPATH / ZH_STRINGTABLES_BUNDLE
    zh_by_id = {}
    if zh_path.exists():
        _, zh_tables = read_string_tables(zh_path)
        for t in zh_tables.values():
            for e in t["tree"]["m_TableData"]:
                zh_by_id[str(e["m_Id"])] = e["m_Localized"]

    en_path = en_dir / BUNDLES_SUBPATH / EN_STRINGTABLES_BUNDLE
    if not en_path.exists():
        return

    _, en_tables = read_string_tables(en_path)
    # Берем файл ключей из папки игры (zh_dir), так как в папке патча его нет
    keys_path = zh_dir / BUNDLES_SUBPATH / SHARED_ASSETS_BUNDLE
    keys = read_shared_key_names(keys_path) if keys_path.exists() else {}

    for name, t in en_tables.items():
        for e in t["tree"]["m_TableData"]:
            mid = str(e["m_Id"])
            yield {
                "bundle": name,
                "type": "stringtable",
                "game_id": mid,
                "key_column": keys.get(int(mid), ""),
                "zh": zh_by_id.get(mid, ""),
                "en": e["m_Localized"]
            }

def scan_dataconfig(game_dir: Path):
    """Генератор, читающий тексты из CSV файлов внутри бандлов."""
    for short, bpath in sorted(find_text_bundles(game_dir).items()):
        _, assets = read_text_assets(bpath)
        for aname, a in assets.items():
            header, rows = parse_csv(a["text"])
            en_cols = [i for i, c in enumerate(header) if c.endswith("_en")]
            if not en_cols:
                continue
            id_col = 0
            for row in rows:
                if not row or not row[id_col]:
                    continue
                for i in en_cols:
                    if i >= len(row):
                        continue
                    en_val = row[i]
                    base_col = header[i][:-3]
                    zh = ""
                    if base_col in header:
                        j = header.index(base_col)
                        zh = row[j] if j < len(row) else ""
                    
                    if not en_val and not zh:
                        continue
                        
                    yield {
                        "bundle": f"{short}__{aname}",
                        "type": "dataconfig",
                        "game_id": row[id_col],
                        "key_column": header[i],
                        "zh": zh,
                        "en": en_val
                    }

def handle_init(args):
    """Первичное заполнение БД из готового патча (где RU лежит в EN слоте)."""
    conn = setup_db()
    patch_dir = Path(args.patch)
    game_dir = Path(args.game)

    if not patch_dir.exists():
        sys.exit(f"Ошибка: Папка патча {patch_dir} не найдена.")

    print(f"📥 Инициализация базы данных из патча: {patch_dir.name}...")
    
    count = 0
    cursor = conn.cursor()
    
    # Объединяем генераторы таблиц и CSV
    iterator = itertools.chain(
        scan_stringtables(patch_dir, game_dir),
        scan_dataconfig(patch_dir)
    )
    
    for item in iterator:
        ru_text = item["en"] # В патче русский текст лежит в ячейках EN
        if not ru_text:
            continue
            
        status = 'OK' if has_cyrillic(ru_text) else 'CHECK'
        
        cursor.execute('''
            INSERT INTO translations (bundle, type, game_id, key_column, zh, en, ru, status)
            VALUES (?, ?, ?, ?, ?, '', ?, ?)
            ON CONFLICT(bundle, type, game_id, key_column) 
            DO UPDATE SET ru=excluded.ru, zh=excluded.zh, status=excluded.status
        ''', (item["bundle"], item["type"], item["game_id"], item["key_column"], item["zh"], ru_text, status))
        count += 1

    conn.commit()
    print(f"✅ Успешно перенесено строк в БД: {count}")
    print("⚠️ Теперь обязательно запустите 'python sync_db.py sync', чтобы подтянуть оригинальный EN текст из чистой игры!")

def handle_sync(args):
    """Синхронизация БД с текущей версией игры (обновление EN, проверка STALE)."""
    conn = setup_db()
    game_dir = Path(args.game)
    
    if not game_dir.exists():
        sys.exit(f"Ошибка: Папка игры {game_dir} не найдена.")

    print(f"🔄 Синхронизация с версией игры: {game_dir.name}...")
    cursor = conn.cursor()
    
    stats = {"NEW": 0, "STALE": 0, "UPDATED_EN": 0, "UNCHANGED": 0}
    
    iterator = itertools.chain(
        scan_stringtables(game_dir, game_dir),
        scan_dataconfig(game_dir)
    )
    
    for item in iterator:
        cursor.execute('''
            SELECT zh, ru, status FROM translations 
            WHERE bundle=? AND type=? AND game_id=? AND key_column=?
        ''', (item["bundle"], item["type"], item["game_id"], item["key_column"]))
        
        db_row = cursor.fetchone()
        
        if not db_row:
            # Новая строка в игре
            cursor.execute('''
                INSERT INTO translations (bundle, type, game_id, key_column, zh, en, ru, status)
                VALUES (?, ?, ?, ?, ?, ?, '', 'NEW')
            ''', (item["bundle"], item["type"], item["game_id"], item["key_column"], item["zh"], item["en"]))
            stats["NEW"] += 1
        else:
            old_zh, ru, old_status = db_row["zh"], db_row["ru"], db_row["status"]
            
            if old_zh and old_zh != item["zh"]:
                # Китайский оригинал изменился
                new_status = 'STALE' if ru else 'NEW'
                cursor.execute('''
                    UPDATE translations 
                    SET zh=?, en=?, status=?
                    WHERE bundle=? AND type=? AND game_id=? AND key_column=?
                ''', (item["zh"], item["en"], new_status, item["bundle"], item["type"], item["game_id"], item["key_column"]))
                stats["STALE"] += 1
            else:
                # Китайский не менялся, просто обновляем английский (на случай фиксов от разрабов)
                cursor.execute('''
                    UPDATE translations 
                    SET en=?
                    WHERE bundle=? AND type=? AND game_id=? AND key_column=?
                ''', (item["en"], item["bundle"], item["type"], item["game_id"], item["key_column"]))
                stats["UPDATED_EN"] += 1

    conn.commit()
    print("✅ Синхронизация завершена. Статистика изменений:")
    for k, v in stats.items():
        print(f"  {k}: {v}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Синхронизация локализации с SQLite БД.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    # Команда init
    parser_init = subparsers.add_parser('init', help="Собрать переводы из готового патча в БД")
    parser_init.add_argument('--patch', type=str, default=str(Path('dist') / 'RUS-patch'), help="Путь к патчу с переводом")
    parser_init.add_argument('--game', type=str, default=str(DEFAULT_GAME_DIR), help="Путь к чистой игре (для извлечения ZH)")

    # Команда sync
    parser_sync = subparsers.add_parser('sync', help="Синхронизировать БД с текущей игрой")
    parser_sync.add_argument('--game', type=str, default=str(DEFAULT_GAME_DIR), help="Путь к папке игры")

    args = parser.parse_args()

    if args.command == 'init':
        handle_init(args)
    elif args.command == 'sync':
        handle_sync(args)
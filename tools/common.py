# -*- coding: utf-8 -*-
"""Общие помощники для набора инструментов русификации Witch's Apocalyptic Journey."""
import csv
import io
import json
import re
import sys
from pathlib import Path

import UnityPy

KIT_DIR = Path(__file__).resolve().parent.parent
DEFAULT_GAME_DIR = KIT_DIR.parent / "Witch's Apocalyptic Journey"
DIST_DIR = KIT_DIR / "dist"
WORKSPACE_DIR = KIT_DIR / "workspace"
DEFAULT_OLD_PATCH_DIR = DIST_DIR / "RUS-patch"
TM_PATH = WORKSPACE_DIR / "tm.json"

DATA_SUBDIR = "Witch's Apocalyptic Journey_Data"
AA_SUBPATH = Path(DATA_SUBDIR) / "StreamingAssets" / "aa"
BUNDLES_SUBPATH = AA_SUBPATH / "StandaloneWindows64"
TEXT_BUNDLES_SUBPATH = BUNDLES_SUBPATH / "dataconfig_assets_dataconfigs" / "text"

EN_STRINGTABLES_BUNDLE = "localization-string-tables-english(en)_assets_all.bundle"
ZH_STRINGTABLES_BUNDLE = "localization-string-tables-chinese(simplified)(zh-cn)_assets_all.bundle"
SHARED_ASSETS_BUNDLE = "localization-assets-shared_assets_all.bundle"

CYRILLIC_RE = re.compile(r"[Ѐ-ӿ]")


def has_cyrillic(s: str) -> bool:
    return bool(CYRILLIC_RE.search(s or ""))


def load_bundle(path: Path) -> UnityPy.Environment:
    if not path.exists():
        sys.exit(f"Не найден файл: {path}")
    return UnityPy.load(str(path))


def read_string_tables(bundle_path: Path):
    """Возвращает {имя_таблицы: {'obj': obj, 'tree': typetree}} для StringTable MonoBehaviour'ов."""
    env = load_bundle(bundle_path)
    tables = {}
    for obj in env.objects:
        if obj.type.name == "MonoBehaviour":
            tt = obj.read_typetree()
            if "m_TableData" in tt:
                tables[tt.get("m_Name", str(obj.path_id))] = {"obj": obj, "tree": tt}
    return env, tables


def read_shared_key_names(bundle_path: Path):
    """Возвращает {m_Id: имя_ключа} из всех SharedTableData."""
    env = load_bundle(bundle_path)
    keys = {}
    for obj in env.objects:
        if obj.type.name == "MonoBehaviour":
            tt = obj.read_typetree()
            for e in tt.get("m_Entries") or []:
                keys[e["m_Id"]] = e["m_Key"]
    return keys


def read_text_assets(bundle_path: Path):
    """Возвращает {имя_ассета: {'obj': data_obj, 'text': str}} для TextAsset'ов CSV."""
    env = load_bundle(bundle_path)
    assets = {}
    for obj in env.objects:
        if obj.type.name == "TextAsset":
            d = obj.read()
            assets[d.m_Name] = {"data": d, "text": d.m_Script}
    return env, assets


def strip_bom(text: str) -> str:
    return text.lstrip("﻿")


def parse_csv(text: str):
    """CSV -> (header, rows). Разделители/кавычки как в игре (стандартный csv)."""
    reader = csv.reader(io.StringIO(strip_bom(text)))
    rows = list(reader)
    return (rows[0], rows[1:]) if rows else ([], [])


def dump_csv(header, rows, crlf: bool) -> str:
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n" if crlf else "\n")
    w.writerow(header)
    w.writerows(rows)
    return "﻿" + buf.getvalue()


def text_bundle_key(filename: str) -> str:
    """'dialogue_29c63b72....bundle' -> 'dialogue' (имя без хеша содержимого)."""
    return re.sub(r"_[0-9a-f]{32}\.bundle$", "", filename)


def find_text_bundles(root_aa_parent: Path):
    """Возвращает {короткое_имя: путь} для текстовых dataconfig-бандлов."""
    d = root_aa_parent / TEXT_BUNDLES_SUBPATH
    return {text_bundle_key(p.name): p for p in sorted(d.glob("*.bundle"))}


def save_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")


def load_json(path: Path):
    if not path.exists():
        sys.exit(f"Не найден файл: {path} (сначала запустите предыдущий шаг)")
    return json.loads(path.read_text(encoding="utf-8"))

# -*- coding: utf-8 -*-
import os
import sqlite3
import re
import html
from pathlib import Path
from flask import Flask, render_template_string, request, redirect, abort

app = Flask(__name__)

WORKSPACE_DIR = Path("workspace")
DB_PATH = WORKSPACE_DIR / "translations.db"

def get_db():
    if not DB_PATH.exists():
        print(f"❌ Ошибка: База данных {DB_PATH} не найдена. Сначала запустите sync_db.py!")
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn

HTML_TEMPLATE = """
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title>Witch's Apocalyptic Journey - Панель Переводчика</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 0;
            padding: 0;
            background-color: #f5f6f8;
            color: #333;
            display: flex;
            height: 100vh;
            overflow: hidden;
        }
        
        .sidebar {
            width: 300px;
            background-color: #fff;
            border-left: 1px solid #e0e4ec;
            display: flex;
            flex-direction: column;
            height: 100%;
            order: 3;
        }
        
        .sidebar-header {
            padding: 15px 20px;
            background-color: #2c3e50;
            color: #fff;
            margin: 0;
            font-size: 1.1em;
        }
        
        .tree-container {
            flex: 1;
            overflow-y: auto;
            padding: 15px;
        }
        
        .tree-folder {
            font-weight: bold;
            margin-top: 15px;
            margin-bottom: 5px;
            color: #2c3e50;
            font-size: 0.85em;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }
        
        .tree-list {
            list-style: none;
            padding-left: 10px;
            margin: 0;
        }
        
        .tree-item {
            margin: 4px 0;
        }
        
        .tree-link {
            display: block;
            padding: 6px 8px;
            color: #5a6c7d;
            text-decoration: none;
            border-radius: 4px;
            font-size: 0.85em;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            transition: all 0.2s;
        }
        
        .tree-link:hover {
            background-color: #f0f2f5;
            color: #2c3e50;
        }
        
        .tree-link.active {
            background-color: #3498db;
            color: #fff;
            font-weight: 500;
        }
        
        .rows-sidebar {
            width: 350px;
            background-color: #fff;
            border-right: 1px solid #e0e4ec;
            display: flex;
            flex-direction: column;
            height: 100%;
            order: 1;
        }
        
        .rows-header {
            padding: 15px;
            background-color: #f8f9fa;
            border-bottom: 1px solid #e0e4ec;
            font-weight: 600;
            font-size: 0.95em;
        }
        
        .rows-container {
            flex: 1;
            overflow-y: auto;
        }
        
        .row-item-link {
            display: flex;
            align-items: center;
            padding: 10px 15px;
            text-decoration: none;
            color: #333;
            border-bottom: 1px solid #f0f2f5;
            font-size: 0.85em;
            transition: background 0.1s;
        }
        
        .row-item-link:hover {
            background-color: #f8fafc;
        }
        
        .row-item-link.active {
            background-color: #eef6fc;
            border-left: 4px solid #3498db;
            padding-left: 11px;
        }
        
        .row-info {
            flex: 1;
            overflow: hidden;
        }
        
        .row-meta {
            display: flex;
            justify-content: space-between;
            margin-bottom: 3px;
            color: #7f8c8d;
            font-size: 0.8em;
        }
        
        .row-preview {
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            color: #555;
        }
        
        .main-content {
            flex: 1;
            padding: 30px;
            overflow-y: auto;
            box-sizing: border-box;
            order: 2;
            display: flex;
            flex-direction: column;
        }
        
        .editor-header {
            margin-bottom: 25px;
            border-bottom: 1px solid #e0e4ec;
            padding-bottom: 15px;
        }
        
        .editor-header h2 {
            margin: 0 0 5px 0;
            color: #2c3e50;
            font-size: 1.4em;
        }
        
        .editor-header p {
            margin: 0;
            color: #7f8c8d;
            font-size: 0.9em;
        }
        
        .text-card {
            background: #fff;
            border-radius: 6px;
            padding: 15px;
            margin-bottom: 20px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.05);
            border-left: 4px solid #bdc3c7;
        }
        
        .card-label {
            font-size: 0.75em;
            font-weight: bold;
            color: #7f8c8d;
            text-transform: uppercase;
            margin-bottom: 8px;
            letter-spacing: 0.5px;
        }
        
        .card-content {
            font-size: 1.05em;
            line-height: 1.5;
            word-wrap: break-word;
            white-space: pre-wrap;
        }
        
        .card-zh { border-left-color: #2c3e50; font-family: sans-serif; }
        .card-en { border-left-color: #7f8c8d; color: #4f5d65; }
        .card-ru { border-left-color: #27ae60; }
        
        .ru-textarea {
            width: 100%;
            min-height: 120px;
            box-sizing: border-box;
            padding: 12px;
            border: 1px solid #cbd5e1;
            border-radius: 6px;
            font-size: 1.05em;
            font-family: inherit;
            resize: vertical;
            margin-bottom: 20px;
            box-shadow: inset 0 1px 2px rgba(0,0,0,0.02);
        }
        
        .ru-textarea:focus {
            border-color: #3498db;
            outline: none;
            box-shadow: 0 0 0 3px rgba(52, 152, 219, 0.15);
        }
        
        .controls-panel {
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        
        .btn {
            padding: 10px 20px;
            font-size: 0.95em;
            font-weight: 500;
            border-radius: 5px;
            cursor: pointer;
            border: 1px solid transparent;
            text-decoration: none;
            display: inline-flex;
            align-items: center;
            transition: all 0.1s;
        }
        
        .btn-secondary {
            background-color: #fff;
            color: #475569;
            border-color: #cbd5e1;
        }
        
        .btn-secondary:hover {
            background-color: #f8fafc;
            border-color: #94a3b8;
        }
        
        .btn-primary {
            background-color: #27ae60;
            color: #fff;
        }
        
        .btn-primary:hover {
            background-color: #219653;
        }
        
        .btn-disabled {
            background-color: #e2e8f0;
            color: #94a3b8;
            cursor: not-allowed;
            pointer-events: none;
        }
        
        .status-badge {
            display: inline-block;
            padding: 3px 6px;
            border-radius: 3px;
            font-size: 0.75em;
            font-weight: bold;
            text-align: center;
            margin-left: 8px;
        }
        
        .status-OK { background-color: #ccff00; color: #2d3b00; }
        .status-STALE { background-color: #ff9900; color: #fff; }
        .status-NEW { background-color: #ff3333; color: #fff; }
        
        .var-highlight {
            color: #d35400;
            border-bottom: 1px dashed #d35400;
            cursor: help;
            font-style: italic;
            padding: 0 2px;
            background-color: rgba(211, 84, 0, 0.05);
            border-radius: 2px;
        }
        
        .var-missing {
            color: #7f8c8d;
            border-bottom: 1px dashed #7f8c8d;
            background-color: rgba(127, 140, 141, 0.05);
        }
        
        .welcome-msg {
            text-align: center;
            margin-top: 150px;
            color: #7f8c8d;
        }
    </style>
    <script>
        function confirmSave() {
            return confirm("Вы уверены, что хотите сохранить изменения для этой строки?");
        }
        
        document.addEventListener("DOMContentLoaded", function() {
            var activeRow = document.querySelector('.row-item-link.active');
            if (activeRow) {
                activeRow.scrollIntoView({ block: 'center', behavior: 'instant' });
            }
            
            var activeBundle = document.querySelector('.tree-link.active');
            if (activeBundle) {
                activeBundle.scrollIntoView({ block: 'center', behavior: 'instant' });
            }
            
            var textarea = document.querySelector('.ru-textarea');
            if (textarea) {
                textarea.style.height = 'auto';
                textarea.style.height = (textarea.scrollHeight + 2) + 'px';
            }
        });
        
        function wrapWithBr(s, maxWidth) {
            // Токены: тег <...> (включая уже существующие <br>), пробельный run,
            // либо "слово" (кусок без пробелов и без '<').
            const tokenRe = /<[^>]+>|\s+|[^\s<]+/g;
            const tokens = s.match(tokenRe) || [];

            let out = [];
            let currentLen = 0;

            for (let i = 0; i < tokens.length; i++) {
                const tok = tokens[i];

                if (tok.startsWith('<') && tok.endsWith('>')) {
                    out.push(tok);
                    const lower = tok.toLowerCase();
                    if (lower === '<br>' || lower === '<br/>' || lower === '<br />') {
                        currentLen = 0;
                    }
                    continue;
                }

                if (/^\s+$/.test(tok)) {
                    // Если внутри пробельного токена есть настоящий перенос строки (\\n) —
                    // это уже жёсткий разрыв (как <br>), сбрасываем счётчик и не трогаем.
                    if (tok.includes('\\n')) {
                        out.push(tok);
                        currentLen = 0;
                        continue;
                    }

                    // смотрим вперёд на следующее "слово" (пропуская теги)
                    let j = i + 1;
                    let nextWordLen = 0;
                    while (j < tokens.length && tokens[j].startsWith('<') && tokens[j].endsWith('>')) {
                        j++;
                    }
                    if (j < tokens.length && !/^\s+$/.test(tokens[j])) {
                        nextWordLen = tokens[j].length;
                    }

                    if (currentLen + nextWordLen > maxWidth && currentLen > 0) {
                        out.push('<br>');
                        currentLen = 0;
                    } else {
                        out.push(tok);
                    }
                    continue;
                }

                // обычное слово
                out.push(tok);
                currentLen += tok.length;
            }

            return out.join('');
        }

        function wrapSelectedTextarea() {
            const textarea = document.querySelector('.ru-textarea');
            if (!textarea) return;

            const maxWidthStr = prompt('Максимальная ширина строки (в символах):', '28');
            if (maxWidthStr === null) return; // отмена

            const maxWidth = parseInt(maxWidthStr, 10);
            if (isNaN(maxWidth) || maxWidth <= 0) {
                alert('Введите положительное число.');
                return;
            }

            const start = textarea.selectionStart;
            const end = textarea.selectionEnd;
            const fullText = textarea.value;

            if (start !== end) {
                // Есть выделение — обрабатываем только его
                const before = fullText.slice(0, start);
                const selected = fullText.slice(start, end);
                const after = fullText.slice(end);

                const wrapped = wrapWithBr(selected, maxWidth);

                textarea.value = before + wrapped + after;

                // Восстанавливаем выделение на новый (изменившийся по длине) кусок
                textarea.selectionStart = start;
                textarea.selectionEnd = start + wrapped.length;
                textarea.focus();
            } else {
                // Выделения нет — обрабатываем весь текст, как раньше
                textarea.value = wrapWithBr(fullText, maxWidth);
            }
        }
        
        document.addEventListener('keydown', function(e) {
            if (e.ctrlKey && e.key === 'Enter') {
                var form = document.getElementById('editor-form');
                if (form && confirmSave()) {
                    form.submit();
                }
            }
        });
    </script>
</head>
<body>

    <div class="rows-sidebar">
        {% if all_rows %}
            <div class="rows-header">
                {% if search_query and active_row %}
                    Результаты поиска ({{ all_rows|length }})
                {% else %}
                    Строки бандла ({{ all_rows|length }})
                {% endif %}
            </div>
            <div class="rows-container">
                {% for r in all_rows %}
                    <a href="/?bundle={{ r.bundle }}&row_id={{ r.id }}{% if search_query %}&q={{ search_query }}&col={{ search_col }}{% endif %}" 
                       class="row-item-link {% if active_row and active_row.id == r.id %}active{% endif %}">
                        <div class="row-info">
                            <div class="row-meta">
                                <span style="white-space:nowrap; overflow:hidden; text-overflow:ellipsis; max-width:75%;" title="{{ r.bundle }} | ID: {{ r.game_id }} | {{ r.key_column }}">
                                    {% if search_query and active_row %}[{{ r.bundle }}] {% endif %}ID: {{ r.game_id }} {% if r.key_column %} | {{ r.key_column }}{% endif %}
                                </span>
                                <span class="status-badge status-{{ r.status }}">{{ r.status }}</span>
                            </div>
                            <div class="row-preview">
                                {% if r.ru %}{{ r.ru }}{% else %}[EN] {{ r.en }}{% endif %}
                            </div>
                        </div>
                    </a>
                {% endfor %}
            </div>
        {% else %}
            <div class="rows-header" style="color:#bdc3c7; font-style:italic;">
                {% if search_query and not active_row %}Режим поиска{% else %}Файл не выбран{% endif %}
            </div>
        {% endif %}
    </div>

    <div class="main-content">
        {% if search_query and not active_row %}
            <div class="editor-header">
                <h2>Результаты поиска: "{{ search_query }}"</h2>
                <p>Найдено совпадений: <strong>{{ search_results|length }}</strong></p>
            </div>
            
            {% if search_results %}
                <table style="width: 100%; border-collapse: collapse; background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,0.05); border-radius: 6px; overflow: hidden; table-layout: fixed;">
                    <thead style="background-color: #f8f9fa; border-bottom: 2px solid #e2e8f0; text-align: left;">
                        <tr>
                            <th style="padding: 12px; font-size: 0.85em; color: #7f8c8d; text-transform: uppercase; width: 20%;">Бандл / ID</th>
                            <th style="padding: 12px; font-size: 0.85em; color: #7f8c8d; text-transform: uppercase; width: 35%;">EN / ZH</th>
                            <th style="padding: 12px; font-size: 0.85em; color: #7f8c8d; text-transform: uppercase; width: 35%;">RU</th>
                            <th style="padding: 12px; font-size: 0.85em; color: #7f8c8d; text-transform: uppercase; width: 10%;">Действие</th>
                        </tr>
                    </thead>
                    <tbody>
                        {% for r in search_results %}
                        <tr style="border-bottom: 1px solid #eef2f5;">
                            <td style="padding: 12px; font-size: 0.9em; vertical-align: top;">
                                <div style="font-weight: bold; color: #2c3e50; margin-bottom: 4px; word-wrap: break-word;">{{ r.bundle }}</div>
                                <div style="color: #7f8c8d; font-size: 0.85em;">ID: {{ r.game_id }}</div>
                                <div style="margin-top: 4px;"><span class="status-badge status-{{ r.status }}" style="margin-left: 0;">{{ r.status }}</span></div>
                            </td>
                            <td style="padding: 12px; font-size: 0.95em; vertical-align: top; color: #4f5d65; word-wrap: break-word; white-space: pre-wrap;">
                                <div style="margin-bottom: 8px; border-bottom: 1px dashed #eef2f5; padding-bottom: 8px;">{{ r.en }}</div>
                                <div style="font-size: 0.9em; color: #95a5a6; font-family: sans-serif;">{{ r.zh }}</div>
                            </td>
                            <td style="padding: 12px; font-size: 0.95em; vertical-align: top; color: #27ae60; word-wrap: break-word; white-space: pre-wrap;">
                                {{ r.ru or '<span style="color:#bdc3c7; font-style:italic;">[Пусто]</span>'|safe }}
                            </td>
                            <td style="padding: 12px; vertical-align: top; text-align: center;">
                                <a href="/?bundle={{ r.bundle }}&row_id={{ r.id }}&q={{ search_query }}&col={{ search_col }}" class="btn btn-secondary" style="font-size: 0.8em; padding: 6px 12px; display: inline-block;">Править</a>
                            </td>
                        </tr>
                        {% endfor %}
                    </tbody>
                </table>
            {% else %}
                <div style="padding: 40px; text-align: center; color: #7f8c8d; background: #fff; border-radius: 6px; border: 1px dashed #cbd5e1;">
                    По запросу <strong>"{{ search_query }}"</strong> ничего не найдено.<br>Попробуйте изменить слово или колонку поиска.
                </div>
            {% endif %}

        {% elif active_row %}
            <div class="editor-header">
                {% if search_query %}
                    <a href="/?q={{ search_query }}&col={{ search_col }}" style="font-size: 0.85em; color: #3498db; text-decoration: none; margin-bottom: 10px; display: inline-block;">← Вернуться к таблице поиска</a>
                {% endif %}
                <h2>Редактирование строки</h2>
                <p>Бандл: <strong>{{ current_bundle }}</strong> | Игровой ID: <code>{{ active_row.game_id }}</code> | Ключ/Колонка: <code>{{ active_row.key_column }}</code></p>
            </div>
            
            <div class="text-card card-zh">
                <div class="card-label">Оригинал (ZH)</div>
                <div class="card-content">{{ active_row.zh_html | safe }}</div>
            </div>
            
            <div class="text-card card-en">
                <div class="card-label">Английский (EN)</div>
                <div class="card-content">{{ active_row.en_html | safe }}</div>
            </div>
            
            <form id="editor-form" action="/save" method="POST" onsubmit="return confirmSave();">
                <input type="hidden" name="row_id" value="{{ active_row.id }}">
                <input type="hidden" name="next_url" value="{{ next_url or '' }}">
                <input type="hidden" name="current_url" value="{{ request.full_path }}">
                
                <div class="card-label" style="color: #27ae60;">Перевод (RU) <span style="font-weight:normal; text-transform:none; color:#7f8c8d;">(Ctrl+Enter для сохранения)</span></div>
                <textarea name="ru_text" class="ru-textarea" placeholder="Введите русский перевод... Если оставить пустым, игра подставит английский text.">{{ active_row.ru }}</textarea>
                
                <div class="controls-panel">
                    <div>
                        {% if prev_url %}
                            <a href="{{ prev_url }}" class="btn btn-secondary"><- Назад</a>
                        {% else %}
                            <span class="btn btn-secondary btn-disabled"><- Назад</span>
                        {% endif %}
                        
                        {% if next_url %}
                            <a href="{{ next_url }}" class="btn btn-secondary" style="margin-left:10px;">Пропустить -></a>
                        {% else %}
                            <span class="btn btn-secondary btn-disabled" style="margin-left:10px;">Пропустить -></span>
                        {% endif %}
                    </div>
                    <button type="button" class="btn btn-secondary" onclick="wrapSelectedTextarea()">Перенос слов</button>
                    <button type="submit" class="btn btn-primary">Сохранить и Дальше -></button>
                </div>
            </form>
        {% else %}
            <div class="welcome-msg">
                <h2>Панель управления локализации</h2>
                <p>Выберите файл в правом меню или воспользуйтесь глобальным поиском.</p>
            </div>
        {% endif %}
    </div>

    <div class="sidebar">
        <div style="padding: 15px; border-bottom: 1px solid #e0e4ec; background-color: #f8f9fa;">
            <form action="/" method="GET" style="display: flex; margin: 0;">
                <select name="col" style="padding: 8px 5px; border: 1px solid #cbd5e1; border-right: none; border-radius: 4px 0 0 4px; outline: none; font-size: 0.85em; background: #fff; cursor: pointer;">
                    <option value="all" {% if search_col == 'all' %}selected{% endif %}>Везде</option>
                    <option value="ru" {% if search_col == 'ru' %}selected{% endif %}>RU</option>
                    <option value="en" {% if search_col == 'en' %}selected{% endif %}>EN</option>
                    <option value="zh" {% if search_col == 'zh' %}selected{% endif %}>ZH</option>
                </select>
                <input type="text" name="q" value="{{ search_query or '' }}" placeholder="Поиск..." style="flex: 1; padding: 8px 10px; border: 1px solid #cbd5e1; border-radius: 0; font-size: 0.9em; outline: none; min-width: 0;">
                <button type="submit" class="btn btn-primary" style="border-radius: 0 4px 4px 0; padding: 8px 12px; border: none;">🔍</button>
            </form>
        </div>
        
        <h3 class="sidebar-header">Бандлы текста</h3>
        <div class="tree-container">
            {% for cat, bundles in tree.items() %}
                <div class="tree-folder">{{ cat }}s</div>
                <ul class="tree-list">
                    {% for b in bundles %}
                        <li class="tree-item">
                            <a href="/?bundle={{ b }}" 
                               class="tree-link {% if current_bundle == b %}active{% endif %}"
                               title="{{ b }}">
                                {{ b }}
                            </a>
                        </li>
                    {% endfor %}
                </ul>
            {% endfor %}
        </div>
    </div>

</body>
</html>
"""

def build_global_lookup():
    lookup = {}
    conn = get_db()
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT game_id, key_column, ru, en, zh FROM translations")
        for row in cursor.fetchall():
            text = row['ru'] or row['en'] or row['zh']
            if not text:
                continue
            gid = str(row['game_id'])
            col_name = row['key_column']
            
            if gid:
                if gid not in lookup:
                    lookup[gid] = text
                elif 'name' in col_name.lower() or 'title' in col_name.lower():
                    lookup[gid] = text
            if col_name:
                lookup[col_name] = text
    except Exception as e:
        pass
    finally:
        conn.close()
    return lookup

def format_text_with_vars(text, lookup):
    if not text:
        return ""
    escaped_text = html.escape(text)
    def replace_var(match):
        full_var = match.group(0)
        var_name = match.group(1)
        translated_val = lookup.get(var_name)
        if not translated_val and var_name.startswith('buff_'):
            translated_val = lookup.get(var_name[5:])
        if translated_val:
            safe_val = html.escape(translated_val)
            return f'<i title="{full_var}" class="var-highlight">{safe_val}</i>'
        else:
            return f'<i title="{full_var} (не найдено в базе)" class="var-highlight var-missing">{var_name}</i>'
    return re.sub(r'\{([^}]+)\}', replace_var, escaped_text)

def get_bundle_tree():
    tree = {"stringtable": [], "dataconfig": []}
    conn = get_db()
    cursor = conn.cursor()
    try:
        cursor.execute("SELECT DISTINCT type, bundle FROM translations ORDER BY type, bundle")
        for row in cursor.fetchall():
            if row['type'] in tree:
                tree[row['type']].append(row['bundle'])
    except Exception:
        pass
    finally:
        conn.close()
    return tree

@app.route("/")
def index():
    tree = get_bundle_tree()
    current_bundle = request.args.get("bundle")
    requested_row_id = request.args.get("row_id")
    search_query = request.args.get("q", "")
    search_col = request.args.get("col", "all")
    
    all_rows = []
    active_row = None
    prev_url = None
    next_url = None
    search_results = []
    
    # 1. Если активен поиск
    if search_query:
        conn = get_db()
        cursor = conn.cursor()
        try:
            like_q = f"%{search_query}%"
            if search_col == 'ru':
                cursor.execute("SELECT id, bundle, game_id, key_column, zh, en, ru, status FROM translations WHERE ru LIKE ? ORDER BY bundle, id", (like_q,))
            elif search_col == 'en':
                cursor.execute("SELECT id, bundle, game_id, key_column, zh, en, ru, status FROM translations WHERE en LIKE ? ORDER BY bundle, id", (like_q,))
            elif search_col == 'zh':
                cursor.execute("SELECT id, bundle, game_id, key_column, zh, en, ru, status FROM translations WHERE zh LIKE ? ORDER BY bundle, id", (like_q,))
            else:
                cursor.execute("SELECT id, bundle, game_id, key_column, zh, en, ru, status FROM translations WHERE zh LIKE ? OR en LIKE ? OR ru LIKE ? ORDER BY bundle, id", (like_q, like_q, like_q))
                
            search_results = [dict(r) for r in cursor.fetchall()]
            
            # Если в поиске кликнули на кнопку "Править" (режим редактирования из поиска)
            if requested_row_id and search_results:
                requested_row_id = int(requested_row_id)
                active_idx = 0
                for idx, r in enumerate(search_results):
                    if r['id'] == requested_row_id:
                        active_idx = idx
                        break
                        
                active_row = dict(search_results[active_idx])
                current_bundle = active_row['bundle'] # Чтобы дерево справа подсвечивало нужный бандл
                all_rows = search_results # Подменяем левое меню на результаты поиска
                
                # Формируем ссылки Вперед/Назад внутри результатов поиска
                if active_idx > 0:
                    pr = search_results[active_idx - 1]
                    prev_url = f"/?bundle={pr['bundle']}&row_id={pr['id']}&q={search_query}&col={search_col}"
                if active_idx < len(search_results) - 1:
                    nx = search_results[active_idx + 1]
                    next_url = f"/?bundle={nx['bundle']}&row_id={nx['id']}&q={search_query}&col={search_col}"
                    
                global_lookup = build_global_lookup()
                active_row['zh_html'] = format_text_with_vars(active_row.get('zh', ''), global_lookup)
                active_row['en_html'] = format_text_with_vars(active_row.get('en', ''), global_lookup)
                
        except Exception as e:
            print(f"Ошибка поиска: {e}")
        finally:
            conn.close()
            
    # 2. Если просто выбран бандл (обычный режим)
    elif current_bundle:
        conn = get_db()
        cursor = conn.cursor()
        try:
            cursor.execute('''
                SELECT id, bundle, game_id, key_column, en, ru, status FROM translations 
                WHERE bundle=? ORDER BY id
            ''', (current_bundle,))
            all_rows = [dict(r) for r in cursor.fetchall()]
            
            if all_rows:
                active_idx = 0
                if requested_row_id:
                    requested_row_id = int(requested_row_id)
                    for idx, r in enumerate(all_rows):
                        if r['id'] == requested_row_id:
                            active_idx = idx
                            break
                
                target_id = all_rows[active_idx]['id']
                cursor.execute("SELECT * FROM translations WHERE id=?", (target_id,))
                active_row = dict(cursor.fetchone())
                
                # Формируем ссылки Вперед/Назад внутри текущего бандла
                if active_idx > 0:
                    pr = all_rows[active_idx - 1]
                    prev_url = f"/?bundle={current_bundle}&row_id={pr['id']}"
                if active_idx < len(all_rows) - 1:
                    nx = all_rows[active_idx + 1]
                    next_url = f"/?bundle={current_bundle}&row_id={nx['id']}"
                
                global_lookup = build_global_lookup()
                active_row['zh_html'] = format_text_with_vars(active_row.get('zh', ''), global_lookup)
                active_row['en_html'] = format_text_with_vars(active_row.get('en', ''), global_lookup)
                
        except Exception as e:
            print(f"Ошибка загрузки редактора: {e}")
        finally:
            conn.close()
            
    return render_template_string(
        HTML_TEMPLATE,
        tree=tree,
        current_bundle=current_bundle,
        all_rows=all_rows,
        active_row=active_row,
        prev_url=prev_url,
        next_url=next_url,
        search_query=search_query,
        search_col=search_col,
        search_results=search_results
    )

@app.route("/save", methods=["POST"])
def save():
    row_id = request.form.get("row_id")
    ru_text = request.form.get("ru_text", "")
    next_url = request.form.get("next_url")
    current_url = request.form.get("current_url")
    
    if not row_id:
        abort(400)
        
    conn = get_db()
    cursor = conn.cursor()
    try:
        new_status = "OK" if ru_text else "NEW"
        cursor.execute('''
            UPDATE translations 
            SET ru=?, status=? 
            WHERE id=?
        ''', (ru_text, new_status, row_id))
        conn.commit()
    except Exception as e:
        print(f"Ошибка сохранения строки {row_id}: {e}")
    finally:
        conn.close()
        
    # Если есть URL следующей строки (в бандле или в поиске) — переходим на нее. 
    # Если мы дошли до конца списка — остаемся на текущей странице (current_url).
    if next_url:
        return redirect(next_url)
    return redirect(current_url or "/")

if __name__ == "__main__":
    if not DB_PATH.exists():
        print(f"🛑 ОШИБКА: База данных не обнаружена по пути {DB_PATH}.")
        print("Сначала соберите базу командой: python sync_db.py init")
        exit(1)
        
    print("🚀 Запуск сервера CAT-панели...")
    print("Откройте ссылку в браузере: http://127.0.0.1:5000/")
    app.run(debug=True, port=5000)
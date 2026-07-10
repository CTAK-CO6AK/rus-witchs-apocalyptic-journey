# -*- coding: utf-8 -*-
"""Точечно зануляет CRC изменённых бандлов в бинарном catalog.bin (Addressables 2.x).

Каталог НЕ пересериализуется: меняются только 4 байта CRC в записи
AssetBundleRequestOptions (ABRO) каждого указанного бандла. CRC = 0 означает
для Addressables «не проверять целостность».

Раскладка ABRO-записи в бинарном каталоге (проверена по реальному каталогу игры):
    +0  u32 hashOffset        -> 16 сырых байт Hash128 бандла
    +4  u32 bundleNameOffset  -> строка [len=32:u32]["<32 hex-символа>"]
    +8  u32 crc               <- зануляем
    +12 u32 bundleSize        (== размеру файла бандла)
    +16 u32 commonInfoOffset

Поиск: находим все 32-символьные hex-строки (внутренние имена бандлов),
затем все u32-ссылки на них — это кандидаты bundleNameOffset. Запись считается
найденной для целевого бандла, если bundleSize == размеру его оригинального
файла, а если имя файла содержит хеш (name_<32hex>.bundle) — ещё и 16 байт
по hashOffset совпадают с этим хешем.
"""
import re
import struct
import sys
from pathlib import Path

HEXSTR_RE = re.compile(rb"\x20\x00\x00\x00[0-9a-f]{32}")
NAME_HASH_RE = re.compile(r"_([0-9a-f]{32})\.bundle$")


def find_abro_records(data: bytes):
    """Все ABRO-кандидаты: список (record_pos, crc, size, hash16)."""
    records = []
    name_offsets = [m.start() + 4 for m in HEXSTR_RE.finditer(data)]
    for noff in name_offsets:
        needle = struct.pack("<I", noff)
        q = 0
        while True:
            q = data.find(needle, q)
            if q < 0:
                break
            p = q - 4  # начало записи
            if p >= 0 and p + 20 <= len(data):
                hoff, _, crc, size, cioff = struct.unpack_from("<5I", data, p)
                if 0 < hoff + 16 <= len(data) and 0 < size and cioff <= len(data):
                    records.append((p, crc, size, data[hoff:hoff + 16]))
            q += 1
    return records


def zero_crc(data: bytearray, bundle_name: str, orig_size: int, records=None):
    """Зануляет CRC записи бандла bundle_name. Возвращает (найдено, занулено)."""
    if records is None:
        records = find_abro_records(bytes(data))
    m = NAME_HASH_RE.search(bundle_name)
    want_hash = bytes.fromhex(m.group(1)) if m else None

    found = patched = 0
    for p, crc, size, hash16 in records:
        if size != orig_size:
            continue
        if want_hash is not None and hash16 != want_hash:
            continue
        found += 1
        if crc != 0:
            struct.pack_into("<I", data, p + 8, 0)
            print(f"  {bundle_name}: crc {crc:08x} -> 0 (offset 0x{p + 8:x})")
            patched += 1
    return found, patched


def main():
    if len(sys.argv) < 5:
        sys.exit(__doc__)
    src, dst, orig_dir = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    names = sys.argv[4:]

    data = bytearray(src.read_bytes())
    records = find_abro_records(bytes(data))
    total, missing = 0, []
    for name in names:
        matches = list(orig_dir.rglob(name))
        if not matches:
            missing.append(name)
            continue
        found, patched = zero_crc(data, name, matches[0].stat().st_size, records)
        if found == 0:
            missing.append(name)
        total += patched

    if missing:
        sys.exit(f"ОШИБКА: не найдены записи CRC для: {missing} — каталог не сохранён.")
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(bytes(data))
    print(f"Занулено CRC: {total} записей -> {dst}")


if __name__ == "__main__":
    main()

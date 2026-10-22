# tools/dbf_dump.py — независимый читатель dBase III для золотых файлов. Запуск:
#   python tools/dbf_dump.py <file.dbf> [--head N] > out.json
import json, struct, sys
from pathlib import Path

def read(path: Path, head: int | None = None):
    b = path.read_bytes()
    n, hdr, rec = struct.unpack_from("<IHH", b, 4)
    cols, o = [], 32
    while b[o] != 0x0D:
        name = b[o:o+11].split(b"\0")[0].decode("ascii")
        cols.append((name, chr(b[o+11]), b[o+16], b[o+17])); o += 32
    rows, p = [], hdr
    for _ in range(n if head is None else min(n, head)):
        if b[p] == 0x2A: p += rec; continue          # удалённая запись
        q, row = p + 1, {}
        for name, typ, ln, _dec in cols:
            raw = b[q:q+ln]; q += ln
            if typ == "N":
                s = raw.strip(); row[name] = int(s) if s.lstrip(b"-").isdigit() else (float(s) if s else 0)
            elif typ == "L": row[name] = raw in (b"T", b"t", b"Y", b"y")
            else: row[name] = raw.decode("cp866").rstrip()
        rows.append(row); p += rec
    return {"columns": [c[0] for c in cols], "rows": rows}

if __name__ == "__main__":
    a = sys.argv[1:]; head = int(a[a.index("--head")+1]) if "--head" in a else None
    print(json.dumps(read(Path(a[0]), head), ensure_ascii=False, indent=0))

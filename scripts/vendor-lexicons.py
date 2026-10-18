#!/usr/bin/env python3
"""Vendors the first-party Lexicons into lexicons/ and writes lexicons.json.

Stands in for `atproto-lexgen install|update` until phase 3 builds it (TOO-14..18).

  scripts/vendor-lexicons.py                  recompute lexicons.json from lexicons/ (no network)
  scripts/vendor-lexicons.py --fetch [--atproto <commit>] [--spaces <commit>]
                                              re-copy every source at its pin, then recompute

Sources and pins live in lexicons.json ("sources"); --atproto/--spaces move a pin. Each NSID's
CID is the CID of its schema in record form ({"$type": "com.atproto.lexicon.schema", ...}),
DRISL-CBOR encoded (LEX-23), so a file published unchanged has its record's CID.
"""
import argparse, base64, hashlib, io, json, pathlib, shutil, tarfile, tempfile, urllib.request
from datetime import date

ROOT = pathlib.Path(__file__).resolve().parent.parent
LEXICONS, MANIFEST = ROOT / "lexicons", ROOT / "lexicons.json"


def cbor_head(major, n):
    if n < 24: return bytes([major << 5 | n])
    for info, size in ((24, 1), (25, 2), (26, 4), (27, 8)):
        if n < 1 << 8 * size: return bytes([major << 5 | info]) + n.to_bytes(size, "big")
    raise ValueError(n)


def drisl(value):
    if value is None: return b"\xf6"
    if value is True: return b"\xf5"
    if value is False: return b"\xf4"
    if isinstance(value, int): return cbor_head(0, value) if value >= 0 else cbor_head(1, -1 - value)
    if isinstance(value, str):
        data = value.encode()
        return cbor_head(3, len(data)) + data
    if isinstance(value, list): return cbor_head(4, len(value)) + b"".join(map(drisl, value))
    if isinstance(value, dict):
        keys = sorted(value, key=lambda k: (len(k.encode()), k.encode()))
        return cbor_head(5, len(keys)) + b"".join(drisl(k) + drisl(value[k]) for k in keys)
    raise TypeError(f"no DRISL form for {type(value).__name__} in a Lexicon")


def cid(document):
    digest = hashlib.sha256(drisl({"$type": "com.atproto.lexicon.schema", **document})).digest()
    raw = bytes([0x01, 0x71, 0x12, 0x20]) + digest
    return "b" + base64.b32encode(raw).decode().lower().rstrip("=")


def get(url, accept=None):
    request = urllib.request.Request(url, headers={"accept": accept} if accept else {})
    with urllib.request.urlopen(request, timeout=60) as response: return response.read()


def copy_git(source, skip=()):
    """Copies the source's namespaces byte for byte from the archive at its commit (TOO-16)."""
    archive = tarfile.open(fileobj=io.BytesIO(get(f"https://codeload.github.com/{source['repository']}/tar.gz/{source['commit']}")))
    with tempfile.TemporaryDirectory() as tmp:
        archive.extractall(tmp, filter="data")
        src = next(pathlib.Path(tmp).iterdir()) / source["path"]
        for ns in source["namespaces"]:
            shutil.rmtree(LEXICONS / ns, ignore_errors=True)
            shutil.copytree(src / ns, LEXICONS / ns, ignore=lambda d, names: [n for n in names if (pathlib.Path(d) / n).relative_to(src).as_posix() in skip])


def copy_network(source):
    pds = next(s["serviceEndpoint"] for s in json.loads(get(f"https://plc.directory/{source['did']}"))["service"] if s["id"] == "#atproto_pds")
    url = f"{pds}/xrpc/com.atproto.repo.listRecords?repo={source['did']}&collection=com.atproto.lexicon.schema&limit=100"
    for record in json.loads(get(url))["records"]:
        value = {k: v for k, v in record["value"].items() if k != "$type"}
        if not value["id"].startswith(source["prefix"]): continue
        path = LEXICONS / (value["id"].replace(".", "/") + ".json")
        path.parent.mkdir(parents=True, exist_ok=True)
        ordered = {"lexicon": value["lexicon"], "id": value["id"], **value}
        path.write_text(json.dumps(ordered, indent=2, ensure_ascii=False) + "\n")


def source_of(nsid, sources):
    for name, source in sources.items():
        if any(nsid.startswith(ns.replace("/", ".") + ".") for ns in source.get("namespaces", [])): return name
        if "prefix" in source and nsid.startswith(source["prefix"]): return name
    raise LookupError(f"{nsid} has no source")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fetch", action="store_true")
    parser.add_argument("--atproto")
    parser.add_argument("--spaces")
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text())
    sources = manifest["sources"]
    if args.atproto: sources["atproto"].update(commit=args.atproto, fetched=str(date.today()))
    if args.spaces: sources["spaces"].update(commit=args.spaces, fetched=str(date.today()))
    if args.fetch:
        copy_git(sources["atproto"], skip=sources["spaces"]["namespaces"])
        copy_git(sources["spaces"])
        copy_network(sources["standard.site"])
        for source in sources.values(): source["fetched"] = str(date.today())
    lexicons = {}
    for path in sorted(LEXICONS.rglob("*.json")):
        document = json.loads(path.read_text())
        lexicons[document["id"]] = {"source": source_of(document["id"], sources), "cid": cid(document)}
    manifest["lexicons"] = dict(sorted(lexicons.items()))
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"{len(lexicons)} Lexicons in lexicons.json")


if __name__ == "__main__":
    main()

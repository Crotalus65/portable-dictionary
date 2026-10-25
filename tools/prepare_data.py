#!/usr/bin/env python3
"""Prepare the offline dictionary database from official OEWN archives.

Uses only the Python standard library. The prepared database is a build artifact;
it is never imported or converted on the user's Windows machine.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import unicodedata
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = ROOT / "tools" / "source_manifest.json"
SCHEMA_VERSION = 1


def lookup_key(value: str) -> str:
    value = unicodedata.normalize("NFC", value).replace("_", " ")
    return " ".join(value.split()).lower()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_sources(manifest: dict, xml_path: Path, wndb_path: Path) -> None:
    expected = {item["file"]: item for item in manifest["archives"]}
    for path in (xml_path, wndb_path):
        item = expected.get(path.name)
        if item is None:
            raise ValueError(f"No pinned manifest entry for {path.name}")
        actual = sha256(path)
        if actual != item["sha256"]:
            raise ValueError(
                f"SHA-256 mismatch for {path.name}: expected {item['sha256']}, got {actual}"
            )


def create_schema(connection: sqlite3.Connection) -> None:
    connection.executescript(
        """
        PRAGMA page_size = 4096;
        PRAGMA journal_mode = OFF;
        PRAGMA synchronous = OFF;
        PRAGMA temp_store = MEMORY;
        PRAGMA foreign_keys = OFF;

        CREATE TABLE metadata (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE entries (
            id INTEGER PRIMARY KEY,
            source_id TEXT NOT NULL UNIQUE,
            lemma TEXT NOT NULL,
            lookup_key TEXT NOT NULL,
            pos TEXT NOT NULL,
            source_order INTEGER NOT NULL
        );

        CREATE TABLE senses (
            id TEXT PRIMARY KEY,
            entry_id INTEGER NOT NULL REFERENCES entries(id),
            synset_id TEXT NOT NULL,
            sense_order INTEGER NOT NULL,
            source_order INTEGER NOT NULL
        );

        CREATE TABLE synsets (
            id TEXT PRIMARY KEY,
            pos TEXT NOT NULL,
            source_order INTEGER NOT NULL
        );

        CREATE TABLE definitions (
            synset_id TEXT NOT NULL REFERENCES synsets(id),
            definition_order INTEGER NOT NULL,
            text TEXT NOT NULL,
            PRIMARY KEY (synset_id, definition_order)
        );

        CREATE TABLE examples (
            synset_id TEXT NOT NULL REFERENCES synsets(id),
            example_order INTEGER NOT NULL,
            text TEXT NOT NULL,
            PRIMARY KEY (synset_id, example_order)
        );

        CREATE TABLE exceptions (
            id INTEGER PRIMARY KEY,
            inflected_key TEXT NOT NULL,
            pos TEXT NOT NULL,
            base_key TEXT NOT NULL,
            source_order INTEGER NOT NULL,
            UNIQUE (inflected_key, pos, base_key)
        );

        CREATE TABLE synset_members (
            member_id INTEGER PRIMARY KEY,
            synset_id TEXT NOT NULL REFERENCES synsets(id),
            lemma TEXT NOT NULL,
            source_order INTEGER NOT NULL,
            sense_order INTEGER NOT NULL
        );
        """
    )


def import_xml(connection: sqlite3.Connection, xml_path: Path) -> dict[str, int | str]:
    counts = {"entries": 0, "senses": 0, "synsets": 0, "definitions": 0, "examples": 0}
    lexicon_id = ""
    lexicon_version = ""
    entry_order = 0
    sense_order = 0
    synset_order = 0

    with gzip.open(xml_path, "rb") as stream:
        for _, element in ET.iterparse(stream, events=("end",)):
            if element.tag == "LexicalEntry":
                lemma_element = element.find("Lemma")
                if lemma_element is None:
                    raise ValueError(f"Lexical entry {element.attrib.get('id')} has no Lemma")
                lemma = lemma_element.attrib["writtenForm"]
                pos = lemma_element.attrib["partOfSpeech"]
                cursor = connection.execute(
                    "INSERT INTO entries(source_id, lemma, lookup_key, pos, source_order) VALUES (?, ?, ?, ?, ?)",
                    (element.attrib["id"], lemma, lookup_key(lemma), pos, entry_order),
                )
                entry_id = cursor.lastrowid
                for local_order, sense in enumerate(element.findall("Sense")):
                    connection.execute(
                        "INSERT INTO senses(id, entry_id, synset_id, sense_order, source_order) VALUES (?, ?, ?, ?, ?)",
                        (sense.attrib["id"], entry_id, sense.attrib["synset"], local_order, sense_order),
                    )
                    sense_order += 1
                entry_order += 1
                counts["entries"] += 1
                counts["senses"] += len(element.findall("Sense"))
                element.clear()
            elif element.tag == "Synset":
                synset_id = element.attrib["id"]
                connection.execute(
                    "INSERT INTO synsets(id, pos, source_order) VALUES (?, ?, ?)",
                    (synset_id, element.attrib["partOfSpeech"], synset_order),
                )
                definitions = element.findall("Definition")
                examples = element.findall("Example")
                for order, definition in enumerate(definitions):
                    connection.execute(
                        "INSERT INTO definitions(synset_id, definition_order, text) VALUES (?, ?, ?)",
                        (synset_id, order, (definition.text or "").strip()),
                    )
                for order, example in enumerate(examples):
                    connection.execute(
                        "INSERT INTO examples(synset_id, example_order, text) VALUES (?, ?, ?)",
                        (synset_id, order, (example.text or "").strip()),
                    )
                synset_order += 1
                counts["synsets"] += 1
                counts["definitions"] += len(definitions)
                counts["examples"] += len(examples)
                element.clear()
            elif element.tag == "Lexicon":
                lexicon_id = element.attrib.get("id", "")
                lexicon_version = element.attrib.get("version", "")

    counts["dataset_id"] = lexicon_id
    counts["dataset_version"] = lexicon_version
    return counts


def import_exceptions(connection: sqlite3.Connection, wndb_path: Path) -> int:
    members = {Path(name).name: name for name in zipfile.ZipFile(wndb_path).namelist()}
    with zipfile.ZipFile(wndb_path) as archive:
        for pos, filename in (("n", "noun.exc"), ("v", "verb.exc"), ("a", "adj.exc"), ("r", "adv.exc")):
            if filename not in members:
                raise ValueError(f"WNDB archive does not contain {filename}")
            with archive.open(members[filename]) as raw:
                for line_order, line in enumerate(raw.read().decode("utf-8-sig").splitlines()):
                    fields = line.split()
                    if not fields or fields[0].startswith("#"):
                        continue
                    inflected = fields[0]
                    for base in fields[1:]:
                        connection.execute(
                            "INSERT OR IGNORE INTO exceptions(inflected_key, pos, base_key, source_order) VALUES (?, ?, ?, ?)",
                            (lookup_key(inflected), pos, lookup_key(base), line_order),
                        )
    return connection.execute("SELECT COUNT(*) FROM exceptions").fetchone()[0]


def write_metadata(connection: sqlite3.Connection, manifest: dict, counts: dict, exception_count: int) -> None:
    xml, wndb = manifest["archives"]
    values = {
        "schema_version": str(SCHEMA_VERSION),
        "dataset_id": counts["dataset_id"],
        "dataset_version": counts["dataset_version"],
        "dataset_name": manifest["datasetName"],
        "source_format": "WN-LMF 1.3 XML + WNDB exception lists",
        "source_xml_url": xml["url"],
        "source_xml_sha256": xml["sha256"],
        "source_wndb_url": wndb["url"],
        "source_wndb_sha256": wndb["sha256"],
        "entry_count": str(counts["entries"]),
        "sense_count": str(counts["senses"]),
        "synset_count": str(counts["synsets"]),
        "definition_count": str(counts["definitions"]),
        "example_count": str(counts["examples"]),
        "exception_mapping_count": str(exception_count),
        "section_order": "validated base forms by POS n,v,a,r; exceptions then rules; source order; exact spellings last",
    }
    connection.executemany("INSERT INTO metadata(key, value) VALUES (?, ?)", values.items())


def prepare(xml_path: Path, wndb_path: Path, output_path: Path, manifest: dict, verify: bool = True) -> None:
    if verify:
        verify_sources(manifest, xml_path, wndb_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = output_path.with_suffix(output_path.suffix + ".tmp")
    temporary.unlink(missing_ok=True)
    connection = sqlite3.connect(temporary)
    try:
        create_schema(connection)
        counts = import_xml(connection, xml_path)
        if counts["dataset_id"] != manifest["datasetId"] or counts["dataset_version"] != manifest["datasetVersion"]:
            raise ValueError(
                f"Unexpected source identity: {counts['dataset_id']}:{counts['dataset_version']}; "
                f"expected {manifest['datasetId']}:{manifest['datasetVersion']}"
            )
        exception_count = import_exceptions(connection, wndb_path)
        connection.execute("""
            INSERT INTO synset_members(synset_id, lemma, source_order, sense_order)
            SELECT s.synset_id, e.lemma, e.source_order, s.sense_order
            FROM senses s JOIN entries e ON e.id = s.entry_id
            ORDER BY s.synset_id, e.source_order, s.sense_order, e.id
        """)
        write_metadata(connection, manifest, counts, exception_count)
        connection.executescript(
            """
            CREATE INDEX idx_entries_lookup ON entries(lookup_key, source_order);
            CREATE INDEX idx_senses_entry ON senses(entry_id, sense_order);
            CREATE INDEX idx_senses_synset ON senses(synset_id, entry_id);
            CREATE INDEX idx_exceptions_lookup ON exceptions(inflected_key, pos, source_order);
            CREATE INDEX idx_members_synset ON synset_members(synset_id, source_order, sense_order, member_id);
            """
        )
        connection.execute(f"PRAGMA user_version = {SCHEMA_VERSION}")
        connection.commit()
        failures = connection.execute("PRAGMA foreign_key_check").fetchall()
        if failures:
            raise ValueError(f"Foreign-key validation failed: {failures[:3]}")
        integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
        if integrity != "ok":
            raise ValueError(f"SQLite integrity check failed: {integrity}")
        connection.execute("ANALYZE")
        connection.commit()
    finally:
        connection.close()
    os.replace(temporary, output_path)
    print(
        f"Prepared {output_path}: {counts['entries']:,} entries, {counts['senses']:,} senses, "
        f"{counts['synsets']:,} synsets, {counts['examples']:,} examples, schema {SCHEMA_VERSION}."
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--xml", type=Path, default=ROOT / "upstream" / "english-wordnet-2025.xml.gz")
    parser.add_argument("--wndb", type=Path, default=ROOT / "upstream" / "english-wordnet-2025.zip")
    parser.add_argument("--output", type=Path, default=ROOT / "data" / "dictionary.db")
    parser.add_argument("--manifest", type=Path, default=MANIFEST_PATH)
    parser.add_argument("--skip-hash-check", action="store_true", help="Only for synthetic importer test fixtures")
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    prepare(args.xml, args.wndb, args.output, manifest, verify=not args.skip_hash_check)


if __name__ == "__main__":
    main()

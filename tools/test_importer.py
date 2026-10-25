#!/usr/bin/env python3
from __future__ import annotations

import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("prepare_data", ROOT / "tools" / "prepare_data.py")
assert SPEC and SPEC.loader
PREPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREPARE)


class ImporterFixtureTests(unittest.TestCase):
    def test_structured_import_and_duplicate_exception_lines(self) -> None:
        xml = """<?xml version="1.0" encoding="UTF-8"?>
<LexicalResource><Lexicon id="oewn" version="2025">
  <LexicalEntry id="e1"><Lemma writtenForm="Alpha_Beta" partOfSpeech="n"/><Sense id="s1" synset="x1"/></LexicalEntry>
  <LexicalEntry id="e2"><Lemma writtenForm="beta" partOfSpeech="n"/><Sense id="s2" synset="x1"/></LexicalEntry>
  <Synset id="x1" partOfSpeech="n">
    <Definition>First source definition.</Definition><Definition>Second source definition.</Definition>
    <Example>First unmodified example.</Example><Example>Second unmodified example.</Example>
  </Synset>
</Lexicon></LexicalResource>
"""
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            xml_path = folder / "fixture.xml.gz"
            with gzip.open(xml_path, "wt", encoding="utf-8") as stream:
                stream.write(xml)
            wndb_path = folder / "fixture.zip"
            with zipfile.ZipFile(wndb_path, "w") as archive:
                archive.writestr("dict/noun.exc", "alphas alpha\nalphas alpha\naxes ax\naxes axis\n")
                for name in ("verb.exc", "adj.exc", "adv.exc"):
                    archive.writestr("dict/" + name, "")
            output = folder / "dictionary.db"
            manifest = json.loads((ROOT / "tools" / "source_manifest.json").read_text(encoding="utf-8"))
            PREPARE.prepare(xml_path, wndb_path, output, manifest, verify=False)
            connection = sqlite3.connect(output)
            self.assertEqual("ok", connection.execute("PRAGMA integrity_check").fetchone()[0])
            self.assertEqual("alpha beta", connection.execute("SELECT lookup_key FROM entries WHERE source_id='e1'").fetchone()[0])
            self.assertEqual(
                [("First source definition.",), ("Second source definition.",)],
                connection.execute("SELECT text FROM definitions ORDER BY definition_order").fetchall(),
            )
            self.assertEqual(
                [("First unmodified example.",), ("Second unmodified example.",)],
                connection.execute("SELECT text FROM examples ORDER BY example_order").fetchall(),
            )
            self.assertEqual(3, connection.execute("SELECT COUNT(*) FROM exceptions").fetchone()[0])
            self.assertEqual("3", connection.execute("SELECT value FROM metadata WHERE key='exception_mapping_count'").fetchone()[0])
            self.assertEqual([], connection.execute("PRAGMA foreign_key_check").fetchall())
            connection.close()


class PreparedDatabaseTests(unittest.TestCase):
    def setUp(self) -> None:
        self.connection = sqlite3.connect(ROOT / "data" / "dictionary.db")

    def tearDown(self) -> None:
        self.connection.close()

    def test_real_database_schema_and_counts(self) -> None:
        # prepare_data.py already runs full integrity_check and foreign_key_check
        # before atomically replacing the prepared database. Avoid repeating that
        # expensive full scan on slow portable or sandbox storage in every test run.
        metadata = dict(self.connection.execute("SELECT key, value FROM metadata"))
        self.assertEqual("1", metadata["schema_version"])
        self.assertEqual("oewn", metadata["dataset_id"])
        self.assertEqual("2025", metadata["dataset_version"])
        self.assertEqual("135969", metadata["entry_count"])
        self.assertEqual("185129", metadata["sense_count"])
        self.assertEqual("107519", metadata["synset_count"])
        self.assertEqual("49596", metadata["example_count"])
        self.assertEqual(185129, self.connection.execute("SELECT COUNT(*) FROM synset_members").fetchone()[0])

    def test_source_examples_and_synonyms_stay_with_their_synset(self) -> None:
        examples = [row[0] for row in self.connection.execute(
            "SELECT text FROM examples WHERE synset_id='oewn-07431314-n' ORDER BY example_order"
        )]
        self.assertEqual([
            "at such junctures he always had an impulse to leave",
            "it was needed only on special occasions",
        ], examples)
        first_synset = {row[0] for row in self.connection.execute(
            "SELECT lemma FROM synset_members WHERE synset_id='oewn-07431314-n'"
        )}
        second_synset = {row[0] for row in self.connection.execute(
            "SELECT lemma FROM synset_members WHERE synset_id='oewn-07461861-n'"
        )}
        self.assertIn("juncture", first_synset)
        self.assertNotIn("affair", first_synset)
        self.assertIn("affair", second_synset)
        self.assertNotIn("juncture", second_synset)

    def test_pinned_upstream_hashes(self) -> None:
        manifest = json.loads((ROOT / "tools" / "source_manifest.json").read_text(encoding="utf-8"))
        for item in manifest["archives"]:
            path = ROOT / "upstream" / item["file"]
            if not path.exists():
                self.skipTest(f"{path.name} is not present; prepared-database checks still ran")
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertEqual(item["sha256"], digest)


if __name__ == "__main__":
    unittest.main(verbosity=2)

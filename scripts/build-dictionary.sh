#!/usr/bin/env bash
#
# build-dictionary.sh — one-time/offline generator for the offline dictionary dataset.
#
# Emits: backend/EReader.Api/data/dictionary/wordnet.json.gz
#   A gzip-compressed lemma -> senses map we own, decoupled from the raw WordNet
#   on-disk format. Decompressed shape:
#     { "<lemma>": [ { "pos": "...", "definition": "...", "examples": ["..."] }, ... ] }
#   Committed gzipped (~6.6 MB vs ~28 MB raw); DictionaryService decompresses on load.
#
# Source dataset: Princeton WordNet 3.1 database files
#   https://wordnetcode.princeton.edu/wn3.1.dict.tar.gz
#
# License / attribution: WordNet is released under a permissive BSD-style license that
# allows commercial and non-commercial use provided the copyright notice is retained:
#   "WordNet 3.1 Copyright 2011 by Princeton University. All rights reserved."
#   https://wordnet.princeton.edu/license-and-commercial-use
# If definitions are surfaced in-app, include a WordNet attribution line in the UI.
#
# This script is NOT run at request time. The generated wordnet.json is committed so
# the backend build is reproducible without any network access.
#
# Usage:
#   bash scripts/build-dictionary.sh
#
set -euo pipefail

WORDNET_URL="https://wordnetcode.princeton.edu/wn3.1.dict.tar.gz"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT_FILE="$REPO_ROOT/backend/EReader.Api/data/dictionary/wordnet.json.gz"

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

echo "Downloading WordNet 3.1 database..."
curl -fSL "$WORDNET_URL" -o "$WORKDIR/wn.tar.gz"

echo "Extracting..."
tar -xzf "$WORKDIR/wn.tar.gz" -C "$WORKDIR"

# The tarball lays the data files out under dict/. Locate it by an anchor file so we
# don't hard-code the top-level directory name across WordNet versions.
DICT_DIR="$(dirname "$(find "$WORKDIR" -name 'data.noun' -print -quit)")"
if [[ -z "$DICT_DIR" || ! -f "$DICT_DIR/data.noun" ]]; then
  echo "ERROR: could not find WordNet data files (data.noun) in the extracted archive." >&2
  exit 1
fi

echo "Normalizing to $OUT_FILE ..."
mkdir -p "$(dirname "$OUT_FILE")"

python3 - "$DICT_DIR" "$OUT_FILE" <<'PY'
import gzip
import json
import os
import re
import sys

dict_dir, out_file = sys.argv[1], sys.argv[2]

# WordNet ss_type code (column 3 of data.* and the file itself) -> our part-of-speech label.
POS_FILES = {
    "data.noun": "noun",
    "data.verb": "verb",
    "data.adj": "adjective",
    "data.adv": "adverb",
}

# Adjective head words carry a syntactic marker like "word(a)" / "word(ip)"; strip it.
MARKER_RE = re.compile(r"\([a-z]+\)$")
# Examples in a gloss are double-quoted, possibly with a leading attribution dash.
EXAMPLE_RE = re.compile(r'"([^"]*)"')


def clean_lemma(word: str) -> str:
    word = MARKER_RE.sub("", word)
    return word.replace("_", " ").lower()


def parse_gloss(gloss: str):
    gloss = gloss.strip()
    examples = [m.strip() for m in EXAMPLE_RE.findall(gloss)]
    # Definition is everything before the first quoted example; if no examples, the
    # whole gloss is the definition. Trim a dangling separator left behind.
    qpos = gloss.find('"')
    definition = (gloss[:qpos] if qpos != -1 else gloss).strip()
    definition = definition.rstrip(";").strip()
    return definition, examples


# lemma -> list of senses, preserving first-seen order across files.
result: dict[str, list[dict]] = {}

for fname, pos_label in POS_FILES.items():
    path = os.path.join(dict_dir, fname)
    # WordNet data files are Latin-1 and prefixed with a multi-line license header
    # whose lines start with two spaces; skip those.
    with open(path, encoding="latin-1") as fh:
        for line in fh:
            if line.startswith("  "):
                continue
            left, sep, gloss = line.partition("|")
            if not sep:
                continue
            tokens = left.split()
            if len(tokens) < 4:
                continue
            try:
                w_cnt = int(tokens[3], 16)
            except ValueError:
                continue

            # Word list: w_cnt pairs of (word, lex_id) starting at column 4.
            words = []
            idx = 4
            for _ in range(w_cnt):
                if idx >= len(tokens):
                    break
                words.append(tokens[idx])
                idx += 2

            definition, examples = parse_gloss(gloss)
            if not definition:
                continue

            for word in words:
                lemma = clean_lemma(word)
                if not lemma:
                    continue
                result.setdefault(lemma, []).append(
                    {"pos": pos_label, "definition": definition, "examples": examples}
                )

# Sort keys for a stable, diff-friendly committed file.
ordered = {k: result[k] for k in sorted(result)}

payload = json.dumps(ordered, ensure_ascii=False, separators=(",", ":")) + "\n"

# Deterministic gzip: mtime=0 and no stored filename so regenerating the dataset
# from the same WordNet release produces a byte-identical blob (clean git diffs).
with open(out_file, "wb") as fh:
    with gzip.GzipFile(filename="", mode="wb", fileobj=fh, compresslevel=9, mtime=0) as gz:
        gz.write(payload.encode("utf-8"))

print(f"Wrote {len(ordered)} lemmas (gzipped).")
PY

echo "Done."

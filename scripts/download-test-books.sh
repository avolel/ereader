#!/usr/bin/env bash
set -euo pipefail

mkdir -p test-books

echo "Downloading The Time Machine (H.G. Wells)..."
curl -L "https://standardebooks.org/ebooks/h-g-wells/the-time-machine/downloads/h-g-wells_the-time-machine.epub" \
     -o "test-books/the-time-machine.epub"

echo "Downloading Pride and Prejudice (Jane Austen)..."
curl -L "https://standardebooks.org/ebooks/jane-austen/pride-and-prejudice/downloads/jane-austen_pride-and-prejudice.epub" \
     -o "test-books/pride-and-prejudice.epub"

echo "Downloading Moby-Dick (Herman Melville)..."
curl -L "https://standardebooks.org/ebooks/herman-melville/moby-dick/downloads/herman-melville_moby-dick.epub" \
     -o "test-books/moby-dick.epub"

echo "Downloading Alice's Adventures in Wonderland (Lewis Carroll) from Project Gutenberg..."
curl -L "https://www.gutenberg.org/ebooks/11.epub.images" \
     -o "test-books/alices-adventures-in-wonderland.epub"

echo "Downloading Don Quijote (Miguel de Cervantes) from Project Gutenberg..."
curl -L "https://www.gutenberg.org/ebooks/2000.epub.images" \
     -o "test-books/don-quijote.epub"

echo ""
echo "Done. Books saved to test-books/:"
ls -lh test-books/*.epub

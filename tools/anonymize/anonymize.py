#!/usr/bin/env python3
"""
Anonymize AsciiDoc files.

For each .adoc file in the given directory, walk line-by-line and replace
every alphabetic "word" (a run of [a-zA-Z]+) with a random alphabetic run of
the same length and same case pattern. AsciiDoc structure is preserved:

- block delimiters (-----, ====, ...., ++++, ____, ****)
- listing/source blocks (their contents left intact)
- block attribute lines [...]
- include:: directives
- comments
- table delimiters (|===, !===)
- document attribute KEYS (:name:) — value is randomised
- cross-references (<<...>>) — id is preserved
- inline attribute references ({var}) — preserved
- code spans (`...`) — preserved
- URLs (http(s)://...) — preserved
- inline macros like footnote:id[...] — the macro name and id are preserved;
  the bracket content IS randomised

Word substitutions are cached so the same source word always maps to the
same random word across all files — keeps the docs readable as structure
even if the meaning is gone.

Run from anywhere with:

    python anonymize.py <directory>

By default operates on the example-documents/ next to the script's repo
root if no directory is given.
"""
from __future__ import annotations
import re
import random
import string
import sys
from pathlib import Path

random.seed(42)

word_cache: dict[str, str] = {}


def randomize_word(word: str) -> str:
    """Random alphabetic string of same length, preserving case. Cached."""
    if word in word_cache:
        return word_cache[word]
    new = ''.join(
        random.choice(string.ascii_uppercase) if c.isupper()
        else random.choice(string.ascii_lowercase) if c.islower()
        else c
        for c in word
    )
    word_cache[word] = new
    return new


# Skip regions — patterns whose matches are emitted verbatim and not entered.
SKIP_PATTERNS = [
    re.compile(r'<<[^>]*>>'),                            # cross-references
    re.compile(r'\{[A-Za-z_][A-Za-z0-9_-]*\}'),          # {attribute-ref}
    re.compile(r'`[^`]*`'),                              # inline code span
    re.compile(r'https?://\S+'),                         # URLs
    re.compile(r'image:+[^\s\[]+'),                      # image::file or image:file
    re.compile(r'link:+[^\s\[]+'),                       # link:url
    re.compile(r'&#?[A-Za-z0-9]+;'),                     # HTML entities &name; / &#NNN; / &#xHHHH;
    # Table cell specs at start of a row or after a cell separator:
    # examples: ".3+h|"  "2+h|"  "12+|"  "2*|"  ".4+|"  "<.>|"  "2+^a|"
    re.compile(r'(?:^|(?<=[\s|]))[\d.+*<>^]+[adehlmsv]?\|'),
]

# Inline macros: footnote:id[content], xref:target[text], etc.
# We keep "macroName:id[" verbatim, randomize bracket content, and put "]" back.
INLINE_MACRO_RE = re.compile(r'\b([a-z][a-z]*):([A-Za-z0-9_-]*)\[')


def randomize_inline(text: str) -> str:
    """Randomise alphabetic runs in text while skipping structural regions."""
    result: list[str] = []
    i = 0
    n = len(text)

    while i < n:
        # Try inline-macro pattern first (handles footnote:id[..])
        m = INLINE_MACRO_RE.match(text, i)
        if m:
            # Emit "name:id[" verbatim
            result.append(text[i:m.end()])
            i = m.end()
            # Find matching close bracket (allow nested [ ])
            depth = 1
            j = i
            while j < n and depth > 0:
                if text[j] == '[':
                    depth += 1
                elif text[j] == ']':
                    depth -= 1
                    if depth == 0:
                        break
                j += 1
            inner = text[i:j]
            result.append(randomize_inline(inner))
            if j < n:
                result.append(']')
                i = j + 1
            else:
                i = j
            continue

        # Try other skip patterns
        matched = None
        for pat in SKIP_PATTERNS:
            m = pat.match(text, i)
            if m:
                matched = m
                break
        if matched:
            result.append(matched.group(0))
            i = matched.end()
            continue

        # Alphabetic word
        m = re.match(r'[a-zA-Z]+', text[i:])
        if m:
            result.append(randomize_word(m.group()))
            i += len(m.group())
            continue

        # Otherwise the single char as-is
        result.append(text[i])
        i += 1

    return ''.join(result)


COMMENT_RE = re.compile(r'^\s*//')
BLOCK_ATTR_RE = re.compile(r'^\s*\[.*\]\s*$')
INCLUDE_RE = re.compile(r'^\s*(?:include|image)::')
TABLE_DELIM_RE = re.compile(r'^\s*[|!]===\s*$')
LISTING_DELIM_RE = re.compile(r'^\s*(-{4,}|\.{4,}|\+{4,}|_{4,}|\*{4,})\s*$')
ATTR_RE = re.compile(r'^(\s*):([^:]+):(\s*)(.*)$')
HEADING_RE = re.compile(r'^(\s*)(=+)\s+(.*)$')
BLOCK_TITLE_RE = re.compile(r'^(\s*)\.([^\s.].*)$')  # ".Title" — single leading dot, non-dot/space char follows


def process_file(path: Path) -> tuple[int, int]:
    """Process one file in place. Returns (lines_in, lines_out)."""
    text = path.read_text(encoding='utf-8')
    has_trailing_nl = text.endswith('\n')
    lines = text.splitlines()

    out: list[str] = []
    in_listing = False
    listing_delim: str | None = None

    for line in lines:
        stripped = line.strip()

        # Listing/code block toggle (preserve contents verbatim)
        if not in_listing:
            m = LISTING_DELIM_RE.match(line)
            if m:
                in_listing = True
                listing_delim = m.group(1)
                out.append(line)
                continue
        else:
            if listing_delim and stripped == listing_delim:
                in_listing = False
                listing_delim = None
                out.append(line)
                continue
            # Inside listing: leave alone
            out.append(line)
            continue

        # Empty line
        if not stripped:
            out.append(line)
            continue

        # Comment
        if COMMENT_RE.match(line):
            out.append(line)
            continue

        # Table delimiter |=== or !===
        if TABLE_DELIM_RE.match(line):
            out.append(line)
            continue

        # Block attribute line [...]
        if BLOCK_ATTR_RE.match(line):
            out.append(line)
            continue

        # include:: or image:: directive
        if INCLUDE_RE.match(line):
            out.append(line)
            continue

        # Document attribute :name: value
        m = ATTR_RE.match(line)
        if m:
            indent, name, ws, value = m.groups()
            out.append(f"{indent}:{name}:{ws}{randomize_inline(value)}")
            continue

        # Section heading = ...
        m = HEADING_RE.match(line)
        if m:
            indent, markers, title = m.groups()
            out.append(f"{indent}{markers} {randomize_inline(title)}")
            continue

        # Block title .Foo (single dot, no space after — distinguishes from
        # the "." ordered-list marker which is followed by whitespace)
        m = BLOCK_TITLE_RE.match(line)
        if m:
            indent, title = m.groups()
            out.append(f"{indent}.{randomize_inline(title)}")
            continue

        # Default: randomise inline words
        out.append(randomize_inline(line))

    result = '\n'.join(out)
    if has_trailing_nl:
        result += '\n'

    path.write_text(result, encoding='utf-8')
    return (len(lines), len(out))


def main() -> int:
    if len(sys.argv) > 1:
        target_dir = Path(sys.argv[1])
    else:
        # Default: example-documents at the repo root (3 levels up from
        # tools/anonymize/anonymize.py)
        target_dir = Path(__file__).resolve().parents[2] / 'example-documents'

    if not target_dir.is_dir():
        print(f"Not a directory: {target_dir}", file=sys.stderr)
        return 1

    files = sorted(target_dir.glob('*.adoc'))
    print(f"Anonymising {len(files)} .adoc files in {target_dir}")
    for f in files:
        n_in, n_out = process_file(f)
        print(f"  {f.name:65}  {n_in:>4} lines")
    print(f"Done. Unique words substituted: {len(word_cache)}")
    return 0


if __name__ == '__main__':
    sys.exit(main())

# Markdown syntax and rendering

## About Hearth's Markdown

When a post or page uses the **Markdown** editor type, its source is converted to HTML by Hearth's own single-pass parser. It supports the common Markdown constructs — headings, emphasis, lists, tables, code, blockquotes, links, images and more — with a few **deliberate deviations** from CommonMark / GitHub described below.

- **Only `*` is an emphasis marker.** The underscore `_` is always literal.
- **C-style escapes are not interpreted.** `\t` and `\n` stay literal.
- **Raw HTML passes through** by default (and is *not* sanitised — see the security note).

## Emphasis — the most important rule

**Only the asterisk `*` creates emphasis. The underscore `_` is ALWAYS a literal character.**

| You write | You get |
| --- | --- |
| `*italic*` | *italic* — `<em>` |
| `**bold**` | **bold** — `<strong>` |
| `***bold italic***` | ***bold italic*** |
| `_italic_` | literal `_italic_` (NOT italic) |
| `snake_case_name` | literal `snake_case_name` (untouched) |
| `__init__` | literal `__init__` (untouched) |

A deliberate choice: identifiers, file paths and variable names with underscores are never accidentally italicised. The cost: Markdown written elsewhere that uses `_` for emphasis renders those underscores literally. **When writing for this parser, use `*` for italics and `**` for bold.** (Closing-delimiter scanning skips backslash-escaped characters, so `*a \* b*` still emphasises correctly.)

## Escapes — what backslash does

A backslash escapes only this set of **punctuation** characters, emitting the literal character:

```
\ ` * _ { } [ ] ( ) # + - . ! | ~ >
```

**A backslash before anything else is a literal backslash** — C-style escape sequences are NOT interpreted:

| You write | You get |
| --- | --- |
| `\*` | literal `*` |
| `\t` | literal `\t` (backslash + t — NOT a tab) |
| `\n` | literal `\n` (NOT a newline) |
| `C:\table\tangible` | literal `C:\table\tangible` |

## Block-level constructs

### Headings

`#` through `######` (levels 1–6), and **must** be followed by a space. **Seven or more `#` is not a heading** — only levels 1–6 exist — so such a line renders as literal paragraph text. Trailing `#` and surrounding whitespace are stripped; content is inline-parsed.

```
# Title          → <h1>Title</h1>
### Section ###  → <h3>Section</h3>
####### Seven    → literal text, not a heading (7+ hashes)
#NoSpace         → literal text, not a heading (no space after #)
```

### Paragraphs & line breaks

Consecutive non-blank lines form one paragraph until a blank line or a block element begins. Within a paragraph, a single newline becomes `<br>`. A trailing backslash on a line, or an explicit `<br>`, also controls line breaks.

### Horizontal rules

A line containing **only** `-` or `*` (spaces allowed), with **3 or more** of the character — emits `<hr>`. e.g. `---`, `***`, `- - -`.

### Blockquotes

Lines beginning with `>` (optional leading whitespace, optional one space after). Supports **lazy continuation** (a following non-blank line is pulled in). The inner content is **recursively parsed** and wrapped in `<blockquote>`.

### Code

**Fenced blocks** open and close with three backticks; an optional language tag may follow the opening fence (intended for highlight.js). The body is HTML-encoded.

````
```python
print("hi")
```
→ <pre><code class="language-python">print(&quot;hi&quot;)</code></pre>
````

**Inline code** uses backtick runs of any length, matched by an equal-length closing run. If the code both starts and ends with a space, one space is trimmed from each side. Body is HTML-encoded. Use double backticks when the code itself contains a backtick.

### Lists

**Unordered:** markers `-`, `*` or `+` followed by a space. **Ordered:** digits then `.` or `)` then a space (`1.` or `1)`).

**Nesting:** content indented beyond the list's base indent becomes child content of the preceding item; it is dedented and **recursively parsed**, so nested lists, paragraphs and code inside items all work. A tab counts as 4 spaces.

### Tables (GitHub-style)

Triggered when a line contains an unescaped `|` **and** the next line is a separator row (only `|`, `-`, `:`, spaces). Column alignment comes from colons:

| Separator | Alignment |
| --- | --- |
| `:---` | left |
| `---:` | right |
| `:---:` | center |
| `---` | default |

Escape a literal pipe inside a cell with `\|`. Header cells become `<th>`, body cells `<td>`; the whole table is wrapped in a horizontally scrollable container. Cell contents are inline-parsed.

## Inline constructs

### Links & images

```
[text](url)             → <a href="url">text</a>
[text](url "title")     → adds title="title"
![alt](url)             → <img src="url" alt="alt">
![alt](url "title")     → adds title="title"
```

Titles may use `"…"` or `'…'`. Link text is inline-parsed (nested brackets handled by depth matching). URLs and titles are attribute-encoded.

### Strikethrough

`~~struck~~` → `<del>struck</del>`

### Auto-links

Bare URLs starting with `http://` or `https://` become links automatically. Trailing punctuation (`. , ; : ! ? )`) is trimmed off the URL.

### HTML entities

Valid entities pass through unchanged; a bare `&` becomes `&amp;`. A lone `>` in text becomes `&gt;`; a `<` becomes `&lt;` unless it begins a passed-through HTML tag.

## Raw HTML passthrough

**Raw HTML is allowed by default.** A line beginning with `<` followed by a recognised block tag is emitted verbatim along with following lines, until **whichever comes first**: the matching close tag (depth-aware, so nested same-name tags balance), or a blank line. Markdown placed right after a closing tag is parsed normally — no blank line required:

```
<div>raw</div>
# Heading        → <div>raw</div> then <h1>Heading</h1>
```

Recognised block tags include `div, p, table, ul, ol, li, h1–h6, pre, blockquote, form, iframe, script, style, section, article, figure, details, video, audio, svg` and more. Inline tag-like `<…>` sequences also pass through.

> **⚠️ Security note.** With passthrough on, the parser does **not** sanitise HTML — including `<script>` and `<iframe>`. Markdown content is authored by trusted admins, so this is intended; just be aware that whatever HTML you put in the source is emitted as-is.

## Quick do / don't

- ✅ Use `*italic*` and `**bold**`.
- ❌ Don't use `_italic_` / `__bold__` — underscores stay literal.
- ✅ Use backslash escapes only for the punctuation set listed above.
- ❌ Don't expect `\t` or `\n` to become whitespace — they're literal.
- ✅ Headings, list markers and `>` need a trailing space.
- ✅ Use `\|` for a literal pipe inside a table cell.
- ⚠️ Raw HTML is passed through unsanitised — only put in what you trust.

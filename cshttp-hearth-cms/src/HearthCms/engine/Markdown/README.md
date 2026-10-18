Markdown to HTML Parser/Converter
https://github.com/adriancs2/csharp-Markdown-To-Html
Version: 1.1

# MarkdownToHtml - Parser Behavior Reference

> **Purpose of this document.** This describes exactly what the `System.MarkdownToHtml`
> C# parser supports, how it renders each construct, and the deliberate choices it makes that
> differ from GitHub/CommonMark. Read this instead of the source code. It is written so that an AI
> assistant can answer questions or generate compatible Markdown, and so a human can use it as a
> feature reference.

## TL;DR

A single-pass, character-by-character Markdown-to-HTML converter. **No regex, no `string.Split`,
no LINQ, and no `System.Web` dependency** (HTML encoding is built in). It supports the common Markdown constructs (headings, emphasis, lists, tables, code, blockquotes,
links, images, etc.) with a few intentional deviations from the CommonMark spec - most notably,
**underscores are never emphasis**, and **C-style escapes like `\t` are not interpreted**.

## Entry point

```csharp
string html = MarkdownToHtml.ToHtml(markdown);                       // HTML passthrough ON  (default)
string html = MarkdownToHtml.ToHtml(markdown, allowHtmlPassThrough); // explicit toggle
```

- Returns `""` for null/empty input.
- Trailing newlines are trimmed from the output.
- `allowHtmlPassThrough` controls whether raw HTML in the input is emitted verbatim (see *HTML passthrough*).

---

## Emphasis - the most important rule to know

**Only the asterisk `*` is an emphasis marker. The underscore `_` is ALWAYS a literal character.**

| You write | You get |
|---|---|
| `*italic*` | *italic* - `<em>` |
| `**bold**` | **bold** - `<strong>` |
| `***bold italic***` | ***bold italic*** - `<strong><em>` |
| `_italic_` | literal `_italic_` (NOT italic) |
| `__bold__` | literal `__bold__` (NOT bold) |
| `snake_case_name` | literal `snake_case_name` (untouched) |
| `__init__` | literal `__init__` (untouched) |

This is a deliberate deviation from GitHub. The benefit: identifiers, file paths, and variable names
containing underscores are never accidentally turned into emphasis. The cost: Markdown authored
elsewhere that uses `_` for emphasis will render those underscores literally. **When generating
Markdown for this parser, always use `*` for italics and `**` for bold.**

Closing-delimiter scanning skips backslash-escaped characters, so `*a \* b*` emphasizes correctly.

---

## Escapes - what backslash does

A backslash escapes only this specific set of **punctuation** characters:

```
\ ` * _ { } [ ] ( ) # + - . ! | ~ >
```

A backslash before any of those emits the literal character (e.g. `\*` → `*`, `\_` → `_`).

**A backslash before anything else is a literal backslash.** In particular, C-style escape
sequences are NOT interpreted:

| You write | You get |
|---|---|
| `\t` | literal `\t` (backslash + t - NOT a tab) |
| `\n` | literal `\n` (NOT a newline) |
| `C:\table\tangible` | literal `C:\table\tangible` |

> Reminder: even a *real* tab character (0x09) only renders as tab-width spacing inside `<pre>`.
> In a normal `<p>`, browsers collapse runs of whitespace down to a single space.

---

## Block-level constructs

### Headings
`#` through `######` (levels 1–6), **must** be followed by a space. Trailing `#` characters and
surrounding whitespace are stripped. Content is inline-parsed.

```
# Title          → <h1>Title</h1>
### Section ###  → <h3>Section</h3>
#NoSpace         → literal text, not a heading
```

### Paragraphs
The default. Consecutive non-blank lines form one paragraph until a blank line or a block-level
element begins. Within a paragraph, single newlines become `<br>`. A trailing backslash `\` on a
line, or an explicit `<br>`, controls line-break behavior.

### Horizontal rules
A line containing **only** `-` or `*` (mixed with spaces/tabs), with **3 or more** of the rule
character. Emits `<hr>`.

```
---     → <hr>
***     → <hr>
- - -   → <hr>
```

### Blockquotes
Lines beginning with `>` (optional leading whitespace, optional one space after `>`). Supports
**lazy continuation** (a following non-blank line is pulled into the quote). The inner content is
**recursively parsed** as Markdown and wrapped in `<blockquote>`.

### Code

**Fenced code blocks** - opened and closed with ` ``` `. An optional language tag may follow the
opening fence. The body is HTML-encoded. The closing fence may be indented.

````
```python
print("hi")
```
````
→ `<pre><code class="language-python">print(&quot;hi&quot;)</code></pre>`

With no language tag, emits `<pre><code>…</code></pre>`. Language is intended for highlight.js.

**Inline code** - backtick runs of any length, matched by an equal-length closing run. If the code
both starts and ends with a space, one space is trimmed from each side. Body is HTML-encoded.

```
`code`        → <code>code</code>
``a `b` c``   → uses double backticks so the inner backtick survives
```

### Lists

**Unordered** - markers `-`, `*`, or `+` followed by a space. (A line that is actually a horizontal
rule is treated as a rule, not a list.)

**Ordered** - one or more digits followed by `.` or `)` then a space (`1.` or `1)`).

```
- item        1. item
* item        2) item
+ item
```

**Nesting** - content indented beyond the list's base indent becomes child content of the preceding
item; it is dedented and **recursively parsed**, so nested lists, paragraphs, and code inside list
items all work. A tab counts as 4 spaces for indentation. Blank lines between items are tolerated as
long as a same-or-greater-indented marker follows.

### Tables (GitHub-style)
Triggered when the current line contains an unescaped `|` **and** the next line is a separator row.
A separator row contains only `|`, `-`, `:`, spaces, and tabs, and must include at least one `|` and
one `-`.

Column alignment comes from colons in the separator:

| Separator | Alignment |
|---|---|
| `:---`  | left |
| `---:`  | right |
| `:---:` | center |
| `---`   | default (no `style`) |

- Escape a literal pipe inside a cell with `\|`.
- Header cells become `<th>`, body cells `<td>`, each carrying `style="text-align:…"` when aligned.
- The whole table is wrapped in `<div style="overflow-x:auto"><table>…</table></div>` with
  `<thead>` and `<tbody>`.
- Cell contents are inline-parsed.

---

## Inline constructs

### Links and images
```
[text](url)                  → <a href="url">text</a>
[text](url "title")          → adds title="title"
![alt](url)                  → <img src="url" alt="alt">
![alt](url "title")          → adds title="title"
```
- Titles may use `"…"` or `'…'`.
- Link text is inline-parsed; nested `[ ]` are handled via bracket-depth matching.
- URLs and titles are HTML-attribute-encoded.

### Strikethrough
```
~~struck~~  → <del>struck</del>
```

### Auto-links
Bare URLs starting with `http://` or `https://` become links automatically. Trailing punctuation
(`. , ; : ! ? )`) is trimmed off the URL.

```
visit https://example.com.   → visit <a href="https://example.com">https://example.com</a>.
```

### HTML entities
Valid entities pass through unchanged; a bare `&` becomes `&amp;`.

```
&amp;    → &amp;     (named)
&#123;   → &#123;    (decimal)
&#x1F;   → &#x1F;    (hex)
&        → &amp;
```

A lone `>` in text becomes `&gt;`. A `<` becomes `&lt;` unless it begins a passed-through HTML tag.

---

## HTML passthrough (`allowHtmlPassThrough`, default ON)

**Block level:** a line beginning with `<` followed by a recognized block tag is emitted verbatim
(along with following lines). The block ends at **whichever comes first**:

- the line containing the **matching close tag** for the opening tag (depth-aware, so nested
  same-name tags are balanced — e.g. a `<div>` block ends at its own `</div>`, not an inner one), or
- a **blank line**.

This means Markdown placed immediately after a closing tag is parsed normally — no blank line is
required:

```
<div>raw</div>
# Heading        → <div>raw</div> then <h1>Heading</h1>
```

`<!-- comments -->`, `<!DOCTYPE …>`, and void/self-closing or unclosed tags (e.g. `<br>`, `<hr>`)
have no matching close tag, so they fall back to ending at a blank line. Recognized tags include:

`div, p, table, tr, td, th, thead, tbody, tfoot, ul, ol, li, dl, dt, dd, h1–h6, pre, blockquote, hr,
br, form, fieldset, iframe, script, style, section, article, nav, aside, header, footer, main,
figure, figcaption, details, summary, video, audio, source, canvas, svg`

**Inline level:** tag-like `<…>` sequences (opening or closing) pass through.

When `allowHtmlPassThrough` is **false**, `<` is escaped to `&lt;` and no raw HTML survives.

> **Security note:** with passthrough on, the parser does **not** sanitize HTML - including
> `<script>` and `<iframe>`. Only enable it for trusted input, or sanitize the output downstream.

---

## Quick "do / don't" for generating Markdown for this parser

- ✅ Use `*italic*` and `**bold**`.
- ❌ Don't use `_italic_` / `__bold__` - underscores stay literal.
- ✅ Use real backslash escapes only for the punctuation set listed above.
- ❌ Don't expect `\t`, `\n`, etc. to become whitespace - they're literal.
- ✅ Headings, list markers, and `>` need a trailing space.
- ✅ Use `\|` to put a literal pipe inside a table cell.
- ⚠️ Raw HTML is passed through unsanitized when passthrough is enabled.

---

## Known quirk

In `SplitTableCells`, the final-cell logic trims a copy to decide *whether* to add a trailing cell,
but then adds the **untrimmed** text. Practical effect: a trailing cell that is only whitespace is
dropped, but a kept trailing cell retains its raw spacing (which is then trimmed again at render time
anyway). Harmless in normal use; noted for completeness.
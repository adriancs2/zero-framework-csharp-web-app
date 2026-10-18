using System;
using System.Collections.Generic;
using System.Text;

// Markdown to HTML Parser/Converter
// https://github.com/adriancs2/csharp-Markdown-To-Html
// Version: 1.2.1

namespace System.engine.Markdown
{
    /// <summary>
    /// Single-pass character-scanning Markdown-to-HTML parser.
    /// No string.Split, no Regex. Walks the raw input char-by-char.
    /// 
    /// Supports: headings, paragraphs, bold/italic/bold-italic, links, images,
    /// inline code, fenced code blocks (highlight.js), unordered/ordered lists
    /// with nesting, blockquotes, horizontal rules, tables (GitHub style with
    /// \| escape), strikethrough, line breaks, auto-links, HTML passthrough.
    /// </summary>
    public static class MarkdownToHtml
    {
        // ════════════════════════════════════════
        //  HTML ENCODING (self-contained — no System.Web dependency)
        // ════════════════════════════════════════
        static class HttpUtility
        {
            public static string HtmlEncode(string s)
            {
                if (string.IsNullOrEmpty(s)) return s ?? "";
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '<': sb.Append("&lt;"); break;
                        case '>': sb.Append("&gt;"); break;
                        case '&': sb.Append("&amp;"); break;
                        case '"': sb.Append("&quot;"); break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            }

            // Matches .NET Framework HtmlAttributeEncode: encodes < & " ' but NOT >.
            public static string HtmlAttributeEncode(string s)
            {
                if (string.IsNullOrEmpty(s)) return s ?? "";
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '<': sb.Append("&lt;"); break;
                        case '&': sb.Append("&amp;"); break;
                        case '"': sb.Append("&quot;"); break;
                        case '\'': sb.Append("&#39;"); break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            }
        }

        // ════════════════════════════════════════
        //  PUBLIC ENTRY
        // ════════════════════════════════════════

        [ThreadStatic]
        static bool _allowHtmlPassThrough;

        public static string ToHtml(string markdown, bool allowHtmlPassThrough = true)
        {
            bool prev = _allowHtmlPassThrough;
            _allowHtmlPassThrough = allowHtmlPassThrough;
            try { return ToHtmlImpl(markdown); }
            finally { _allowHtmlPassThrough = prev; }
        }

        static string ToHtmlImpl(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            var sb = new StringBuilder();
            int pos = 0;
            int len = markdown.Length;

            while (pos < len)
            {
                // ── skip blank lines ──
                if (IsNewline(markdown, pos))
                {
                    pos = SkipNewline(markdown, pos);
                    continue;
                }

                // measure leading whitespace (for lists)
                int lineStart = pos;
                int indent = 0;
                while (pos < len && (markdown[pos] == ' ' || markdown[pos] == '\t'))
                {
                    if (markdown[pos] == '\t') indent += 4;
                    else indent++;
                    pos++;
                }

                // blank line after only whitespace
                if (pos >= len || IsNewline(markdown, pos))
                {
                    if (pos < len) pos = SkipNewline(markdown, pos);
                    continue;
                }

                char c = markdown[pos];

                // ── fenced code block ``` ──
                if (c == '`' && pos + 2 < len && markdown[pos + 1] == '`' && markdown[pos + 2] == '`')
                {
                    pos = ScanFencedCodeBlock(markdown, pos, sb);
                    continue;
                }

                // ── HTML passthrough ──
                if (_allowHtmlPassThrough && c == '<' && IsHtmlBlockStart(markdown, pos))
                {
                    pos = ScanHtmlBlock(markdown, pos, sb);
                    continue;
                }

                // ── heading # ──
                if (c == '#')
                {
                    int result = TryScanHeading(markdown, pos, sb);
                    if (result > pos) { pos = result; continue; }
                }

                // ── horizontal rule (---, ***) ──
                if (c == '-' || c == '*')
                {
                    int result = TryScanHorizontalRule(markdown, pos, sb);
                    if (result > pos) { pos = result; continue; }
                }

                // ── blockquote > ──
                if (c == '>')
                {
                    pos = ScanBlockquote(markdown, pos, sb);
                    continue;
                }

                // ── table (peek: current line has |, next line is separator) ──
                if (LineContainsPipe(markdown, pos) && IsNextLineTableSeparator(markdown, pos))
                {
                    pos = ScanTable(markdown, pos, sb);
                    continue;
                }

                // ── unordered list (- item, * item, + item) ──
                if ((c == '-' || c == '*' || c == '+') && pos + 1 < len && markdown[pos + 1] == ' ')
                {
                    // but not a horizontal rule (already checked above)
                    pos = ScanList(markdown, lineStart, sb, ordered: false);
                    continue;
                }

                // ── ordered list (1. item, 1) item) ──
                if (char.IsDigit(c))
                {
                    int result = TryScanOrderedList(markdown, lineStart, sb);
                    if (result > lineStart) { pos = result; continue; }
                }

                // ── paragraph (default) ──
                pos = ScanParagraph(markdown, lineStart, sb);
            }

            // trim trailing newlines from output
            while (sb.Length > 0 && (sb[sb.Length - 1] == '\n' || sb[sb.Length - 1] == '\r'))
            {
                sb.Length--;
            }

            return sb.ToString();
        }

        // ════════════════════════════════════════
        //  POSITION HELPERS
        // ════════════════════════════════════════

        static bool IsNewline(string s, int pos)
        {
            if (pos >= s.Length) return false;
            char c = s[pos];
            return c == '\n' || c == '\r';
        }

        /// <summary>Advance past \r\n, \r, or \n. Returns new position.</summary>
        static int SkipNewline(string s, int pos)
        {
            if (pos >= s.Length) return pos;
            if (s[pos] == '\r')
            {
                pos++;
                if (pos < s.Length && s[pos] == '\n') pos++;
                return pos;
            }
            if (s[pos] == '\n') return pos + 1;
            return pos;
        }

        /// <summary>Find the end of the current line (position of \r, \n, or end of string).</summary>
        static int FindEndOfLine(string s, int pos)
        {
            while (pos < s.Length && s[pos] != '\r' && s[pos] != '\n')
            {
                pos++;
            }
            return pos;
        }

        /// <summary>Extract a substring from pos to end-of-line.</summary>
        static string ExtractLine(string s, int from, int to)
        {
            return s.Substring(from, to - from);
        }

        /// <summary>Skip spaces and tabs, return new position and indent count.</summary>
        static int SkipIndent(string s, int pos, out int indent)
        {
            indent = 0;
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t'))
            {
                if (s[pos] == '\t') indent += 4;
                else indent++;
                pos++;
            }
            return pos;
        }

        /// <summary>Is the current position a blank line (only whitespace before newline/end)?</summary>
        static bool IsBlankLine(string s, int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t')) pos++;
            return pos >= s.Length || IsNewline(s, pos);
        }

        // ════════════════════════════════════════
        //  BLOCK: FENCED CODE BLOCK
        // ════════════════════════════════════════

        static int ScanFencedCodeBlock(string s, int pos, StringBuilder sb)
        {
            // pos is at first `
            // scan past opening ``` to get the language hint
            int fenceStart = pos;
            pos += 3; // skip ```

            // read language tag until end of line
            int langStart = pos;
            int eol = FindEndOfLine(s, pos);
            string lang = s.Substring(langStart, eol - langStart).Trim();
            pos = eol;
            if (pos < s.Length) pos = SkipNewline(s, pos);

            // collect code content until closing ``` or end of input
            var code = new StringBuilder();

            while (pos < s.Length)
            {
                // check if this line starts with ```
                int tempPos = pos;
                while (tempPos < s.Length && (s[tempPos] == ' ' || s[tempPos] == '\t')) tempPos++;

                if (tempPos + 2 < s.Length && s[tempPos] == '`' && s[tempPos + 1] == '`' && s[tempPos + 2] == '`')
                {
                    // closing fence found - skip past it
                    eol = FindEndOfLine(s, tempPos);
                    pos = eol;
                    if (pos < s.Length) pos = SkipNewline(s, pos);
                    break;
                }

                // not a closing fence - append this line to code
                eol = FindEndOfLine(s, pos);
                code.Append(s, pos, eol - pos);
                pos = eol;
                if (pos < s.Length)
                {
                    code.Append('\n');
                    pos = SkipNewline(s, pos);
                }
            }

            // remove trailing newline from code
            if (code.Length > 0 && code[code.Length - 1] == '\n')
            {
                code.Length--;
            }

            string encoded = HttpUtility.HtmlEncode(code.ToString());

            if (lang.Length > 0)
            {
                sb.Append("<pre><code class=\"language-");
                sb.Append(HttpUtility.HtmlAttributeEncode(lang));
                sb.Append("\">");
                sb.Append(encoded);
                sb.Append("</code></pre>");
            }
            else
            {
                sb.Append("<pre><code>");
                sb.Append(encoded);
                sb.Append("</code></pre>");
            }
            sb.Append('\n');

            return pos;
        }

        // ════════════════════════════════════════
        //  BLOCK: HTML PASSTHROUGH
        // ════════════════════════════════════════

        static readonly HashSet<string> HtmlBlockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "div", "p", "table", "tr", "td", "th", "thead", "tbody", "tfoot",
            "ul", "ol", "li", "dl", "dt", "dd",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "pre", "blockquote", "hr", "br",
            "form", "fieldset", "iframe", "script", "style",
            "section", "article", "nav", "aside", "header", "footer", "main",
            "figure", "figcaption", "details", "summary",
            "video", "audio", "source", "canvas", "svg"
        };

        static bool IsHtmlBlockStart(string s, int pos)
        {
            if (pos >= s.Length || s[pos] != '<') return false;

            // <!-- comment -->
            if (pos + 3 < s.Length && s[pos + 1] == '!' && s[pos + 2] == '-' && s[pos + 3] == '-')
                return true;

            // <!DOCTYPE
            if (pos + 8 < s.Length && s[pos + 1] == '!')
            {
                if (MatchAtIgnoreCase(s, pos + 2, "DOCTYPE"))
                    return true;
            }

            // <tagname or </tagname
            int p = pos + 1;
            if (p < s.Length && s[p] == '/') p++;

            if (p >= s.Length || !IsAsciiLetter(s[p])) return false;

            int tagStart = p;
            while (p < s.Length && (IsAsciiLetter(s[p]) || char.IsDigit(s[p]))) p++;

            string tagName = s.Substring(tagStart, p - tagStart);
            return HtmlBlockTags.Contains(tagName);
        }

        static int ScanHtmlBlock(string s, int pos, StringBuilder sb)
        {
            // Determine the opening tag name so we can stop right after its matching
            // close tag — not only at a blank line. This lets Markdown that follows
            // immediately after </script>, </div>, </style>, etc. be parsed normally.
            // Comments, DOCTYPE and void/unclosed tags fall back to blank-line behaviour.
            string blockTag = GetBlockTagName(s, pos);
            int depth = 0;

            while (pos < s.Length)
            {
                if (IsBlankLine(s, pos)) break;

                int eol = FindEndOfLine(s, pos);
                sb.Append(s, pos, eol - pos);
                sb.Append('\n');

                if (blockTag != null)
                {
                    depth += CountTagOccurrences(s, pos, eol, blockTag, closing: false);
                    depth -= CountTagOccurrences(s, pos, eol, blockTag, closing: true);
                }

                pos = eol;
                if (pos < s.Length) pos = SkipNewline(s, pos);

                // Matching close tag reached → block ends here, Markdown resumes next line.
                if (blockTag != null && depth <= 0) break;
            }
            return pos;
        }

        /// <summary>Tag name at an HTML block start, or null for comments/DOCTYPE/non-tags.</summary>
        static string GetBlockTagName(string s, int pos)
        {
            if (pos >= s.Length || s[pos] != '<') return null;
            if (pos + 1 < s.Length && s[pos + 1] == '!') return null; // comment / DOCTYPE
            int p = pos + 1;
            if (p < s.Length && s[p] == '/') p++;
            if (p >= s.Length || !IsAsciiLetter(s[p])) return null;
            int tagStart = p;
            while (p < s.Length && (IsAsciiLetter(s[p]) || char.IsDigit(s[p]))) p++;
            return s.Substring(tagStart, p - tagStart);
        }

        /// <summary>Count opening (or closing) occurrences of a tag within [from, to).</summary>
        static int CountTagOccurrences(string s, int from, int to, string tag, bool closing)
        {
            int count = 0;
            for (int i = from; i < to; i++)
            {
                if (s[i] != '<') continue;
                int p = i + 1;
                bool isClose = (p < to && s[p] == '/');
                if (isClose) p++;
                if (isClose != closing) continue;
                if (!MatchAtIgnoreCase(s, p, tag)) continue;
                int after = p + tag.Length;
                // Reject longer names: <script> matches, <scriptx> does not.
                if (after < to && (IsAsciiLetter(s[after]) || char.IsDigit(s[after]))) continue;
                count++;
            }
            return count;
        }

        // ════════════════════════════════════════
        //  BLOCK: HEADING
        // ════════════════════════════════════════

        static int TryScanHeading(string s, int pos, StringBuilder sb)
        {
            // pos is at first #
            int start = pos;
            int level = 0;
            while (pos < s.Length && pos - start < 6 && s[pos] == '#')
            {
                level++;
                pos++;
            }

            // must be followed by a space
            if (pos >= s.Length || s[pos] != ' ')
            {
                return start; // not a heading, return original pos
            }
            pos++; // skip the space

            // capture content until end of line
            int contentStart = pos;
            int eol = FindEndOfLine(s, pos);

            // extract content, strip trailing #s
            string content = s.Substring(contentStart, eol - contentStart).TrimEnd();

            // remove optional closing ### with leading whitespace
            int trailEnd = content.Length;
            while (trailEnd > 0 && content[trailEnd - 1] == '#') trailEnd--;
            if (trailEnd < content.Length && trailEnd > 0 && content[trailEnd - 1] == ' ')
            {
                content = content.Substring(0, trailEnd).TrimEnd();
            }

            sb.Append("<h");
            sb.Append(level);
            sb.Append('>');
            sb.Append(ParseInline(content));
            sb.Append("</h");
            sb.Append(level);
            sb.Append(">\n");

            pos = eol;
            if (pos < s.Length) pos = SkipNewline(s, pos);
            return pos;
        }

        // ════════════════════════════════════════
        //  BLOCK: HORIZONTAL RULE
        // ════════════════════════════════════════

        static int TryScanHorizontalRule(string s, int pos, StringBuilder sb)
        {
            int start = pos;
            char ruleChar = s[pos];

            // must be - or *
            if (ruleChar != '-' && ruleChar != '*')
                return start;

            int count = 0;
            int scan = pos;
            int eol = FindEndOfLine(s, pos);

            while (scan < eol)
            {
                if (s[scan] == ruleChar) count++;
                else if (s[scan] != ' ' && s[scan] != '\t') return start; // non-rule char found
                scan++;
            }

            if (count < 3) return start;

            sb.Append("<hr>\n");
            pos = eol;
            if (pos < s.Length) pos = SkipNewline(s, pos);
            return pos;
        }

        // ════════════════════════════════════════
        //  BLOCK: BLOCKQUOTE
        // ════════════════════════════════════════

        static int ScanBlockquote(string s, int pos, StringBuilder sb)
        {
            // Collect inner content (strip leading > ), then recursively parse.
            var inner = new StringBuilder();

            while (pos < s.Length)
            {
                // skip leading whitespace
                int tempPos = pos;
                while (tempPos < s.Length && (s[tempPos] == ' ' || s[tempPos] == '\t')) tempPos++;

                if (tempPos < s.Length && s[tempPos] == '>')
                {
                    tempPos++; // skip >
                    if (tempPos < s.Length && s[tempPos] == ' ') tempPos++; // skip optional space

                    int eol = FindEndOfLine(s, tempPos);
                    inner.Append(s, tempPos, eol - tempPos);
                    inner.Append('\n');
                    pos = eol;
                    if (pos < s.Length) pos = SkipNewline(s, pos);
                }
                else if (!IsBlankLine(s, pos))
                {
                    // lazy continuation
                    int eol = FindEndOfLine(s, pos);
                    inner.Append(s, pos, eol - pos);
                    inner.Append('\n');
                    pos = eol;
                    if (pos < s.Length) pos = SkipNewline(s, pos);
                }
                else
                {
                    break;
                }
            }

            string innerHtml = ToHtmlImpl(inner.ToString());

            sb.Append("<blockquote>\n");
            sb.Append(innerHtml);
            sb.Append('\n');
            sb.Append("</blockquote>\n");

            return pos;
        }

        // ════════════════════════════════════════
        //  BLOCK: TABLE
        // ════════════════════════════════════════

        /// <summary>Does the current line contain an unescaped pipe?</summary>
        static bool LineContainsPipe(string s, int pos)
        {
            while (pos < s.Length && !IsNewline(s, pos))
            {
                if (s[pos] == '\\') { pos += 2; continue; }
                if (s[pos] == '|') return true;
                pos++;
            }
            return false;
        }

        /// <summary>Is the line after the current line a table separator row?</summary>
        static bool IsNextLineTableSeparator(string s, int pos)
        {
            // find end of current line
            int eol = FindEndOfLine(s, pos);
            int nextLine = SkipNewline(s, eol);
            if (nextLine >= s.Length) return false;

            return IsTableSeparatorLine(s, nextLine);
        }

        static bool IsTableSeparatorLine(string s, int pos)
        {
            // A separator line: only |, -, :, spaces/tabs allowed
            bool hasPipe = false;
            bool hasDash = false;
            int scan = pos;

            while (scan < s.Length && !IsNewline(s, scan))
            {
                char c = s[scan];
                if (c == '|') hasPipe = true;
                else if (c == '-') hasDash = true;
                else if (c == ':' || c == ' ' || c == '\t') { }
                else return false; // invalid char for separator
                scan++;
            }

            return hasPipe && hasDash;
        }

        /// <summary>Split a table row into cells, handling \| escapes.</summary>
        static List<string> SplitTableCells(string s, int pos, out int endPos)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            int eol = FindEndOfLine(s, pos);

            // skip leading pipe
            if (pos < eol && s[pos] == '|') pos++;

            while (pos < eol)
            {
                if (s[pos] == '\\' && pos + 1 < eol && s[pos + 1] == '|')
                {
                    // escaped pipe - literal |
                    cell.Append('|');
                    pos += 2;
                }
                else if (s[pos] == '|')
                {
                    cells.Add(cell.ToString());
                    cell.Length = 0;
                    pos++;
                }
                else
                {
                    cell.Append(s[pos]);
                    pos++;
                }
            }

            // trailing content after last pipe (or no trailing pipe)
            string remaining = cell.ToString().Trim();
            if (remaining.Length > 0)
            {
                cells.Add(cell.ToString());
            }

            endPos = eol;
            return cells;
        }

        /// <summary>Parse alignment from separator cells.</summary>
        static string GetAlignment(string cell)
        {
            cell = cell.Trim();
            if (cell.Length == 0) return null;

            bool left = cell[0] == ':';
            bool right = cell[cell.Length - 1] == ':';

            if (left && right) return "center";
            if (right) return "right";
            if (left) return "left";
            return null;
        }

        static int ScanTable(string s, int pos, StringBuilder sb)
        {
            // ── header row ──
            int endPos;
            List<string> headers = SplitTableCells(s, pos, out endPos);
            pos = endPos;
            if (pos < s.Length) pos = SkipNewline(s, pos);

            // ── separator row → extract alignment ──
            List<string> sepCells = SplitTableCells(s, pos, out endPos);
            var aligns = new string[sepCells.Count];
            for (int c = 0; c < sepCells.Count; c++)
            {
                aligns[c] = GetAlignment(sepCells[c]);
            }
            pos = endPos;
            if (pos < s.Length) pos = SkipNewline(s, pos);

            sb.Append("<div style=\"overflow-x:auto\">\n<table>\n");

            // ── thead ──
            sb.Append("<thead>\n<tr>");
            for (int c = 0; c < headers.Count; c++)
            {
                string align = c < aligns.Length ? aligns[c] : null;
                string style = align != null ? " style=\"text-align:" + align + "\"" : "";
                sb.Append("<th");
                sb.Append(style);
                sb.Append('>');
                sb.Append(ParseInline(headers[c].Trim()));
                sb.Append("</th>");
            }
            sb.Append("</tr>\n</thead>\n");

            // ── tbody ──
            sb.Append("<tbody>\n");
            while (pos < s.Length && !IsBlankLine(s, pos) && LineContainsPipe(s, pos))
            {
                List<string> cells = SplitTableCells(s, pos, out endPos);
                sb.Append("<tr>");
                for (int c = 0; c < cells.Count; c++)
                {
                    string align = c < aligns.Length ? aligns[c] : null;
                    string style = align != null ? " style=\"text-align:" + align + "\"" : "";
                    sb.Append("<td");
                    sb.Append(style);
                    sb.Append('>');
                    sb.Append(ParseInline(cells[c].Trim()));
                    sb.Append("</td>");
                }
                sb.Append("</tr>\n");
                pos = endPos;
                if (pos < s.Length) pos = SkipNewline(s, pos);
            }
            sb.Append("</tbody>\n</table>\n</div>\n");

            return pos;
        }

        // ════════════════════════════════════════
        //  BLOCK: LISTS
        // ════════════════════════════════════════

        static bool IsUnorderedListMarker(string s, int pos)
        {
            if (pos + 1 >= s.Length) return false;
            char c = s[pos];
            return (c == '-' || c == '*' || c == '+') && s[pos + 1] == ' ';
        }

        static bool IsOrderedListMarker(string s, int pos, out int markerEnd)
        {
            markerEnd = pos;
            int p = pos;
            if (p >= s.Length || !char.IsDigit(s[p])) return false;

            while (p < s.Length && char.IsDigit(s[p])) p++;
            if (p >= s.Length) return false;
            if (s[p] != '.' && s[p] != ')') return false;
            p++;
            if (p >= s.Length || s[p] != ' ') return false;

            markerEnd = p + 1; // position after "1. " - the content start
            return true;
        }

        static int ScanList(string s, int lineStart, StringBuilder sb, bool ordered)
        {
            string tag = ordered ? "ol" : "ul";
            int pos = lineStart;

            // determine base indent
            int baseIndent;
            SkipIndent(s, pos, out baseIndent);

            var items = new List<ListItemData>();

            while (pos < s.Length)
            {
                // blank line handling
                if (IsBlankLine(s, pos))
                {
                    // peek past blank lines for continuation
                    int peek = pos;
                    while (peek < s.Length && IsBlankLine(s, peek))
                    {
                        peek = SkipNewline(s, FindEndOfLine(s, peek));
                    }

                    if (peek < s.Length)
                    {
                        int peekIndent;
                        int peekContent = SkipIndent(s, peek, out peekIndent);
                        if (peekIndent >= baseIndent &&
                            (IsUnorderedListMarker(s, peekContent) || IsOrderedListMarkerAt(s, peekContent)))
                        {
                            pos = peek;
                            continue;
                        }
                    }
                    break;
                }

                int lineIndent;
                int contentPos = SkipIndent(s, pos, out lineIndent);

                if (lineIndent < baseIndent) break;

                // check for list marker at base indent level
                if (lineIndent == baseIndent)
                {
                    bool isUl = IsUnorderedListMarker(s, contentPos);
                    int olEnd;
                    bool isOl = IsOrderedListMarker(s, contentPos, out olEnd);

                    if (isUl || isOl)
                    {
                        int markerContentStart;
                        if (isUl)
                        {
                            markerContentStart = contentPos + 2; // skip "- "
                        }
                        else
                        {
                            markerContentStart = olEnd;
                        }

                        int eol = FindEndOfLine(s, markerContentStart);
                        string itemContent = s.Substring(markerContentStart, eol - markerContentStart);

                        items.Add(new ListItemData
                        {
                            Content = itemContent,
                            IsOrdered = isOl,
                            ChildLines = new List<string>()
                        });

                        pos = eol;
                        if (pos < s.Length) pos = SkipNewline(s, pos);
                        continue;
                    }

                    // not a list marker at base indent - end of list
                    break;
                }

                // indented content → belongs to last item as child
                if (lineIndent > baseIndent && items.Count > 0)
                {
                    int eol = FindEndOfLine(s, pos);
                    string childLine = s.Substring(pos, eol - pos);
                    items[items.Count - 1].ChildLines.Add(childLine);
                    pos = eol;
                    if (pos < s.Length) pos = SkipNewline(s, pos);
                    continue;
                }

                break;
            }

            // render
            sb.Append('<');
            sb.Append(tag);
            sb.Append(">\n");

            foreach (var item in items)
            {
                sb.Append("<li>");
                sb.Append(ParseInline(item.Content));

                if (item.ChildLines.Count > 0)
                {
                    // dedent children and recursively parse
                    var childBlock = new StringBuilder();
                    int minDedent = baseIndent + 2;
                    foreach (string childLine in item.ChildLines)
                    {
                        int ci;
                        int cp = SkipIndent(childLine, 0, out ci);
                        if (ci >= minDedent)
                        {
                            // Remove minDedent worth of indent
                            int charsToRemove = 0;
                            int removed = 0;
                            while (charsToRemove < childLine.Length && removed < minDedent)
                            {
                                if (childLine[charsToRemove] == '\t') removed += 4;
                                else removed++;
                                charsToRemove++;
                            }
                            childBlock.Append(childLine, charsToRemove, childLine.Length - charsToRemove);
                        }
                        else
                        {
                            childBlock.Append(childLine.TrimStart());
                        }
                        childBlock.Append('\n');
                    }

                    string childHtml = ToHtmlImpl(childBlock.ToString());
                    if (childHtml.Length > 0)
                    {
                        sb.Append('\n');
                        sb.Append(childHtml);
                    }
                }

                sb.Append("</li>\n");
            }

            sb.Append("</");
            sb.Append(tag);
            sb.Append(">\n");

            return pos;
        }

        static bool IsOrderedListMarkerAt(string s, int pos)
        {
            int dummy;
            return IsOrderedListMarker(s, pos, out dummy);
        }

        static int TryScanOrderedList(string s, int lineStart, StringBuilder sb)
        {
            int indent;
            int contentPos = SkipIndent(s, lineStart, out indent);
            int markerEnd;
            if (IsOrderedListMarker(s, contentPos, out markerEnd))
            {
                return ScanList(s, lineStart, sb, ordered: true);
            }
            return lineStart;
        }

        class ListItemData
        {
            public string Content;
            public bool IsOrdered;
            public List<string> ChildLines;
        }

        // ════════════════════════════════════════
        //  BLOCK: PARAGRAPH
        // ════════════════════════════════════════

        static int ScanParagraph(string s, int pos, StringBuilder sb)
        {
            var lines = new List<string>();

            while (pos < s.Length)
            {
                if (IsBlankLine(s, pos)) break;

                // peek at what this line starts with (after indent)
                int peekIndent;
                int peekContent = SkipIndent(s, pos, out peekIndent);

                if (peekContent < s.Length)
                {
                    char c = s[peekContent];

                    // stop if we hit a block-level element
                    if (c == '#' && TryPeekHeading(s, peekContent)) break;
                    if (c == '`' && peekContent + 2 < s.Length && s[peekContent + 1] == '`' && s[peekContent + 2] == '`') break;
                    if (c == '>') break;
                    if ((c == '-' || c == '*') && IsPeekHorizontalRule(s, peekContent)) break;
                    if ((c == '-' || c == '*' || c == '+') && peekContent + 1 < s.Length && s[peekContent + 1] == ' ') break;
                    if (char.IsDigit(c) && IsOrderedListMarkerAt(s, peekContent)) break;
                    if (_allowHtmlPassThrough && c == '<' && IsHtmlBlockStart(s, peekContent)) break;
                    if (LineContainsPipe(s, peekContent) && IsNextLineTableSeparator(s, peekContent)) break;
                }

                // skip the first line - it's already been checked by the main loop,
                // but for continuation lines we need the break checks above
                int eol = FindEndOfLine(s, pos);
                lines.Add(s.Substring(pos, eol - pos));
                pos = eol;
                if (pos < s.Length) pos = SkipNewline(s, pos);
            }

            if (lines.Count > 0)
            {
                sb.Append("<p>");
                string pendingSeparator = null; // null = no separator before the first line

                for (int j = 0; j < lines.Count; j++)
                {
                    string pLine = lines[j];

                    if (pendingSeparator != null)
                        sb.Append(pendingSeparator);

                    bool hardBreak = false;

                    // two or more trailing spaces = hard line break (CommonMark)
                    int trailingSpaces = 0;
                    while (trailingSpaces < pLine.Length &&
                           pLine[pLine.Length - 1 - trailingSpaces] == ' ')
                    {
                        trailingSpaces++;
                    }
                    if (trailingSpaces >= 2)
                    {
                        hardBreak = true;
                        pLine = pLine.Substring(0, pLine.Length - trailingSpaces);
                    }

                    // strip trailing backslash (explicit line break marker)
                    string trimmed = pLine.TrimEnd();
                    if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '\\')
                    {
                        pLine = trimmed.Substring(0, trimmed.Length - 1);
                        hardBreak = true; // backslash at line end forces a hard break
                    }

                    // a literal trailing <br> already provides the break itself (it
                    // will be emitted verbatim by ParseInline below via HTML
                    // passthrough) — the next line follows it with NO extra
                    // separator, otherwise we'd double up into "<br><br>".
                    trimmed = pLine.TrimEnd();
                    bool endsWithLiteralBr = trimmed.EndsWith("<br>") || trimmed.EndsWith("<br />") || trimmed.EndsWith("<br/>");

                    sb.Append(ParseInline(pLine));

                    if (endsWithLiteralBr) pendingSeparator = "";
                    else if (hardBreak) pendingSeparator = "<br>";
                    else pendingSeparator = " ";
                }
                sb.Append("</p>\n");
            }

            return pos;
        }

        /// <summary>Peek: is this a valid heading (1-6 # followed by space)?</summary>
        /// Must mirror TryScanHeading exactly: at most 6 '#', then a space.
        /// 7+ hashes is NOT a heading, so a paragraph must consume the line.
        /// (If this peek and TryScanHeading ever disagree, the main loop can
        /// livelock: TryScanHeading rejects the line without advancing, while
        /// ScanParagraph breaks on it without advancing either.)
        static bool TryPeekHeading(string s, int pos)
        {
            int start = pos;
            while (pos < s.Length && pos - start < 6 && s[pos] == '#') pos++;
            // Reject if a 7th '#' immediately follows (over the 6 cap).
            if (pos < s.Length && s[pos] == '#') return false;
            return pos < s.Length && s[pos] == ' ';
        }

        /// <summary>Peek: is this a horizontal rule from current position to end of line?</summary>
        static bool IsPeekHorizontalRule(string s, int pos)
        {
            int eol = FindEndOfLine(s, pos);
            if (eol - pos < 3) return false;

            char ruleChar = s[pos];
            if (ruleChar != '-' && ruleChar != '*') return false;

            int count = 0;
            for (int scan = pos; scan < eol; scan++)
            {
                if (s[scan] == ruleChar) count++;
                else if (s[scan] != ' ' && s[scan] != '\t') return false;
            }
            return count >= 3;
        }

        // ════════════════════════════════════════
        //  INLINE PARSER
        // ════════════════════════════════════════

        static string ParseInline(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder();
            int i = 0;
            int len = text.Length;

            while (i < len)
            {
                char c = text[i];

                // ── backslash escape ──
                if (c == '\\' && i + 1 < len)
                {
                    char next = text[i + 1];
                    if (IsEscapable(next))
                    {
                        AppendHtmlEncoded(sb, next);
                        i += 2;
                        continue;
                    }
                }

                // ── inline code ──
                if (c == '`')
                {
                    int tickCount = CountRun(text, i, '`');
                    int closePos = FindClosingBackticks(text, i + tickCount, tickCount);
                    if (closePos >= 0)
                    {
                        int codeStart = i + tickCount;
                        int codeLen = closePos - codeStart;
                        string code = text.Substring(codeStart, codeLen);
                        // trim one leading/trailing space if both present
                        if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ')
                        {
                            code = code.Substring(1, code.Length - 2);
                        }
                        sb.Append("<code>");
                        sb.Append(HttpUtility.HtmlEncode(code));
                        sb.Append("</code>");
                        i = closePos + tickCount;
                        continue;
                    }
                }

                // ── image ![alt](url) - check before link ──
                if (c == '!' && i + 1 < len && text[i + 1] == '[')
                {
                    int result = TryParseImageOrLink(text, i, sb, isImage: true);
                    if (result > i) { i = result; continue; }
                }

                // ── link [text](url) ──
                if (c == '[')
                {
                    int result = TryParseImageOrLink(text, i, sb, isImage: false);
                    if (result > i) { i = result; continue; }
                }

                // ── bold + italic (***) ──
                if (c == '*' && i + 2 < len && text[i + 1] == c && text[i + 2] == c)
                {
                    int close = FindClosingRun(text, i + 3, c, 3);
                    if (close >= 0)
                    {
                        string inner = text.Substring(i + 3, close - i - 3);
                        sb.Append("<strong><em>");
                        sb.Append(ParseInline(inner));
                        sb.Append("</em></strong>");
                        i = close + 3;
                        continue;
                    }
                }

                // ── bold (**) ──
                if (c == '*' && i + 1 < len && text[i + 1] == c)
                {
                    int close = FindClosingRun(text, i + 2, c, 2);
                    if (close >= 0)
                    {
                        string inner = text.Substring(i + 2, close - i - 2);
                        sb.Append("<strong>");
                        sb.Append(ParseInline(inner));
                        sb.Append("</strong>");
                        i = close + 2;
                        continue;
                    }
                }

                // ── italic (*) ──
                if (c == '*')
                {
                    int close = FindClosingRun(text, i + 1, c, 1);
                    if (close >= 0)
                    {
                        string inner = text.Substring(i + 1, close - i - 1);
                        sb.Append("<em>");
                        sb.Append(ParseInline(inner));
                        sb.Append("</em>");
                        i = close + 1;
                        continue;
                    }
                }

                // ── strikethrough ~~ ──
                if (c == '~' && i + 1 < len && text[i + 1] == '~')
                {
                    int close = FindDoubleChar(text, i + 2, '~');
                    if (close >= 0)
                    {
                        string inner = text.Substring(i + 2, close - i - 2);
                        sb.Append("<del>");
                        sb.Append(ParseInline(inner));
                        sb.Append("</del>");
                        i = close + 2;
                        continue;
                    }
                }

                // ── auto-link bare URLs ──
                if (c == 'h' && MatchAt(text, i, "http"))
                {
                    int urlEnd = ScanUrlEnd(text, i);
                    if (urlEnd > i)
                    {
                        string url = text.Substring(i, urlEnd - i);
                        // trim trailing punctuation
                        while (url.Length > 0 && ".,;:!?)".IndexOf(url[url.Length - 1]) >= 0)
                        {
                            url = url.Substring(0, url.Length - 1);
                        }
                        sb.Append("<a href=\"");
                        sb.Append(HttpUtility.HtmlAttributeEncode(url));
                        sb.Append("\">");
                        sb.Append(HttpUtility.HtmlEncode(url));
                        sb.Append("</a>");
                        i += url.Length;
                        continue;
                    }
                }

                // ── HTML tags passthrough ──
                if (c == '<')
                {
                    if (_allowHtmlPassThrough)
                    {
                        int closeAngle = text.IndexOf('>', i + 1);
                        if (closeAngle >= 0)
                        {
                            int tp = i + 1;
                            if (tp < len && text[tp] == '/') tp++;
                            if (tp < len && IsAsciiLetter(text[tp]))
                            {
                                while (tp < closeAngle && (IsAsciiLetter(text[tp]) || char.IsDigit(text[tp]))) tp++;
                                sb.Append(text, i, closeAngle - i + 1);
                                i = closeAngle + 1;
                                continue;
                            }
                        }
                    }
                    sb.Append("&lt;");
                    i++;
                    continue;
                }

                if (c == '>')
                {
                    sb.Append("&gt;");
                    i++;
                    continue;
                }

                // ── ampersand / HTML entities ──
                if (c == '&')
                {
                    int entityEnd = TryScanHtmlEntity(text, i);
                    if (entityEnd > i)
                    {
                        // valid entity - pass through as-is
                        sb.Append(text, i, entityEnd - i);
                        i = entityEnd;
                        continue;
                    }
                    sb.Append("&amp;");
                    i++;
                    continue;
                }

                // ── default: append as-is ──
                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        // ════════════════════════════════════════
        //  INLINE HELPERS
        // ════════════════════════════════════════

        static bool IsEscapable(char c)
        {
            return @"\`*_{}[]()#+-.!|~>".IndexOf(c) >= 0;
        }

        static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        static void AppendHtmlEncoded(StringBuilder sb, char c)
        {
            switch (c)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }

        static int CountRun(string s, int pos, char c)
        {
            int count = 0;
            while (pos + count < s.Length && s[pos + count] == c) count++;
            return count;
        }

        static int FindClosingBackticks(string text, int start, int count)
        {
            for (int j = start; j <= text.Length - count; j++)
            {
                if (text[j] == '`')
                {
                    int run = CountRun(text, j, '`');
                    if (run == count) return j;
                    j += run - 1;
                }
            }
            return -1;
        }

        static int FindClosingRun(string text, int start, char c, int runLength)
        {
            for (int j = start; j <= text.Length - runLength; j++)
            {
                if (text[j] == '\\' && j + 1 < text.Length)
                {
                    j++; // skip escaped char
                    continue;
                }
                if (text[j] == c)
                {
                    int run = CountRun(text, j, c);
                    if (run == runLength) return j;
                    j += run - 1;
                }
            }
            return -1;
        }

        static int FindDoubleChar(string text, int start, char c)
        {
            for (int j = start; j + 1 < text.Length; j++)
            {
                if (text[j] == c && text[j + 1] == c) return j;
            }
            return -1;
        }

        static bool MatchAt(string text, int pos, string match)
        {
            if (pos + match.Length > text.Length) return false;
            for (int j = 0; j < match.Length; j++)
            {
                if (text[pos + j] != match[j]) return false;
            }
            return true;
        }

        static bool MatchAtIgnoreCase(string s, int pos, string match)
        {
            if (pos + match.Length > s.Length) return false;
            for (int j = 0; j < match.Length; j++)
            {
                if (char.ToLowerInvariant(s[pos + j]) != char.ToLowerInvariant(match[j])) return false;
            }
            return true;
        }

        /// <summary>Scan a URL starting at pos (must start with http:// or https://).</summary>
        static int ScanUrlEnd(string text, int pos)
        {
            // verify prefix
            if (!MatchAt(text, pos, "http://") && !MatchAt(text, pos, "https://"))
                return pos;

            int p = pos;
            while (p < text.Length)
            {
                char c = text[p];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' ||
                    c == '<' || c == '>' || c == '[' || c == ']' ||
                    c == '"' || c == '\'' || c == '`')
                    break;
                p++;
            }
            return p;
        }

        /// <summary>Try to scan an HTML entity at pos. Returns end position if valid, pos otherwise.</summary>
        static int TryScanHtmlEntity(string text, int pos)
        {
            // &amp; &#123; &#x1F;
            if (pos >= text.Length || text[pos] != '&') return pos;

            int p = pos + 1;
            if (p >= text.Length) return pos;

            if (text[p] == '#')
            {
                p++;
                if (p >= text.Length) return pos;

                if (text[p] == 'x' || text[p] == 'X')
                {
                    // hex entity &#xABC;
                    p++;
                    int digits = 0;
                    while (p < text.Length && IsHexDigit(text[p])) { p++; digits++; }
                    if (digits == 0 || p >= text.Length || text[p] != ';') return pos;
                    return p + 1;
                }
                else
                {
                    // decimal entity &#123;
                    int digits = 0;
                    while (p < text.Length && char.IsDigit(text[p])) { p++; digits++; }
                    if (digits == 0 || p >= text.Length || text[p] != ';') return pos;
                    return p + 1;
                }
            }
            else
            {
                // named entity &amp;
                if (!IsAsciiLetter(text[p])) return pos;
                while (p < text.Length && IsAsciiLetter(text[p])) p++;
                if (p >= text.Length || text[p] != ';') return pos;
                return p + 1;
            }
        }

        static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        static int TryParseImageOrLink(string text, int i, StringBuilder sb, bool isImage)
        {
            int start = isImage ? i + 2 : i + 1; // skip ![ or [

            // find closing ]
            int bracketClose = FindMatchingBracket(text, start);
            if (bracketClose < 0) return i;

            // must be followed by (
            if (bracketClose + 1 >= text.Length || text[bracketClose + 1] != '(') return i;

            // find closing )
            int parenClose = text.IndexOf(')', bracketClose + 2);
            if (parenClose < 0) return i;

            string label = text.Substring(start, bracketClose - start);
            string urlPart = text.Substring(bracketClose + 2, parenClose - bracketClose - 2).Trim();

            // split url and optional title
            string url = urlPart;
            string title = null;

            if (urlPart.Length > 0)
            {
                // find title after url: [text](url "title") or [text](url 'title')
                int spacePos = urlPart.IndexOf(' ');
                if (spacePos < 0) spacePos = urlPart.IndexOf('\t');

                if (spacePos >= 0)
                {
                    string afterSpace = urlPart.Substring(spacePos + 1).Trim();
                    if (afterSpace.Length >= 2 && (afterSpace[0] == '"' || afterSpace[0] == '\''))
                    {
                        char titleQuote = afterSpace[0];
                        if (afterSpace[afterSpace.Length - 1] == titleQuote)
                        {
                            title = afterSpace.Substring(1, afterSpace.Length - 2);
                            url = urlPart.Substring(0, spacePos);
                        }
                    }
                }
            }

            string encodedUrl = HttpUtility.HtmlAttributeEncode(url.Trim());
            string titleAttr = title != null
                ? " title=\"" + HttpUtility.HtmlAttributeEncode(title) + "\""
                : "";

            if (isImage)
            {
                sb.Append("<img src=\"");
                sb.Append(encodedUrl);
                sb.Append("\" alt=\"");
                sb.Append(HttpUtility.HtmlAttributeEncode(label));
                sb.Append('"');
                sb.Append(titleAttr);
                sb.Append('>');
            }
            else
            {
                sb.Append("<a href=\"");
                sb.Append(encodedUrl);
                sb.Append('"');
                sb.Append(titleAttr);
                sb.Append('>');
                sb.Append(ParseInline(label));
                sb.Append("</a>");
            }

            return parenClose + 1;
        }

        static int FindMatchingBracket(string text, int start)
        {
            int depth = 1;
            for (int j = start; j < text.Length; j++)
            {
                if (text[j] == '\\' && j + 1 < text.Length)
                {
                    j++; // skip escaped
                    continue;
                }
                if (text[j] == '[') depth++;
                if (text[j] == ']') depth--;
                if (depth == 0) return j;
            }
            return -1;
        }
    }
}
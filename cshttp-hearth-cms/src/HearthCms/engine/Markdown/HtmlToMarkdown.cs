using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using CsHttp.Web;

// Markdown to HTML Parser/Converter
// https://github.com/adriancs2/csharp-Markdown-To-Html
// Version: 1.1

namespace System.engine.Markdown
{
    /// <summary>
    /// Single-pass character-scanning HTML-to-Markdown converter.
    /// No Regex, no string.Split. Walks the raw input char-by-char.
    /// 
    /// Strips all tag attributes/classes. Unsupported tags are removed
    /// (their inner content is kept). Supported: headings, paragraphs,
    /// bold, italic, bold-italic, links, images, inline code, fenced
    /// code blocks, unordered/ordered lists with nesting, blockquotes,
    /// horizontal rules, tables, strikethrough, line breaks.
    /// </summary>
    public static class HtmlToMarkdown
    {
        // ════════════════════════════════════════
        //  PUBLIC ENTRY
        // ════════════════════════════════════════

        public static string ToMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";

            // first pass: parse into a flat list of tokens (tags + text)
            List<Token> tokens = Tokenize(html);

            // second pass: walk tokens and emit markdown
            var sb = new StringBuilder();
            var ctx = new ConvertContext();
            ConvertTokens(tokens, 0, tokens.Count, sb, ctx);

            // clean up excessive blank lines (max 2 newlines in a row)
            string result = CollapseBlankLines(sb.ToString());

            // trim leading/trailing whitespace
            return result.Trim();
        }

        // ════════════════════════════════════════
        //  TOKEN TYPES
        // ════════════════════════════════════════

        enum TokenType
        {
            Text,
            OpenTag,
            CloseTag,
            SelfClosingTag,
            Comment
        }

        class Token
        {
            public TokenType Type;
            public string TagName;   // lowercase, for tags only
            public string RawText;   // for Text tokens: the text content
            public string RawTag;    // the full raw tag string (for extracting attributes we need)
        }

        class ConvertContext
        {
            public int ListDepth = 0;
            public bool InPre = false;
            public bool InCode = false;
            public bool LastBlockEndsNewline = false;
        }

        // ════════════════════════════════════════
        //  TOKENIZER (char-by-char)
        // ════════════════════════════════════════

        static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            int pos = 0;
            int len = s.Length;
            var textBuf = new StringBuilder();

            while (pos < len)
            {
                if (s[pos] == '<')
                {
                    // flush accumulated text
                    if (textBuf.Length > 0)
                    {
                        tokens.Add(new Token { Type = TokenType.Text, RawText = textBuf.ToString() });
                        textBuf.Length = 0;
                    }

                    // scan the tag
                    int tagStart = pos;
                    pos = ScanTag(s, pos, tokens);

                    // if ScanTag didn't consume anything (malformed), treat < as text
                    if (pos == tagStart)
                    {
                        textBuf.Append('<');
                        pos++;
                    }
                }
                else
                {
                    textBuf.Append(s[pos]);
                    pos++;
                }
            }

            // flush remaining text
            if (textBuf.Length > 0)
            {
                tokens.Add(new Token { Type = TokenType.Text, RawText = textBuf.ToString() });
            }

            return tokens;
        }

        /// <summary>Scan a single HTML tag starting at '&lt;'. Returns new position.</summary>
        static int ScanTag(string s, int pos, List<Token> tokens)
        {
            if (pos >= s.Length || s[pos] != '<') return pos;

            int start = pos;
            pos++; // skip <

            if (pos >= s.Length) return start;

            // ── HTML comment <!--  --> ──
            if (pos + 2 < s.Length && s[pos] == '!' && s[pos + 1] == '-' && s[pos + 2] == '-')
            {
                // scan to -->
                pos += 3;
                while (pos + 2 < s.Length)
                {
                    if (s[pos] == '-' && s[pos + 1] == '-' && s[pos + 2] == '>')
                    {
                        pos += 3;
                        tokens.Add(new Token { Type = TokenType.Comment });
                        return pos;
                    }
                    pos++;
                }
                // unterminated comment — consume to end
                tokens.Add(new Token { Type = TokenType.Comment });
                return s.Length;
            }

            // ── DOCTYPE ──
            if (pos < s.Length && s[pos] == '!')
            {
                // skip to >
                while (pos < s.Length && s[pos] != '>') pos++;
                if (pos < s.Length) pos++; // skip >
                tokens.Add(new Token { Type = TokenType.Comment }); // treat as ignorable
                return pos;
            }

            // ── closing tag </tag> ──
            bool isClose = false;
            if (pos < s.Length && s[pos] == '/')
            {
                isClose = true;
                pos++;
            }

            // read tag name
            int nameStart = pos;
            while (pos < s.Length && IsTagNameChar(s[pos]))
            {
                pos++;
            }

            if (pos == nameStart) return start; // no tag name found — malformed

            string tagName = s.Substring(nameStart, pos - nameStart).ToLowerInvariant();

            // scan past attributes to find > or />
            // we also capture the raw tag for extracting href/src/alt later
            bool selfClosing = false;

            while (pos < s.Length && s[pos] != '>')
            {
                if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '>')
                {
                    selfClosing = true;
                    pos++; // skip /, the > will be skipped below
                    break;
                }

                // skip over quoted attribute values
                if (s[pos] == '"')
                {
                    pos++;
                    while (pos < s.Length && s[pos] != '"') pos++;
                    if (pos < s.Length) pos++; // skip closing "
                    continue;
                }
                if (s[pos] == '\'')
                {
                    pos++;
                    while (pos < s.Length && s[pos] != '\'') pos++;
                    if (pos < s.Length) pos++; // skip closing '
                    continue;
                }

                pos++;
            }

            if (pos < s.Length && s[pos] == '>') pos++; // skip >

            string rawTag = s.Substring(start, pos - start);

            if (isClose)
            {
                tokens.Add(new Token { Type = TokenType.CloseTag, TagName = tagName, RawTag = rawTag });
            }
            else if (selfClosing || IsSelfClosingTag(tagName))
            {
                tokens.Add(new Token { Type = TokenType.SelfClosingTag, TagName = tagName, RawTag = rawTag });
            }
            else
            {
                tokens.Add(new Token { Type = TokenType.OpenTag, TagName = tagName, RawTag = rawTag });
            }

            return pos;
        }

        static bool IsTagNameChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                   (c >= '0' && c <= '9') || c == '-';
        }

        static readonly HashSet<string> SelfClosingTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "hr", "img", "input", "meta", "link", "source", "wbr", "col", "area", "base", "embed", "track"
        };

        static bool IsSelfClosingTag(string tag)
        {
            return SelfClosingTags.Contains(tag);
        }

        // ════════════════════════════════════════
        //  ATTRIBUTE EXTRACTION (char-by-char)
        // ════════════════════════════════════════

        /// <summary>Extract a specific attribute value from a raw tag string.</summary>
        static string GetAttribute(string rawTag, string attrName)
        {
            if (string.IsNullOrEmpty(rawTag)) return null;

            int pos = 0;
            int len = rawTag.Length;

            // skip past the tag name part: <tagname
            if (pos < len && rawTag[pos] == '<') pos++;
            if (pos < len && rawTag[pos] == '/') pos++;

            // skip tag name
            while (pos < len && IsTagNameChar(rawTag[pos])) pos++;

            // now scan attributes
            while (pos < len)
            {
                // skip whitespace
                while (pos < len && (rawTag[pos] == ' ' || rawTag[pos] == '\t' ||
                       rawTag[pos] == '\r' || rawTag[pos] == '\n')) pos++;

                if (pos >= len || rawTag[pos] == '>' || rawTag[pos] == '/') break;

                // read attribute name
                int attrStart = pos;
                while (pos < len && rawTag[pos] != '=' && rawTag[pos] != ' ' &&
                       rawTag[pos] != '>' && rawTag[pos] != '/' &&
                       rawTag[pos] != '\t' && rawTag[pos] != '\r' && rawTag[pos] != '\n')
                {
                    pos++;
                }

                string foundAttr = rawTag.Substring(attrStart, pos - attrStart);

                // skip whitespace
                while (pos < len && (rawTag[pos] == ' ' || rawTag[pos] == '\t')) pos++;

                if (pos < len && rawTag[pos] == '=')
                {
                    pos++; // skip =

                    // skip whitespace
                    while (pos < len && (rawTag[pos] == ' ' || rawTag[pos] == '\t')) pos++;

                    string value = null;

                    if (pos < len && (rawTag[pos] == '"' || rawTag[pos] == '\''))
                    {
                        char quote = rawTag[pos];
                        pos++; // skip opening quote
                        int valStart = pos;
                        while (pos < len && rawTag[pos] != quote) pos++;
                        value = rawTag.Substring(valStart, pos - valStart);
                        if (pos < len) pos++; // skip closing quote
                    }
                    else
                    {
                        // unquoted value
                        int valStart = pos;
                        while (pos < len && rawTag[pos] != ' ' && rawTag[pos] != '>' &&
                               rawTag[pos] != '\t' && rawTag[pos] != '/') pos++;
                        value = rawTag.Substring(valStart, pos - valStart);
                    }

                    if (StringEqualsIgnoreCase(foundAttr, attrName))
                    {
                        return DecodeHtmlEntities(value);
                    }
                }
                else
                {
                    // boolean attribute (no value)
                    if (StringEqualsIgnoreCase(foundAttr, attrName))
                    {
                        return "";
                    }
                }
            }

            return null;
        }

        static bool StringEqualsIgnoreCase(string a, string b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i])) return false;
            }
            return true;
        }

        // ════════════════════════════════════════
        //  HTML ENTITY DECODING (char-by-char)
        // ════════════════════════════════════════

        static string DecodeHtmlEntities(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;

            var sb = new StringBuilder(s.Length);
            int pos = 0;

            while (pos < s.Length)
            {
                if (s[pos] == '&')
                {
                    int entityEnd = FindEntityEnd(s, pos);
                    if (entityEnd > pos)
                    {
                        string entity = s.Substring(pos, entityEnd - pos);
                        string decoded = DecodeEntity(entity);
                        sb.Append(decoded);
                        pos = entityEnd;
                        continue;
                    }
                }
                sb.Append(s[pos]);
                pos++;
            }

            return sb.ToString();
        }

        static int FindEntityEnd(string s, int pos)
        {
            if (pos >= s.Length || s[pos] != '&') return pos;
            int p = pos + 1;

            if (p < s.Length && s[p] == '#')
            {
                p++;
                if (p < s.Length && (s[p] == 'x' || s[p] == 'X'))
                {
                    p++;
                    while (p < s.Length && IsHexDigit(s[p])) p++;
                }
                else
                {
                    while (p < s.Length && s[p] >= '0' && s[p] <= '9') p++;
                }
            }
            else
            {
                while (p < s.Length && ((s[p] >= 'a' && s[p] <= 'z') || (s[p] >= 'A' && s[p] <= 'Z'))) p++;
            }

            if (p < s.Length && s[p] == ';') return p + 1;
            return pos; // not a valid entity
        }

        static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        static string DecodeEntity(string entity)
        {
            // common named entities
            switch (entity)
            {
                case "&amp;": return "&";
                case "&lt;": return "<";
                case "&gt;": return ">";
                case "&quot;": return "\"";
                case "&apos;": return "'";
                case "&nbsp;": return " ";
                case "&ndash;": return "\u2013";
                case "&mdash;": return "\u2014";
                case "&laquo;": return "\u00AB";
                case "&raquo;": return "\u00BB";
                case "&copy;": return "\u00A9";
                case "&reg;": return "\u00AE";
                case "&trade;": return "\u2122";
                case "&hellip;": return "\u2026";
            }

            // numeric entities &#123; &#x1F;
            if (entity.Length > 3 && entity[1] == '#')
            {
                try
                {
                    int codePoint;
                    if (entity[2] == 'x' || entity[2] == 'X')
                    {
                        string hex = entity.Substring(3, entity.Length - 4); // strip &#x and ;
                        codePoint = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                    }
                    else
                    {
                        string dec = entity.Substring(2, entity.Length - 3); // strip &# and ;
                        codePoint = int.Parse(dec);
                    }
                    return char.ConvertFromUtf32(codePoint);
                }
                catch
                {
                    return entity; // return as-is if invalid
                }
            }

            return entity; // unknown entity — return as-is
        }

        // ════════════════════════════════════════
        //  SUPPORTED TAG SET
        // ════════════════════════════════════════

        static readonly HashSet<string> SupportedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // block
            "h1", "h2", "h3", "h4", "h5", "h6",
            "p", "blockquote", "pre", "code", "hr", "br",
            // list
            "ul", "ol", "li",
            // table
            "table", "thead", "tbody", "tfoot", "tr", "th", "td",
            // inline
            "strong", "b", "em", "i", "del", "s", "strike",
            "a", "img",
            // structural (kept for parsing, produce no markdown wrapper)
            "div", "span", "section", "article", "main", "header", "footer",
            "nav", "aside", "figure", "figcaption", "details", "summary"
        };

        static bool IsSupported(string tagName)
        {
            return SupportedTags.Contains(tagName);
        }

        // ════════════════════════════════════════
        //  TOKEN CONVERTER
        // ════════════════════════════════════════

        static void ConvertTokens(List<Token> tokens, int from, int to, StringBuilder sb, ConvertContext ctx)
        {
            int i = from;

            while (i < to)
            {
                Token t = tokens[i];

                if (t.Type == TokenType.Comment)
                {
                    i++;
                    continue;
                }

                if (t.Type == TokenType.Text)
                {
                    string text = t.RawText;

                    if (ctx.InPre)
                    {
                        // inside <pre> — preserve text as-is (no collapsing)
                        sb.Append(text);
                    }
                    else
                    {
                        // collapse whitespace, decode entities
                        string collapsed = CollapseWhitespace(text);
                        string decoded = DecodeHtmlEntities(collapsed);
                        if (decoded.Length > 0)
                        {
                            sb.Append(decoded);
                        }
                    }
                    i++;
                    continue;
                }

                if (t.Type == TokenType.SelfClosingTag)
                {
                    HandleSelfClosingTag(t, sb, ctx);
                    i++;
                    continue;
                }

                if (t.Type == TokenType.CloseTag)
                {
                    // orphan close tag — skip
                    i++;
                    continue;
                }

                if (t.Type == TokenType.OpenTag)
                {
                    // find matching close tag
                    int closeIdx = FindMatchingClose(tokens, i, to);
                    int innerFrom = i + 1;
                    int innerTo = closeIdx < to ? closeIdx : to;

                    HandleOpenCloseBlock(tokens, i, innerFrom, innerTo, sb, ctx);

                    i = (closeIdx < to) ? closeIdx + 1 : to;
                    continue;
                }

                i++;
            }
        }

        /// <summary>Find the matching close tag index for an open tag at position idx.</summary>
        static int FindMatchingClose(List<Token> tokens, int idx, int to)
        {
            string tagName = tokens[idx].TagName;
            int depth = 1;

            for (int j = idx + 1; j < to; j++)
            {
                if (tokens[j].Type == TokenType.OpenTag && tokens[j].TagName == tagName)
                    depth++;
                else if (tokens[j].Type == TokenType.CloseTag && tokens[j].TagName == tagName)
                {
                    depth--;
                    if (depth == 0) return j;
                }
            }

            return to; // no matching close found — treat rest as inner content
        }

        // ════════════════════════════════════════
        //  SELF-CLOSING TAG HANDLING
        // ════════════════════════════════════════

        static void HandleSelfClosingTag(Token t, StringBuilder sb, ConvertContext ctx)
        {
            switch (t.TagName)
            {
                case "br":
                    if (ctx.InPre)
                        sb.Append('\n');
                    else
                        sb.Append('\n');
                    break;

                case "hr":
                    EnsureBlankLine(sb);
                    sb.Append("---\n");
                    break;

                case "img":
                    string alt = GetAttribute(t.RawTag, "alt") ?? "";
                    string src = GetAttribute(t.RawTag, "src") ?? "";
                    sb.Append("![");
                    sb.Append(EscapeMarkdownText(alt));
                    sb.Append("](");
                    sb.Append(src);
                    sb.Append(')');
                    break;

                default:
                    // unsupported self-closing tag — ignored
                    break;
            }
        }

        // ════════════════════════════════════════
        //  OPEN/CLOSE BLOCK HANDLING
        // ════════════════════════════════════════

        static void HandleOpenCloseBlock(List<Token> tokens, int openIdx, int innerFrom, int innerTo,
            StringBuilder sb, ConvertContext ctx)
        {
            Token open = tokens[openIdx];
            string tag = open.TagName;

            // ── headings ──
            if (tag == "h1" || tag == "h2" || tag == "h3" ||
                tag == "h4" || tag == "h5" || tag == "h6")
            {
                int level = tag[1] - '0';
                EnsureBlankLine(sb);
                sb.Append('#', level);
                sb.Append(' ');

                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                sb.Append(inner.ToString().Trim());
                sb.Append('\n');
                return;
            }

            // ── paragraph ──
            if (tag == "p")
            {
                EnsureBlankLine(sb);
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                string text = inner.ToString().Trim();
                if (text.Length > 0)
                {
                    sb.Append(text);
                    sb.Append('\n');
                }
                return;
            }

            // ── blockquote ──
            if (tag == "blockquote")
            {
                EnsureBlankLine(sb);
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                string content = inner.ToString().Trim();

                // prefix each line with >
                int pos = 0;
                while (pos < content.Length)
                {
                    int eol = content.IndexOf('\n', pos);
                    if (eol < 0) eol = content.Length;

                    sb.Append("> ");
                    sb.Append(content, pos, eol - pos);
                    sb.Append('\n');

                    pos = eol + 1;
                }
                return;
            }

            // ── pre (code block) ──
            if (tag == "pre")
            {
                EnsureBlankLine(sb);

                // detect language from inner <code class="language-xxx">
                string lang = "";
                if (innerFrom < innerTo && tokens[innerFrom].Type == TokenType.OpenTag &&
                    tokens[innerFrom].TagName == "code")
                {
                    string cls = GetAttribute(tokens[innerFrom].RawTag, "class");
                    if (cls != null && cls.Length > 9 && StartsWithIgnoreCase(cls, "language-"))
                    {
                        lang = cls.Substring(9);
                    }
                }

                // extract raw text content inside <pre> (and possible <code>)
                bool oldInPre = ctx.InPre;
                ctx.InPre = true;
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                ctx.InPre = oldInPre;

                string code = inner.ToString();
                // trim one leading and one trailing newline if present
                if (code.Length > 0 && code[0] == '\n') code = code.Substring(1);
                if (code.Length > 0 && code[code.Length - 1] == '\n') code = code.Substring(0, code.Length - 1);

                sb.Append("```");
                sb.Append(lang);
                sb.Append('\n');
                sb.Append(code);
                sb.Append('\n');
                sb.Append("```\n");
                return;
            }

            // ── inline code (not inside pre) ──
            if (tag == "code" && !ctx.InPre)
            {
                var inner = new StringBuilder();
                bool oldInCode = ctx.InCode;
                ctx.InCode = true;
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                ctx.InCode = oldInCode;

                string code = inner.ToString();
                // choose backtick delimiter that doesn't conflict
                if (code.IndexOf('`') >= 0)
                {
                    sb.Append("`` ");
                    sb.Append(code);
                    sb.Append(" ``");
                }
                else
                {
                    sb.Append('`');
                    sb.Append(code);
                    sb.Append('`');
                }
                return;
            }

            // ── code inside pre — just emit inner content, skip the <code> wrapper ──
            if (tag == "code" && ctx.InPre)
            {
                ConvertTokens(tokens, innerFrom, innerTo, sb, ctx);
                return;
            }

            // ── bold ──
            if (tag == "strong" || tag == "b")
            {
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                string text = inner.ToString();
                if (text.Length > 0)
                {
                    sb.Append("**");
                    sb.Append(text);
                    sb.Append("**");
                }
                return;
            }

            // ── italic ──
            if (tag == "em" || tag == "i")
            {
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                string text = inner.ToString();
                if (text.Length > 0)
                {
                    sb.Append('*');
                    sb.Append(text);
                    sb.Append('*');
                }
                return;
            }

            // ── strikethrough ──
            if (tag == "del" || tag == "s" || tag == "strike")
            {
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                string text = inner.ToString();
                if (text.Length > 0)
                {
                    sb.Append("~~");
                    sb.Append(text);
                    sb.Append("~~");
                }
                return;
            }

            // ── link ──
            if (tag == "a")
            {
                string href = GetAttribute(open.RawTag, "href") ?? "";
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                string text = inner.ToString().Trim();

                sb.Append('[');
                sb.Append(text.Length > 0 ? text : href);
                sb.Append("](");
                sb.Append(href);
                sb.Append(')');
                return;
            }

            // ── unordered list ──
            if (tag == "ul")
            {
                EnsureBlankLine(sb);
                int oldDepth = ctx.ListDepth;
                ctx.ListDepth++;
                ConvertListItems(tokens, innerFrom, innerTo, sb, ctx, ordered: false);
                ctx.ListDepth = oldDepth;
                return;
            }

            // ── ordered list ──
            if (tag == "ol")
            {
                EnsureBlankLine(sb);
                int oldDepth = ctx.ListDepth;
                ctx.ListDepth++;
                ConvertListItems(tokens, innerFrom, innerTo, sb, ctx, ordered: true);
                ctx.ListDepth = oldDepth;
                return;
            }

            // ── list item (handled here for orphan <li> outside list context) ──
            if (tag == "li")
            {
                var inner = new StringBuilder();
                ConvertTokens(tokens, innerFrom, innerTo, inner, ctx);
                sb.Append(inner.ToString().Trim());
                sb.Append('\n');
                return;
            }

            // ── table ──
            if (tag == "table")
            {
                EnsureBlankLine(sb);
                ConvertTable(tokens, innerFrom, innerTo, sb, ctx);
                return;
            }

            // ── table structural wrappers (thead, tbody, tfoot) — just recurse ──
            if (tag == "thead" || tag == "tbody" || tag == "tfoot")
            {
                ConvertTokens(tokens, innerFrom, innerTo, sb, ctx);
                return;
            }

            // ── structural/passthrough tags (div, span, section, etc.) ──
            // These are "supported" in that we understand them,
            // but they produce no markdown wrapper — just recurse into children.
            if (tag == "div" || tag == "span" || tag == "section" || tag == "article" ||
                tag == "main" || tag == "header" || tag == "footer" || tag == "nav" ||
                tag == "aside" || tag == "figure" || tag == "figcaption" ||
                tag == "details" || tag == "summary")
            {
                // block-level divs get a newline to separate content
                if (tag == "div" || tag == "section" || tag == "article" ||
                    tag == "main" || tag == "header" || tag == "footer" ||
                    tag == "nav" || tag == "aside" || tag == "figure")
                {
                    EnsureNewline(sb);
                }

                ConvertTokens(tokens, innerFrom, innerTo, sb, ctx);

                if (tag == "div" || tag == "section" || tag == "article" ||
                    tag == "main" || tag == "header" || tag == "footer" ||
                    tag == "nav" || tag == "aside" || tag == "figure")
                {
                    EnsureNewline(sb);
                }
                return;
            }

            // ── unsupported tag — strip tag, keep inner content ──
            ConvertTokens(tokens, innerFrom, innerTo, sb, ctx);
        }

        // ════════════════════════════════════════
        //  LIST CONVERSION
        // ════════════════════════════════════════

        static void ConvertListItems(List<Token> tokens, int from, int to,
            StringBuilder sb, ConvertContext ctx, bool ordered)
        {
            int itemNumber = 1;
            int i = from;

            while (i < to)
            {
                Token t = tokens[i];

                // skip whitespace text between <li> elements
                if (t.Type == TokenType.Text)
                {
                    i++;
                    continue;
                }

                if (t.Type == TokenType.Comment)
                {
                    i++;
                    continue;
                }

                if (t.Type == TokenType.OpenTag && t.TagName == "li")
                {
                    int closeIdx = FindMatchingClose(tokens, i, to);
                    int innerFrom = i + 1;
                    int innerTo = closeIdx < to ? closeIdx : to;

                    // compute indent
                    string indent = new string(' ', (ctx.ListDepth - 1) * 2);

                    // build inner content, separating nested lists
                    var itemText = new StringBuilder();
                    var nestedLists = new StringBuilder();

                    SeparateListContent(tokens, innerFrom, innerTo, itemText, nestedLists, ctx);

                    string text = itemText.ToString().Trim();

                    // emit the list marker
                    sb.Append(indent);
                    if (ordered)
                    {
                        sb.Append(itemNumber);
                        sb.Append(". ");
                    }
                    else
                    {
                        sb.Append("- ");
                    }
                    sb.Append(text);
                    sb.Append('\n');

                    // emit nested lists (they handle their own indentation)
                    if (nestedLists.Length > 0)
                    {
                        sb.Append(nestedLists);
                    }

                    itemNumber++;
                    i = (closeIdx < to) ? closeIdx + 1 : to;
                    continue;
                }

                // skip any other tokens between items
                i++;
            }
        }

        /// <summary>
        /// Separate list item content into inline text and nested sub-lists.
        /// </summary>
        static void SeparateListContent(List<Token> tokens, int from, int to,
            StringBuilder textSb, StringBuilder listSb, ConvertContext ctx)
        {
            int i = from;

            while (i < to)
            {
                Token t = tokens[i];

                if (t.Type == TokenType.OpenTag && (t.TagName == "ul" || t.TagName == "ol"))
                {
                    // this is a nested list — route to listSb
                    int closeIdx = FindMatchingClose(tokens, i, to);
                    int innerFrom = i + 1;
                    int innerTo = closeIdx < to ? closeIdx : to;

                    int oldDepth = ctx.ListDepth;
                    ctx.ListDepth++;
                    ConvertListItems(tokens, innerFrom, innerTo, listSb, ctx, ordered: t.TagName == "ol");
                    ctx.ListDepth = oldDepth;

                    i = (closeIdx < to) ? closeIdx + 1 : to;
                    continue;
                }

                // everything else goes to the text content
                if (t.Type == TokenType.OpenTag)
                {
                    int closeIdx = FindMatchingClose(tokens, i, to);
                    int innerFrom2 = i + 1;
                    int innerTo2 = closeIdx < to ? closeIdx : to;
                    HandleOpenCloseBlock(tokens, i, innerFrom2, innerTo2, textSb, ctx);
                    i = (closeIdx < to) ? closeIdx + 1 : to;
                    continue;
                }

                if (t.Type == TokenType.SelfClosingTag)
                {
                    HandleSelfClosingTag(t, textSb, ctx);
                    i++;
                    continue;
                }

                if (t.Type == TokenType.Text)
                {
                    string collapsed = CollapseWhitespace(t.RawText);
                    string decoded = DecodeHtmlEntities(collapsed);
                    textSb.Append(decoded);
                    i++;
                    continue;
                }

                i++;
            }
        }

        // ════════════════════════════════════════
        //  TABLE CONVERSION
        // ════════════════════════════════════════

        static void ConvertTable(List<Token> tokens, int from, int to,
            StringBuilder sb, ConvertContext ctx)
        {
            // collect all rows: each row is a list of cell strings + a flag for th vs td
            var rows = new List<TableRow>();
            CollectTableRows(tokens, from, to, rows, ctx);

            if (rows.Count == 0) return;

            // determine column count
            int colCount = 0;
            foreach (var row in rows)
            {
                if (row.Cells.Count > colCount) colCount = row.Cells.Count;
            }

            if (colCount == 0) return;

            // first row is always treated as header
            TableRow headerRow = rows[0];

            // emit header
            sb.Append('|');
            for (int c = 0; c < colCount; c++)
            {
                sb.Append(' ');
                sb.Append(c < headerRow.Cells.Count ? EscapeTableCell(headerRow.Cells[c]) : "");
                sb.Append(" |");
            }
            sb.Append('\n');

            // emit separator
            sb.Append('|');
            for (int c = 0; c < colCount; c++)
            {
                sb.Append(" --- |");
            }
            sb.Append('\n');

            // emit body rows
            for (int r = 1; r < rows.Count; r++)
            {
                sb.Append('|');
                for (int c = 0; c < colCount; c++)
                {
                    sb.Append(' ');
                    sb.Append(c < rows[r].Cells.Count ? EscapeTableCell(rows[r].Cells[c]) : "");
                    sb.Append(" |");
                }
                sb.Append('\n');
            }
        }

        class TableRow
        {
            public List<string> Cells = new List<string>();
            public bool IsHeader;
        }

        static void CollectTableRows(List<Token> tokens, int from, int to,
            List<TableRow> rows, ConvertContext ctx)
        {
            int i = from;

            while (i < to)
            {
                Token t = tokens[i];

                if (t.Type == TokenType.OpenTag)
                {
                    if (t.TagName == "thead" || t.TagName == "tbody" || t.TagName == "tfoot")
                    {
                        int closeIdx = FindMatchingClose(tokens, i, to);
                        CollectTableRows(tokens, i + 1, closeIdx < to ? closeIdx : to, rows, ctx);
                        i = (closeIdx < to) ? closeIdx + 1 : to;
                        continue;
                    }

                    if (t.TagName == "tr")
                    {
                        int closeIdx = FindMatchingClose(tokens, i, to);
                        int innerFrom = i + 1;
                        int innerTo = closeIdx < to ? closeIdx : to;

                        var row = new TableRow();
                        CollectTableCells(tokens, innerFrom, innerTo, row, ctx);
                        rows.Add(row);

                        i = (closeIdx < to) ? closeIdx + 1 : to;
                        continue;
                    }
                }

                i++;
            }
        }

        static void CollectTableCells(List<Token> tokens, int from, int to,
            TableRow row, ConvertContext ctx)
        {
            int i = from;

            while (i < to)
            {
                Token t = tokens[i];

                if (t.Type == TokenType.OpenTag && (t.TagName == "td" || t.TagName == "th"))
                {
                    if (t.TagName == "th") row.IsHeader = true;

                    int closeIdx = FindMatchingClose(tokens, i, to);
                    int innerFrom = i + 1;
                    int innerTo = closeIdx < to ? closeIdx : to;

                    var cellSb = new StringBuilder();
                    ConvertTokens(tokens, innerFrom, innerTo, cellSb, ctx);
                    row.Cells.Add(cellSb.ToString().Trim());

                    i = (closeIdx < to) ? closeIdx + 1 : to;
                    continue;
                }

                i++;
            }
        }

        static string EscapeTableCell(string text)
        {
            // escape pipes in cell content
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '|')
                    sb.Append("\\|");
                else
                    sb.Append(text[i]);
            }
            return sb.ToString();
        }

        // ════════════════════════════════════════
        //  TEXT HELPERS
        // ════════════════════════════════════════

        /// <summary>Collapse runs of whitespace (space, tab, newline) into a single space.</summary>
        static string CollapseWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);
            bool lastWasSpace = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            return sb.ToString();
        }

        /// <summary>Escape characters that have meaning in markdown.</summary>
        static string EscapeMarkdownText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '[' || c == ']' || c == '(' || c == ')' || c == '!')
                {
                    sb.Append('\\');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Ensure the StringBuilder ends with at least one blank line (\n\n).</summary>
        static void EnsureBlankLine(StringBuilder sb)
        {
            if (sb.Length == 0) return;

            // count trailing newlines
            int trailing = 0;
            int pos = sb.Length - 1;
            while (pos >= 0 && (sb[pos] == '\n' || sb[pos] == '\r'))
            {
                if (sb[pos] == '\n') trailing++;
                pos--;
            }

            while (trailing < 2)
            {
                sb.Append('\n');
                trailing++;
            }
        }

        /// <summary>Ensure the StringBuilder ends with at least one newline.</summary>
        static void EnsureNewline(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            if (sb[sb.Length - 1] != '\n')
            {
                sb.Append('\n');
            }
        }

        /// <summary>
        /// Two-step cleanup:
        /// 1. Strip \r and remove whitespace-only lines (keep as blank lines).
        /// 2. Collapse runs of 3+ newlines into exactly 2.
        /// </summary>
        static string CollapseBlankLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            // ── Pass 1: normalize \r, strip whitespace-only lines ──
            var pass1 = new StringBuilder(s.Length);
            int lineStart = 0;
            bool lineHasNonSpace = false;

            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\r') continue;

                if (s[i] == '\n')
                {
                    // if the line so far was whitespace-only, discard that whitespace
                    if (!lineHasNonSpace && pass1.Length > lineStart)
                    {
                        pass1.Length = lineStart;
                    }
                    pass1.Append('\n');
                    lineStart = pass1.Length;
                    lineHasNonSpace = false;
                }
                else
                {
                    if (s[i] != ' ' && s[i] != '\t')
                    {
                        lineHasNonSpace = true;
                    }
                    pass1.Append(s[i]);
                }
            }

            // trailing whitespace-only content
            if (!lineHasNonSpace && pass1.Length > lineStart)
            {
                pass1.Length = lineStart;
            }

            // ── Pass 2: collapse 3+ consecutive newlines into exactly 2 ──
            var sb = new StringBuilder(pass1.Length);
            int consecutiveNewlines = 0;

            for (int i = 0; i < pass1.Length; i++)
            {
                if (pass1[i] == '\n')
                {
                    consecutiveNewlines++;
                    if (consecutiveNewlines <= 2)
                    {
                        sb.Append('\n');
                    }
                }
                else
                {
                    consecutiveNewlines = 0;
                    sb.Append(pass1[i]);
                }
            }

            return sb.ToString();
        }

        static bool StartsWithIgnoreCase(string s, string prefix)
        {
            if (s.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (char.ToLowerInvariant(s[i]) != char.ToLowerInvariant(prefix[i])) return false;
            }
            return true;
        }
    }
}

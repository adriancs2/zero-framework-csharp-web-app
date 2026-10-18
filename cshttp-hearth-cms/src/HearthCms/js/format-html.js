function formatHtml(html) {
    const htmlIndentSize = 2; // default indent size (spaces)

    const blockTags = new Set([
        'address', 'article', 'aside', 'blockquote', 'details', 'dialog',
        'dd', 'div', 'dl', 'dt', 'fieldset', 'figcaption', 'figure', 'footer',
        'form', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'header', 'hgroup', 'hr',
        'li', 'main', 'nav', 'ol', 'p', 'pre', 'section',
        'table', 'thead', 'tbody', 'tfoot', 'tr', 'td', 'th',
        'ul', 'br'
    ]);

    const voidTags = new Set([
        'br', 'hr', 'img', 'input', 'meta', 'link', 'area', 'base',
        'col', 'embed', 'source', 'track', 'wbr'
    ]);

    // Tags whose contents are whitespace-significant and must never be
    // re-tokenized, re-indented, or have their text nodes trimmed. The
    // entire subtree (open tag through matching close tag) is captured
    // and emitted byte-for-byte.
    const verbatimTags = new Set(['pre', 'textarea', 'script', 'style']);

    let depth = 0;
    const lines = [];        // array of completed lines
    let currentLine = null;  // line currently being built (for inline appends)

    const tokens = html.match(/<!--[\s\S]*?-->|<[^>]+>|[^<]+/g) || [];
    const indent = () => ' '.repeat(depth * htmlIndentSize);

    const startLine = (content) => {
        currentLine = content;
    };
    const appendToLine = (content) => {
        currentLine += content;
    };
    const flushLine = () => {
        if (currentLine !== null) {
            lines.push(currentLine);
            currentLine = null;
        }
    };

    for (let i = 0; i < tokens.length; i++) {
        const rawToken = tokens[i];
        let token = rawToken.trim();
        if (!token) continue;

        if (token.startsWith('<')) {
            const tagMatch = token.match(/^<\/?([a-zA-Z0-9]+)/);
            const tagName = tagMatch ? tagMatch[1].toLowerCase() : null;
            const isClosing = token.startsWith('</');
            const isSelfClosing = token.endsWith('/>') || voidTags.has(tagName);
            const isComment = token.startsWith('<!--');
            const isBlock = tagName && blockTags.has(tagName);

            if (isComment) {
                flushLine();
                lines.push(indent() + token);
                continue;
            }

            // ---- Verbatim subtree (pre/textarea/script/style) ----
            // Capture everything from this opening tag through its matching
            // closing tag untouched (no trim, no re-tokenizing, no reformatting
            // of whitespace), since that whitespace is semantically meaningful
            // (e.g. code indentation) rather than decorative.
            if (!isClosing && !isSelfClosing && tagName && verbatimTags.has(tagName)) {
                flushLine();
                let verbatim = indent() + token; // opening tag, indented like any block
                const closeRe = new RegExp('^</' + tagName + '\\s*>$', 'i');
                i++;
                while (i < tokens.length && !closeRe.test(tokens[i].trim())) {
                    verbatim += tokens[i]; // raw, untrimmed
                    i++;
                }
                if (i < tokens.length) verbatim += tokens[i].trim(); // closing tag
                lines.push(verbatim);
                if (isBlock) { /* depth unchanged: open+close handled as one unit */ }
                continue;
            }

            if (isClosing) {
                if (isBlock) {
                    flushLine();
                    depth = Math.max(0, depth - 1);
                    lines.push(indent() + token);
                } else {
                    // inline closing tag — attach to current line
                    if (currentLine === null) startLine(indent() + token);
                    else appendToLine(token);
                }
            } else {
                if (isBlock) {
                    flushLine();
                    lines.push(indent() + token);
                    if (!isSelfClosing) depth++;
                } else {
                    // inline opening tag
                    if (currentLine === null) startLine(indent() + token);
                    else appendToLine(token);
                }
            }
        } else {
            // text node
            if (currentLine === null) startLine(indent() + token);
            else appendToLine(token);
        }
    }

    flushLine();
    return lines.join('\n').trim();
}
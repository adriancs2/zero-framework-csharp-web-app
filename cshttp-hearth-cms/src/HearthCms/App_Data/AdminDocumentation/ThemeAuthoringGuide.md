# Theme authoring guide

> **Guides:** [HTML Template Guide](/admin/themes/docs) · [C# Template Guide](/admin/themes/docs-csharp)

## Two template engines

Hearth CMS ships with **two template engines**, and you choose whichever fits the site you are building.

### HTML-based templates

- **Effortless to edit.** A theme is just plain HTML files with `{{token}}` placeholders — no compilation and no build step.
- **Fixed data shape.** The data each template receives is predefined — well suited to a straightforward content site.
- **Directly editable home page.** The landing page can be edited in place; the only real limit is your own imagination and creativity.
- **The classic CMS.** A quick, familiar way to present articles — content management in its most recognisable form.

### C#-based templates

- **Full flexibility.** Render any layout you can express in code — there is no fixed token vocabulary to work around.
- **Unlimited UI potential.** Build complex, interactive components, call third-party APIs, and compose pages however you like.
- **More than a CMS.** At full power this is essentially application development, not just publishing.
- **Custom data access.** Run your own SQLite queries for bespoke, purpose-built features.
- **Custom endpoints.** Add your own API endpoints for custom actions — automated article import and posting, integrations, scheduled jobs, and more.
- **Application scaffolding.** Hearth's foundation is a fast on-ramp for building a small application from the ground up on SQLite — and not just any ASP.NET app, but one that follows the new **Pageless ASP.NET Web Forms Architecture**.

To build a C#-based template, read the [C# Template Guide](/admin/themes/docs-csharp).

## While editing an HTML template

- **Turn the RAM cache off while editing** so your live changes appear immediately, then turn it back on when you are done (Settings → Cache).
- **Apply a cache-buster** to your own CSS / JavaScript (bump the `?v=` or rename the file) so browsers fetch the new version rather than a stale copy — details in the linking section below.

## How theming works in Hearth

A **theme** is a small set of **HTML template files** plus whatever **CSS, JavaScript, fonts and images** you want to ship with it. The public site is rendered by dropping live data into `{{token}}` placeholders inside those templates — there is **no logic** in the HTML: no loops, no `if` statements, no expressions. The engine decides what each token contains; your job is purely *structure* and *style*.

The model is deliberately small. There is **one skeleton** (the body shell), **six page templates** the system knows how to fill, and a handful of **components** — the small repeating pieces (a post card, a list row) that the engine stamps out once per item and joins together.

- **No logic in templates.** Only `{{token}}` placeholders. An unknown or misspelled token simply renders as empty text — it never errors.
- **No escaping to worry about.** The engine escapes every value before it reaches the template. Plain-text tokens are HTML-encoded, URL tokens are attribute-encoded, and pre-built HTML blocks (nav, lists, rendered article bodies) are injected verbatim. You never call an encoder in a template.
- **Single-pass substitution.** Tokens are replaced in one scan, so HTML injected through a token is never re-scanned for more tokens. No accidental double-substitution.
- **Per-file fallback.** If your theme is missing a template file, the engine falls back to the built-in default theme's copy of that same file. A half-finished theme never breaks the site — pages always render.
- **You own your assets.** CSS, JS, fonts, background and decoration images live in your theme's public asset folder, and **you link them yourself** in `_layout.html`. The engine does not generate the stylesheet link and does not manage cache-busting (see below).

**hearth** is the name of this CMS, and also the slug of the first default theme. Throughout this guide, wherever you see `{slug}`, substitute your own theme's slug (e.g. `hearth`).

> **🛡️ Strongly recommended: duplicate an existing theme before modifying anything.** Do not edit the built-in `hearth` theme (or any shipped theme) in place. Copy *both* of its folders to a new slug of your own — the templates *and* the assets:
>
> ```
> /App_Data/themes/{theme_name}/    →  /App_Data/themes/{my_theme}/
> /assets/themes/{theme_name}/      →  /assets/themes/{my_theme}/
> ```
>
> The reason is **update safety**: when you update the CMS to a newer version, the updater overwrites the original shipped theme files — any edits you made directly inside them are lost. Your duplicated folders use your own slug, so an update never touches them and your modified templates and assets survive every upgrade.
>
> **🔄 And every time you edit a CSS/JS/image asset, apply a cache-buster** (bump the `?v=` on that asset's link, or rename the file) so your readers' browsers actually fetch the new version instead of serving the old one from cache. Details in the linking section below.

## Where the files live

**Templates** live on disk under the server-only data folder. This folder is *not* web-accessible, so your raw `{{token}}` markup is never exposed over HTTP. All first-level templates sit in the theme root; the small repeating pieces go in a `components/` sub-folder:

```
/App_Data/themes/{slug}/
    _layout.html                    ← the skeleton (the body shell)
    home.html                       ← landing option: home
    latest-post.html                ← landing option: flat list of latest posts
    categories-latest-post.html     ← landing option: per-category sections
    category.html                   ← a single category page
    article-full-width.html         ← page/post layout: full width
    article-sidebar.html            ← page/post layout: with sidebar aside
    components/
        section-latest-posts.html   ← the home "latest writing" wrapper
        post-card.html              ← one card in the home grid
        row-post.html               ← one row in a flat list
        pagination-block.html       ← the pagination control wrapper (prev / pages / next)
        pagination-block-item.html  ← one page-number link inside the control
        category-section.html       ← one category block (feature + 2 columns)
        cat-mini-item.html          ← one compact item inside a category column
        footer-column.html          ← one footer column
```

**Assets** (CSS, JS, fonts, images) live in the web-accessible asset folder for your theme. You decide the filenames and the structure here — the engine does not look inside this folder:

```
/assets/themes/{slug}/
    site.css        (or whatever you name it; you link it yourself)
    theme.js        (optional)
    fonts/ ...
    img/ ...        (backgrounds, decoration, etc.)
```

The reference theme to copy from is `/App_Data/themes/hearth/` with its assets at `/assets/themes/hearth/`.

## Linking CSS & JS — you own it, including cache-busting

The engine **does not** inject a stylesheet link and **does not** generate a version query string. There is no `{{theme_href}}` token. Instead you write your own `<link>` and `<script>` tags directly in `_layout.html`, pointing at files in your asset folder:

```html
<link rel='stylesheet' href='/assets/themes/hearth/site.css' />
<script src='/assets/themes/hearth/theme.js' defer></script>
```

Because you own these tags, **cache-busting is your responsibility**. When you ship a change and want browsers to re-fetch a file, bump a version query string by hand on whichever asset changed — CSS, JS, an image, a font, anything:

```html
<link rel='stylesheet' href='/assets/themes/hearth/site.css?v=4' />
<script src='/assets/themes/hearth/theme.js?v=2' defer></script>
<!-- in CSS or markup -->  background-image: url('/assets/themes/hearth/img/grain.png?v=2');
```

This is intentional: a CMS-generated version number can only track one thing and tends to over- or under-invalidate. A theme author knows exactly which file changed and can bump precisely that one. Keep the numbers small and human — they only need to change when *you* publish a new asset.

Tip: if you'd rather not touch query strings, you can also rename the file (`site-v4.css`) and update the link — same effect, fully under your control.

## The shape of a rendered page

Every public URL produces a page in two nested stages:

1. **The skeleton** — `_layout.html` — is rendered first. It is the `<html>` / `<head>` / header / footer shell, with one big `{{body}}` hole in the middle.
2. **A page template** — one of the six below — is rendered and dropped into that `{{body}}`.
3. Some tokens inside a page template are themselves **containers**. The engine fills them by stamping a **component** once per item and joining the results. For example `{{row_list}}` in `latest-post.html` is filled with many `components/row-post.html` renders, one per post.

```
_layout.html  ({{body}})
   → home.html
        → {{latest_posts_block}}  → components/section-latest-posts.html
             → {{post_card_list}}   → components/post-card.html  (× N posts)
```

## Choosing the landing page

Under **Settings**, an admin chooses what the site root (`/` and `/home`) renders. The choice maps to one of these templates:

| Setting | Renders |
| --- | --- |
| Home | `home.html` — the hero + "latest writing" landing |
| Latest posts | `latest-post.html` — a flat list of the newest posts |
| Categories | `categories-latest-post.html` — one section per category |
| A specific Page | any published *page* you pick, rendered through the article layout |

Whichever you pick, the standalone routes still exist too (`/latest-post`, `/categories-latest-post`); the setting just *also* serves the chosen one at the root.

## Pages and posts

Hearth has two kinds of dynamic content, and they work **identically**:

- **Page** — standalone content (About, Contact). Served at `/{slug}`. No category. No published-date line and no "keep reading" aside.
- **Post** — blog/news content. Served at `/{slug}`. Has a category, a published date, and a "keep reading" aside of related posts. On a slug clash a page takes priority over a post.

Both a page and a post are rendered by the **same two article templates**, and each can choose either layout:

| Layout | Template | Notes |
| --- | --- | --- |
| Full width (`stack`) | `article-full-width.html` | Content runs the full column; any aside sits below. |
| Sidebar (`split`) | `article-sidebar.html` | Content beside a sidebar that holds the aside. |

The layout is the real axis — *not* whether the content is a page or a post. A page defaults to full-width, a post to sidebar, but the editor (and a `?layout=stack|split` preview override) can pick either.

## _layout.html — the skeleton

The shell every page is wrapped in. You write the entire `<head>` here, including your own stylesheet and script links (see the linking section above). `{{body}}` is **required** and is where the page template lands.

| Token | Type | Contains / sample |
| --- | --- | --- |
| `{{head_meta}}` | raw | The page's `<title>` and meta-description, built by the engine. Place inside `<head>`. e.g. `<title>My Post - Hearth</title><meta name='description' content='...'>` |
| `{{site_name}}` | text | The site name. e.g. `Hearth` |
| `{{nav_items}}` | raw | The rendered top-nav links (admin-managed nav builder, up to 2 levels). |
| `{{footer_column_list}}` | raw | The multi-column footer area, or empty when no columns are configured. Built from `components/footer-column.html`. |
| `{{footer_text}}` | text | The footer line. e.g. `© 2026 Hearth. All rights reserved.` |
| `{{body}}` | raw | **Required.** The rendered page template. |

There is no `{{theme_href}}` and no `{{theme_js}}` token — link your own assets directly. Everything else you want in `<head>` (fonts, icon CSS, syntax-highlight CSS/JS) you also add yourself; see `hearth/_layout.html` for a complete working example.

## home.html

The home landing body. The "latest writing" section is optional — when there are no posts the engine passes an empty string, so you never get a heading with no cards.

| Token | Type | Contains / sample |
| --- | --- | --- |
| `{{site_name}}` | text | e.g. `Hearth` |
| `{{site_tagline}}` | text | e.g. `A clean place to write.` |
| `{{site_description}}` | text | e.g. `Welcome to our minimalist CMS.` |
| `{{latest_posts_block}}` | raw | The whole "latest writing" section, or empty when there are no posts. Built from `components/section-latest-posts.html`. |

## latest-post.html

A flat list of posts, with a search box. Also used to render search results (the `?q=` form posts back to the same route). During a search, `{{page_subheading}}` is empty and `{{search_meta}}` shows the result count.

The list is **paged**: the *Number of posts* count for this listing (Settings) becomes the page size, and `?page=N` selects the slice. Place `{{pagination}}` below `{{row_list}}` to show the page controls — it renders empty when everything fits on one page, so it never leaves a stray control on a short list.

| Token | Type | Contains / sample |
| --- | --- | --- |
| `{{page_heading}}` | text | e.g. `Latest posts` |
| `{{page_subheading}}` | raw | Sub-heading paragraph, or empty during search. e.g. `<p class='list-sub'>Fresh writing, newest first.</p>` |
| `{{search_bar}}` | raw | The GET search form. |
| `{{search_meta}}` | raw | "N result(s) for …" line, or empty when not searching. |
| `{{row_list}}` | raw | The list of rows, or an empty-state message. Built from `components/row-post.html`. |
| `{{pagination}}` | raw | The pagination control, or empty when there is only one page. Built from `components/pagination-block.html`. |

## category.html

A single category at `/category/{slug}` — the same flat-list shape as `latest-post.html`, plus a breadcrumb. Search here is scoped to the category.

| Token | Type | Contains / sample |
| --- | --- | --- |
| `{{breadcrumbs}}` | raw | Breadcrumb nav. e.g. Home / Categories / Essays |
| `{{page_heading}}` | text | The category name. e.g. `Essays` |
| `{{page_subheading}}` | raw | e.g. `<p class='list-sub'>Posts in this category.</p>` |
| `{{search_bar}}` | raw | The GET search form (scoped to this category). |
| `{{search_meta}}` | raw | Result count line, or empty. |
| `{{row_list}}` | raw | The list of rows. Built from `components/row-post.html`. |

## categories-latest-post.html

One section per category, newest items first. Each section is built from `components/category-section.html`.

| Token | Type | Contains / sample |
| --- | --- | --- |
| `{{page_heading}}` | text | e.g. `Browse by category` |
| `{{page_subheading}}` | text | e.g. `The latest from every category.` |
| `{{category_section_list}}` | raw | One block per category, or an empty-state. Built from `components/category-section.html`. |

## article-full-width.html & article-sidebar.html

Both article templates share the **same token set**. The sidebar layout places `{{article_aside}}` beside the content; the full-width layout places it after. Several tokens are *block-or-nothing* — they render a complete element when there is data, or empty when there isn't (so pages can omit dates and asides cleanly).

| Token | Type | Contains / sample |
| --- | --- | --- |
| `{{breadcrumbs}}` | raw | Breadcrumb nav. Post: Home / Essays / Title · Page: Home / Title |
| `{{article_title}}` | text | The article title. e.g. `Welcome to Hearth` |
| `{{published_date}}` | raw | Published-date line, or empty (pages have none). e.g. `<div class='doc-meta'>… January 1, 2026</div>` |
| `{{updated_date}}` | raw | "Updated …" line, or empty when no modified date. |
| `{{article_author}}` | raw | Author line, or empty when the author has no display name. |
| `{{cover_image}}` | raw | The complete cover-image element (the `components/cover-image.html` partial, a `.content-cover-image` wrapper around an `<img>`), or empty when there is no cover — so pages without one omit it cleanly. Edit `components/cover-image.html` to change the markup. e.g. `<div class='content-cover-image'><img src='/uploads/x.jpg' alt='' /></div>` |
| `{{article_content}}` | raw | **The rendered article body** (Markdown already converted to HTML, or HTML passed through). |
| `{{article_aside}}` | raw | The "keep reading" recent-posts block (posts only), or empty for pages and when there is nothing to show. |

In the full-width template you may place `{{article_aside}}` anywhere — or omit it entirely if your design doesn't want a related-posts block.

## Components — the repeating pieces

A component is the markup for a **single item**. The engine renders it once per record and joins the results, then injects the joined block into a container token on a page template. You only ever describe one item.

### components/section-latest-posts.html (home "latest writing" wrapper)

A wrapper, rendered once. Holds the grid container.

| Token | Type | Contains |
| --- | --- | --- |
| `{{post_card_list}}` | raw | The joined post cards (many `post-card.html` renders). |

### components/post-card.html (one card in the home grid)

Joined into `{{post_card_list}}` above.

| Token | Type | Sample |
| --- | --- | --- |
| `{{post_url}}` | url | `/welcome-to-hearth` |
| `{{post_title}}` | text | `Welcome to Hearth` |
| `{{post_excerpt}}` | text | `Your first post — edit or delete…` |
| `{{post_date}}` | text | `Jan 1, 2026` |

### components/row-post.html (one row in a flat list)

Joined into `{{row_list}}` on `latest-post.html` and `category.html`.

| Token | Type | Sample / note |
| --- | --- | --- |
| `{{post_url}}` | url | `/design-philosophy` |
| `{{post_title}}` | text | `Design Philosophy` |
| `{{post_excerpt}}` | text | `Less, but better.` |
| `{{post_date}}` | text | `Jan 1, 2026` |
| `{{post_category}}` | raw | A category tag element, or empty (category pages hide it). e.g. `<span class='row-cat'>… Essays</span>` |
| `{{post_thumb_empty}}` | raw | Class suffix: ` is-empty` when there is no cover image, else empty. Put it right after the thumb's class name. |
| `{{post_thumb_img}}` | raw | A ready-made `<img>` for the cover (sized to cover its wrapper via your CSS), or empty when there is no cover. e.g. `<img src='/uploads/x.jpg' alt='' />` |

The thumb pattern is `<div class='row-thumb{{post_thumb_empty}}'>{{post_thumb_img}}</div>` — a wrapper `<div>` you style, holding the engine's `<img>`. Give the wrapper a fixed size plus `overflow:hidden`, then set the inner `<img>` to `width:100%; height:100%; object-fit:cover` so the photo fills it. `{{post_thumb_empty}}` still appends `is-empty` when there is no cover, for styling the placeholder.

### components/pagination-block.html (the pagination control)

Rendered once and dropped into `{{pagination}}` on `latest-post.html`. It is the wrapper holding the prev/next links and the page-number list; the engine fills `{{pagination_items}}` with many `pagination-block-item.html` renders, one per page in the window. The whole block is only rendered when there is more than one page.

| Token | Type | Contains / note |
| --- | --- | --- |
| `{{prev_url}}` | url | Link to the previous page (clamped to page 1 on the first page). |
| `{{prev_disabled}}` | raw | Class suffix ` is-disabled` on the first page, else empty. Put it right after the prev link's class name; style it to block the click (e.g. `pointer-events:none`). |
| `{{next_url}}` | url | Link to the next page (clamped to the last page on the last page). |
| `{{next_disabled}}` | raw | Class suffix ` is-disabled` on the last page, else empty. |
| `{{pagination_items}}` | raw | The joined page-number links (many `pagination-block-item.html` renders). A `<span class='pagination-gap'>…</span>` ellipsis is inserted automatically wherever the page window skips a run. |
| `{{pagination_info}}` | text | A ready "Page 2 of 7" summary, if you want to show it. |

### components/pagination-block-item.html (one page-number link)

Joined into `{{pagination_items}}` above — one render per page number.

| Token | Type | Sample / note |
| --- | --- | --- |
| `{{page_url}}` | url | `/latest-post?page=2` (an active `?q=` search is preserved across pages). |
| `{{page_number}}` | text | `2` |
| `{{page_active}}` | raw | Class suffix ` is-active` on the current page, else empty. Put it right after the link's class name. |

The default markup is a `<nav class='pagination'>` wrapper with `.pagination-prev` / `.pagination-next` links around a `.pagination-pages` row of `.pagination-page` numbers. Keep these class names if you inherit the component but write your own CSS, or that markup will be unstyled.

### components/category-section.html (one category block)

Joined into `{{category_section_list}}` on `categories-latest-post.html`. The first (newest) post is the *feature*; the rest are split across two compact columns.

| Token | Type | Sample / note |
| --- | --- | --- |
| `{{category_title}}` | text | `Essays` |
| `{{category_url}}` | url | `/category/essays` |
| `{{feature_url}}` | url | `/welcome-to-hearth` |
| `{{feature_title}}` | text | `Welcome to Hearth` |
| `{{feature_excerpt}}` | text | `Your first post…` |
| `{{feature_date}}` | text | `Jan 1, 2026` |
| `{{feature_thumb_empty}}` | raw | Class suffix ` is-empty` for the feature image, or empty. |
| `{{feature_thumb_img}}` | raw | A ready-made `<img>` for the feature cover (cover-fit via your CSS), or empty. |
| `{{column_1_list}}` | raw | First compact column — joined `cat-mini-item.html` renders. |
| `{{column_2_list}}` | raw | Second compact column — joined `cat-mini-item.html` renders. |

### components/cat-mini-item.html (compact item in a category column)

Joined into `{{column_1_list}}` / `{{column_2_list}}` above.

| Token | Type | Sample / note |
| --- | --- | --- |
| `{{post_url}}` | url | `/design-philosophy` |
| `{{post_title}}` | text | `Design Philosophy` |
| `{{post_date}}` | text | `Jan 1, 2026` |
| `{{post_thumb_empty}}` | raw | Class suffix ` is-empty`, or empty. |
| `{{post_thumb_img}}` | raw | A ready-made `<img>` (cover-fit via your CSS), or empty. |

### components/footer-column.html (one footer column)

Rendered once per configured footer column and joined into `{{footer_column_list}}` on `_layout.html`.

| Token | Type | Contains |
| --- | --- | --- |
| `{{footer_content}}` | raw | That column's content (admin Markdown rendered to HTML). |

## Building a theme, step by step

1. **Duplicate a theme** in the Theme editor (start from `hearth`). This creates the theme record and gives you a `{slug}`.
2. **Create the folders** `/App_Data/themes/{slug}/` and `/App_Data/themes/{slug}/components/` on the server, and the asset folder `/assets/themes/{slug}/`.
3. **Copy only the templates you want to change** from `hearth`. Anything you omit falls back to the default automatically — you can override just `_layout.html` and `home.html` and inherit the rest.
4. **Add your assets** to `/assets/themes/{slug}/` and link them yourself in `_layout.html`.
5. **Activate** the theme in the Theme editor.

If you inherit any template from `hearth` but write your own CSS, keep the original class names (`doc-grid`, `row-post`, `post-card`, `cat-section`, `cat-feature`, `cat-mini`, …) or that inherited markup will be unstyled.

## Fixed CSS classes — the Markdown toggle

Most of a theme's CSS is entirely up to you: you write the templates, so you choose the class names. There is **one exception**. The "View Content / View Markdown" toggle on a post's content area is not part of any template file — it is markup the CMS rendering engine injects directly into `{{article_content}}` at render time (see `ArticleTools.BuildContentArea` if you're curious), because the same fixed structure has to work identically whether the post is rendered by an HTML theme or a C# theme. You cannot edit this markup from a theme, and its class/id selectors are **fixed and non-negotiable**:

```html
<div class='md-toolbar'>
    <button type='button' onclick='showContent();'>View Content</button>
    <button type='button' onclick='showMarkdown();'>View Markdown</button>
</div>
<div id='post_content'> … </div>
<div id='post_markdown' style='display:none'><textarea readonly></textarea></div>
```

Because this structure is fixed by the engine rather than by your template, your theme's CSS must specifically target it — matching the engine's contract instead of a class name you invented yourself:

| Selector | What it is |
| --- | --- |
| `.md-toolbar` | The row holding the two buttons. |
| `.md-toolbar button` | The *View Content* / *View Markdown* buttons themselves. |
| `#post_markdown textarea` | The raw-Markdown box, shown after clicking *View Markdown*. |

**The engine does not ship any styling for these anymore.** Every theme — HTML or C# — must define its own CSS for these three selectors in its own stylesheet, or the buttons and textarea render with no styling at all (unstyled browser defaults) whenever *Show Markdown button on posts* is on in Settings. A baseline you can start from, and copy verbatim if you don't want to design it yourself:

```css
.md-toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin: 0 0 16px; }
.md-toolbar button {
    font: inherit; font-size: .85em; padding: 6px 14px;
    color: inherit; background: transparent;
    border: 1px solid currentColor; border-radius: 6px;
    cursor: pointer; opacity: .7; transition: opacity .12s;
}
.md-toolbar button:hover { opacity: 1; }
#post_markdown textarea {
    width: 100%; min-height: 60vh; box-sizing: border-box;
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    font-size: .9em; line-height: 1.6;
    color: inherit; background: transparent;
    border: 1px solid currentColor; border-radius: 8px;
    padding: 16px 18px; resize: vertical; opacity: .92;
}
```

Every shipped theme (both the HTML-template themes and the C# `hearth`/`broadsheet` themes) already carries this block in its own CSS file, so it's a working example to check against — see `/assets/themes/{slug}/*.css`.

## Duplicating & deleting themes

A theme is just **two folders that share the same slug** — one for the templates, one for the assets. There is no database table and no registry: the slug *is* the folder name. That makes both duplicating and deleting a theme plain folder operations on the server.

```
/App_Data/themes/{slug}/      ← templates (server-only)
/assets/themes/{slug}/        ← CSS, JS, fonts, images (web-accessible)
```

### Duplicating a theme

**Duplicating is the strongly recommended way to start any modification.** Never edit a shipped theme (like `hearth`) in place: a future CMS update will overwrite the original shipped files and wipe your edits. A duplicate under your own slug — both `/App_Data/themes/{theme_name}/` *and* `/assets/themes/{theme_name}/` — is never touched by an update, so your modified templates and assets are kept across upgrades.

To create a new theme, make the two folders for your new slug, then either write fresh files or copy an existing theme's files into them and modify from there:

1. **Pick a slug** for the new theme — the folder name. Use lowercase letters, numbers and hyphens (e.g. `my-theme`).
2. **Create the two folders:**
   ```
   /App_Data/themes/{new_theme_name}/
   /assets/themes/{new_theme_name}/
   ```
3. **Add the templates** — one of two ways:
   - *Start from scratch:* write only the template files you need into `/App_Data/themes/{new_theme_name}/`. Anything you omit falls back to the default `hearth` theme automatically, so even a single `_layout.html` is a working theme.
   - *Copy an existing theme:* copy the contents of, say, `/App_Data/themes/hearth/` (including its `components/` sub-folder) into your new theme folder, then edit the files. Do the same for its assets — copy `/assets/themes/hearth/` into `/assets/themes/{new_theme_name}/`.
4. **Fix the asset links.** If you copied another theme, its `_layout.html` still points at the old asset folder (e.g. `/assets/themes/hearth/site.css`). Update those `<link>` / `<script>` paths to your new slug: `/assets/themes/{new_theme_name}/site.css`.
5. **Activate** the new theme from the Themes library when you're ready.

The new theme appears in the Themes library as soon as the `/App_Data/themes/{new_theme_name}/` folder exists — the library lists folders, so no extra registration step is needed.

### Deleting a theme

To delete a theme, simply remove its two folders:

```
/App_Data/themes/{target_theme}/      ← delete
/assets/themes/{target_theme}/        ← delete
```

Once both folders are gone the theme disappears from the Themes library. (The Themes library also offers a Delete button that does exactly this for non-active themes.)

- **Don't delete the active theme.** Activate a different theme first — otherwise the site falls back to the default `hearth` theme for every missing file.
- **Don't delete `hearth`.** It is the built-in fallback every other theme relies on for any template it doesn't override; removing it can leave partial themes unable to render.
- Deleting is permanent — there is no recycle bin. Keep a copy of the folders if you might want the theme back.

## Tips & gotchas

- **An unknown token renders empty.** A typo like `{{titel}}` silently produces nothing — check spelling against the tables above.
- **Thumbnails are an `<img>` in a wrapper you own.** The engine drops a bare `<img>` for each cover (or nothing) into the `{{post_thumb_img}}` / `{{feature_thumb_img}}` tokens. Wrap it in your own sized `<div>` with `overflow:hidden` and give the inner `<img>` `width:100%; height:100%; object-fit:cover` so the photo fills the box. No `loading` attribute is set, so thumbnails load immediately.
- **Type matters.** *text* tokens are HTML-encoded (safe inside elements), *url* tokens are attribute-encoded (safe inside `href`/`src`), and *raw* tokens are whole HTML blocks — place them where block-level HTML is valid, never inside a plain-text context.
- **Suffix tokens have no leading space.** `{{thumb_empty}}` sits flush after a class name (it appends `is-empty`); the cover itself arrives through the separate `{{thumb_img}}` token as a complete `<img>` element you place inside the wrapper.
- **Number of posts** shown on each list/section is set in *Settings*, not in templates.
- **Templates are server-only.** Files under `/App_Data/` can't be fetched over HTTP, so your raw `{{token}}` markup is never exposed.
- **Never modify a shipped theme in place — duplicate it first.** A CMS update overwrites the original theme files (both `/App_Data/themes/{theme_name}/` and `/assets/themes/{theme_name}/`); your duplicated copy under its own slug survives every update.
- **You own cache-busting — every edit needs one.** Whenever you change a CSS/JS/image asset, bump its `?v=` (or rename the file) so the new edit gets flushed to your readers' browsers; the engine won't do it for you.

## Editing the home page (per-theme content)

A theme can expose **editable regions on its home page**, edited visually from the theme editor's **Edit Home Content** button. It loads your live `home.html` in a frame and lets an admin edit the marked regions in place, with a floating *Save*.

You decide what is editable by adding a `data-edit` attribute (or a typed variant — `data-edit-href`, `data-edit-src`, `data-edit-bg`, `data-edit-icon`) to elements in `home.html`. The element's existing content is the **default**; an admin's overrides are saved per-theme to `/App_Data/themes/{slug}/home.values.json` and applied when the home page renders. Nothing is stored in the database, and the override travels with the theme folder.

### Editable text — available now

Put `data-edit='key'` on a **plain-text leaf element** (one whose content is just text). The admin edits it inline; the text is saved under `key`.

```html
<h3 data-edit='feat1_title'>Made to read</h3>
<p  data-edit='feat1_body'>Warm typography, generous margins…</p>
```

Leaf elements only. If a button holds an icon — e.g. `Read the blog <i class='fa-solid fa-arrow-right'></i>` — wrap just the words in a `<span data-edit='…'>` so the icon survives the swap.

### Links, images, backgrounds & icons — live now

Links, images and icons aren't *text* — they live in an **attribute** (`href`, `src`, an inline `style` background, the `fa-` class). Mark the element with a typed attribute and the editor opens the right tool when you click it. All of these **work today**; overrides save to `home.values.json` exactly like text.

| Marker | Edits | Click opens |
| --- | --- | --- |
| `data-edit='key'` | the element's inner text | inline editing (type in place) |
| `data-edit-href='key'` | its `href` | a URL popover |
| `data-edit-src='key'` | its `src` (an `<img>`) | the Media browser |
| `data-edit-bg='key'` | its `background-image` (inline style) | the Media browser |
| `data-edit-icon='key'` | its Font Awesome `class` | the icon picker |

```html
<a   data-edit-href='cta_url'   href='/latest-post'><span data-edit='cta_label'>Read the lastest posts</span></a>
<img data-edit-src='hero_image' src='/assets/themes/{slug}/img/hero.jpg' alt='' />
<i   data-edit-icon='hero_icon' class='fa-solid fa-fire'></i>
<div data-edit-bg='banner_bg' style='background-image:url("/uploads/banner.jpg")'>…</div>
```

### The two kinds of image

An image shows up in one of two ways, and **both** are editable:

- **`<img>` — `data-edit-src`**: a real image element. The picked URL is written to its `src`.
- **Background — `data-edit-bg`**: an element with an *inline* `style='background-image:url(…)'`. The engine swaps only the `background-image` and **keeps your other inline styles** (size, position, radius…).

A background set in a **CSS file** (e.g. a theme's parallax-hero rule) is *not* reachable — only an **inline** `background-image` on an element in `home.html` is. To make a CSS background editable, move it onto the element as an inline style and add `data-edit-bg`.

### How editing feels

- **Click a region, get the right tool** — text edits inline; a link opens a URL popover; an image or background opens the Media browser; an icon opens the icon picker (a searchable grid, or paste any `fa-` class).
- **Navigation is locked while editing** — clicking links and buttons edits them instead of navigating away, so you never lose your place.
- **Regions are outlined by type** while the editor is open, and the floating *Save* writes them all at once.
- **Output is escaped** — text is HTML-encoded, URLs and classes attribute-encoded — so saved content can't break your markup.

## Media, icons & favicon

### Media browser — swapping images

Image fields use the built-in **MediaBrowser** component (`/js/media-browser.js`) — the same picker used across the admin. It returns a URL you store as the field value; the engine writes that into the element's `src`. You don't build a picker; you call this one:

```js
// pick a single image
const url  = await mediaBrowser.pick({ accept: ['image/*'] });
// pick several
const urls = await mediaBrowser.pick({ multiSelect: true, accept: ['image/*'] });
// viewer only, or mount inline
await mediaBrowser.open();
const handle = mediaBrowser.mount('#div_media_container'); handle.unmount();
```

It talks to the existing media API — the same endpoints the Media page uses:

```
GET  /api/admin/media?action=list
     -> { success:true, html: "<div class='media-tile' data-url='…'>…</div>…" }
POST /api/admin/media   action=upload, file=<File>   -> { success, … }
POST /api/admin/media   action=delete, id=<int>      -> { success, message? }
```

Each `.media-tile` exposes `data-url` (and optionally `data-name`, `data-id`). Because the picker already exists, image swapping **reuses** it rather than adding anything new.

### Font Awesome is loaded site-wide

The Font Awesome stylesheet is linked on **every page** — public site and admin. Use any `fa-solid fa-*` or `fa-regular fa-*` class directly in your templates; you never add the library yourself. An icon field simply swaps the `fa-` classes on an `<i>`.

### Favicon is a site-wide setting

The favicon is **not** a theme or home-page concern — it shows on every page across the whole site, the admin included. Set it in **Settings → Site identity → Favicon**, using the same Media browser picker (with a live preview). It is stored once globally and injected into every page's `<head>` automatically — the correct `type` is inferred from the file extension (`.png`, `.svg`, `.ico`, …) — and is shared by all themes, so it never belongs in `home.html` or the home editor.

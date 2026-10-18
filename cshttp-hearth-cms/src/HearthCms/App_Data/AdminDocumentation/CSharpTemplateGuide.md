# C# Template authoring guide

> **Guides:** [HTML Template Guide](/admin/themes/docs) · [C# Template Guide](/admin/themes/docs-csharp)

## What is a C# template?

A **C# theme** renders each page imperatively in code instead of filling `{{token}}` placeholders in HTML files. Where an HTML theme is constrained to a fixed data shape, a C# theme can do *anything C# can do* — arbitrary layout, loops and conditionals, custom queries, third-party API calls, even its own endpoints.

A C# theme is simply **a class that inherits `CsTemplate`**, compiled into the application. There is no flag or attribute to set — deriving from the base class is what marks the class as a theme. The registry scans the loaded assemblies, finds your class, and activates it when its `Slug` matches the active theme.

The shipped **Broadsheet (C#)** theme (`/engine/CsTemplate/Themes/Broadsheet/`) is a complete, working reference — copy it as your starting point.

## More than a theme — an application scaffold

A C# template is not limited to "skinning a blog." Because you have the full language, the database, and the request pipeline at your disposal, Hearth becomes a **fast scaffold for building a small application from the ground up** — SQLite already wired in, sessions and admin authentication handled, caching in place, and a content model ready to use.

And it is not just any ASP.NET application: it is a reference implementation of the **Pageless ASP.NET Web Forms Architecture** — no `.aspx` pages, no code-behind files, no per-page control lifecycle. Every request is routed in `Global.asax` to a plain handler that queries data and writes HTML, and every response can flow through the same public cache. You keep the deployment simplicity of classic Web Forms while working in a clean, modern, page-less request model. Building your own site as a C# theme *is* the proof that the architecture holds up in practice.

## The essentials

1. **Inherit `CsTemplate`.** Add a class under `/engine/CsTemplate/Themes/{YourTheme}/` that derives from `CsTemplate` and override `Slug` (it must equal the active-theme slug that selects your theme):
   ```csharp
   public partial class MyTheme : CsTemplate
   {
       public override string Slug { get { return "my-theme"; } }
       public override string Name { get { return "My Theme"; } }
   }
   ```
2. **Keep assets in the standard path.** Put CSS / JavaScript / images in `/assets/themes/{theme_name}/` — exactly the same convention as an HTML theme. Link them yourself in your layout, and bump a cache-buster `?v=` whenever you change one.
3. **Override only the pages you care about.** Every `Handle*` method has a default that falls back to the built-in HTML-theme handler, so a partial C# theme transparently inherits folder-theme rendering for anything it does not override.
4. **Emit through the cache.** Call `WriteCached(html)` to write a page — it participates in the public RAM/file page cache just like the HTML path. Writing straight to `ApiHelper` bypasses the cache.

## Getting data — the unified `CsTemplate` API

An HTML theme receives data through `{{tokens}}`. A C# theme instead calls **public methods on `CsTemplate`**. The CMS core centralises and manages the common data here, so theme code gets a stable, intention-revealing API and never has to know the SQL or the connection details.

### Identity & metadata (override)

| Member | Purpose |
| --- | --- |
| `Slug` | **Required.** Must equal the active-theme slug that selects this theme. |
| `Name` / `Description` / `Author` / `Url` / `Version` | Shown in the Themes library (the C# parallel to a folder theme's `config.txt`). |

### Page handlers (override; each defaults to the HTML-theme handler)

| Method | Route / role |
| --- | --- |
| `HandleHome()` | The site root. |
| `HandlePost(string slug)` | A single post. Return `false` for "not found" (the caller then tries the next match / 404). |
| `HandlePage(string slug)` | A single page. Same `false` = not-found contract. |
| `HandleLatestPost()` | The flat latest-posts listing. |
| `HandleCategoriesLatestPost()` | The per-category sections page. |
| `HandleCategory(string slug)` | A single category listing. |
| `HandleNotFound()` | The 404 page. |

### Output & encoding (protected helpers)

| Member | Use |
| --- | --- |
| `WriteCached(string html)` | Emit the finished page through the public cache — the sanctioned output path. |
| `H(string value)` | HTML-encode user text (titles, excerpts…). Always wrap untrusted values — raw interpolation is an injection risk. |
| `Attr(string value)` | Attribute-encode a value for `href` / `src` etc. |
| `AssetBase` | `/assets/themes/{Slug}` — build links like `AssetBase + "/style.css"`. |
| `WithDb<T>(Func<SQLiteExpress,T> work)` | Open + dispose a SQLite connection around your own query (see below). |

### Settings

| Method | Returns |
| --- | --- |
| `GetSiteName()` | The configured site name. |
| `GetSiteTagline(fallback)` / `GetSiteDescription(fallback)` | Site tagline / description. |
| `GetSetting(key, fallback)` | Any settings value (cached; no query). |
| `GetCountSetting(key, fallback)` | A per-listing post-count setting, clamped to 1–50. |

### Posts, pages & people

| Method | Returns |
| --- | --- |
| `GetRecentPost(int totalPost)` | Newest published posts. |
| `GetCategoryRecentPost(int categoryId, int totalPost)` | Newest posts in one category. |
| `GetAllCategoriesRecentPost(int totalPost)` | Every non-empty category paired with its latest posts. |
| `SearchPosts(string q, int categoryId, int totalPost)` | Relevance search (title > excerpt > content > date); `categoryId > 0` scopes it. |
| `GetPostBySlug(string slug)` / `GetPageBySlug(string slug)` | One published item by slug, or `null`. |
| `GetUserDisplayName(int userId)` | An author's display name (or empty). |
| `GetRelatedPosts(int excludePostId, int categoryId, int totalPost)` | "Keep reading" list: same category first, then latest overall. |
| `ToExcerpt(string content, int maxLength)` | Plain-text excerpt of HTML content. |
| `PostExcerpt(obPost p, int maxLength)` | A post's excerpt, falling back to a trim of its content. |

### Pagination — page through the full set

These let a listing page through *every* matching post instead of capping at the count setting. The per-listing count (e.g. `latest_post_count`) becomes the **page size**; `?page=N` selects the slice.

| Method | Returns |
| --- | --- |
| `PageParam()` | The current 1-based page from `?page=N` (defaults to 1). |
| `TotalPages(int total, int perPage)` | The page count for a row total at a given page size (always ≥ 1). |
| `GetPublishedPostCount()` | Total published posts — the pagination denominator. |
| `SearchPostsCount(string q, int categoryId)` | Total posts matching a search (`categoryId > 0` scopes it). |
| `GetRecentPostPaged(int perPage, int offset)` | One page of the latest posts (`LIMIT perPage OFFSET offset`). |
| `SearchPostsPaged(string q, int categoryId, int perPage, int offset)` | One page of search results, same relevance ordering as `SearchPosts`. |

### Shared render helpers

| Method | Renders |
| --- | --- |
| `RenderSearchBar(actionPath, q)` | The GET search box used by the listing pages. |
| `RenderRowList(posts, showCategory)` | A flat row list [thumb \| title / excerpt / date / category]. |
| `RenderCategorySection(cat, posts)` | One category section (feature post + two mini columns). |
| `RenderPagination(basePath, q, currentPage, totalPages)` | The pagination control as ready HTML. `basePath` is the listing route (e.g. `"/latest-post"`); `q` preserves an active search across pages. Returns `""` for a single page. |

## Pagination — a worked pattern

A paged listing is four moves: read the page number, count the matches, fetch just that page's slice, and render the control. The shipped **Broadsheet** and **Hearth** `HandleLatestPost()` do exactly this:

```csharp
public override void HandleLatestPost()
{
    string q = (HttpContext.Current.Request.QueryString["q"] + "").Trim();
    int perPage = GetCountSetting("latest_post_count");   // page size
    int page = PageParam();                               // ?page=N

    int total = string.IsNullOrEmpty(q)
        ? GetPublishedPostCount()
        : SearchPostsCount(q, 0);
    int totalPages = TotalPages(total, perPage);
    if (page > totalPages) page = totalPages;             // clamp out-of-range
    int offset = (page - 1) * perPage;

    var posts = string.IsNullOrEmpty(q)
        ? GetRecentPostPaged(perPage, offset)
        : SearchPostsPaged(q, 0, perPage, offset);

    // ... render the row list, then drop in the control:
    string pagination = RenderPagination("/latest-post", q, page, totalPages);
}
```

`RenderPagination` emits a `<nav class='pagination'>` with `.pagination-prev` / `.pagination-next` links, a `.pagination-pages` window of `.pagination-page` numbers (a `.pagination-gap` ellipsis when there are many), and `is-active` / `is-disabled` suffix classes — the same class hooks an HTML theme styles, so one CSS block covers both engines. Style them in your theme's CSS (`/assets/themes/{slug}/...`).

The same helpers work for any listing — pass a different `basePath` (e.g. `"/category/essays"`) and the category-scoped count/paged variants to page a category page too.

## Fixed CSS classes — the Markdown toggle

A C# theme has full control over the HTML it emits — with one exception. When you call `ArticleTools.BuildContentArea(content, postId)` (used by `HandlePost`/`HandlePage`, exactly like the shipped `Hearth` and `Broadsheet` themes do in `ArticleHtml`), it may wrap `content` in the "View Content / View Markdown" toggle structure. That markup is owned by the engine, not by your theme, so both C# themes and HTML themes render byte-for-byte identical structure:

```html
<div class='md-toolbar'>
    <button type='button' onclick='showContent();'>View Content</button>
    <button type='button' onclick='showMarkdown();'>View Markdown</button>
</div>
<div id='post_content'> … </div>
<div id='post_markdown' style='display:none'><textarea readonly></textarea></div>
```

You cannot change this markup, and the class/id selectors are fixed: `.md-toolbar`, `.md-toolbar button`, `#post_markdown textarea`. Because it's the engine's contract and not something your theme defines, your theme's own CSS file must style these selectors explicitly — the engine emits no CSS for them. Without it, the toggle renders completely unstyled whenever *Show Markdown button on posts* is on in Settings. See the "Fixed CSS classes" section of the [HTML Template Guide](/admin/themes/docs) for the baseline CSS block to copy — the shipped `hearth-cs`/`broadsheet-cs` stylesheets under `/assets/themes/` already carry it as a working example.

## Complex queries — go straight to SQLite

When the built-in helpers are not enough, query the database directly. Use `WithDb` so the connection is opened and disposed for you, and run parameterised SQL through `SQLiteExpress`:

```csharp
var rows = WithDb(delegate (SQLiteExpress s)
{
    var p = new Dictionary<string, object> { { "@cat", categoryId } };
    return s.GetObjectList<obPost>(
        "SELECT * FROM posts WHERE category_id=@cat AND is_published=1 ORDER BY date_published DESC LIMIT 20;", p);
});
```

You can also create and query **your own tables** for custom features — the same SQLite database backs the whole application, so a C# theme can carry its own data model.

Always parameterise (`@name` + a dictionary). Never concatenate user input into SQL.

## Custom pages, settings & API endpoints

To add something the theme handlers do not cover — a brand-new public page, a custom admin screen, or an API endpoint — register a route in `Global.asax.cs` and point it at your handler. The router is a simple `switch` on the request path:

```csharp
case "/api/my-action":
    MyApi.HandleRequest(); return;
```

A handler reads the request, does its work, and writes a response. For a public, cacheable page use `PublicPageCache.WriteAndCache(html)`; for an API use `ApiHelper` (`WriteJson`, `WriteSuccess`, `WriteError`, or write text directly). Gate admin-only handlers with `AdminGuard.RequireLogin()`.

This is how you build **automated article import and posting**, scheduled maintenance actions, webhooks, or third-party integrations — all in plain C#, all page-less.

### Worked example — the article-markdown endpoint

The shipped `GetArticleMarkdownApi` (route `/api/get-article-markdown?id=123`) is a compact, real example of a public, no-login endpoint: it reads a query parameter, fetches a post, and returns `text/plain`. For markdown-format posts it returns the stored source; for HTML-format posts it converts on the fly with `System.engine.Markdown.HtmlToMarkdown.ToMarkdown(html)`. The result is cached in the RAM tier (honoring the `cache_ram_enabled` setting).

Reusable Markdown utilities: `MarkdownToHtml.ToHtml(md)` and `HtmlToMarkdown.ToMarkdown(html)` in `System.engine.Markdown`. See the [Markdown Documentation](/admin/markdown-docs) for exact rendering behavior.

## The shape of a C# page

A handler queries data, builds HTML (typically a shared `Layout` helper for the head/footer plus the body), and emits it through the cache:

```csharp
public override void HandleHome()
{
    var posts = GetRecentPost(GetCountSetting("home_post_count"));
    var layout = NewLayout(GetSiteName());
    var sb = new StringBuilder();
    sb.Append(layout.RenderHeader());
    foreach (var p in posts)
        sb.Append("<h2>" + H(p.Title) + "</h2>");
    sb.Append(layout.RenderFooter());
    WriteCached(sb.ToString());
}
```

See `/engine/CsTemplate/Themes/Broadsheet/` for the full pattern: a `Layout` helper, one file per page handler, and a shared article renderer.

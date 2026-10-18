# General Guidelines

## About this page

Operational notes for running this site. Each section is a short how-to for a task that isn't part of day-to-day writing.

## Documentation & guides

- [HTML Template Guide](/admin/themes/docs) — authoring token-based HTML themes.
- [C# Template Guide](/admin/themes/docs-csharp) — building code-rendered themes by inheriting `CsTemplate` (and scaffolding an application on the Pageless ASP.NET Web Forms architecture).
- [Markdown Documentation](/admin/markdown-docs) — the supported Markdown syntax and how each construct renders.
- [Migrate Wordpress Into Hearth CMS](https://github.com/ASP-NET-Web-Forms-Club/Hearth-CMS/wiki/Migrate-Wordpress-Into-Hearth-CMS)

## Markdown source on posts — View Content / View Markdown

Every **post** can show two small buttons above its content — **View Content** and **View Markdown** — letting a reader flip between the rendered article and its raw Markdown source. The rendered content is shown by default; the Markdown is fetched **live from the server** only when the reader clicks *View Markdown*, so it never adds weight to the normal page load.

This is controlled by a single switch in [Settings](/admin/settings) → **Post content** → *Show Markdown button on posts*.

- **On by default.** A fresh install shows the buttons. Turn the switch off and save to remove them site-wide.
- **Posts only.** Pages have no Markdown source endpoint, so they never get the buttons — only posts do.
- **Every theme, one behaviour.** The switch applies to *all* themes — both HTML (token) themes and C# (code) themes. You do **not** add anything to a theme to get it; the engine injects the structure into the post body for you.
- **Cache-safe.** Toggling the switch clears the page cache, so the change shows on the next page view.

### Fixed structure — for theme designers

The buttons, the content container and the Markdown box are **rendered by the engine** and are intentionally **not editable from a theme**. The markup is fixed and identical across every theme; a theme only **styles** it with CSS. When the switch is on, a post's content area is emitted as:

```html
<div class='md-toolbar'>
    <button type='button' onclick='showContent();'>View Content</button>
    <button type='button' onclick='showMarkdown();'>View Markdown</button>
</div>
<div id='post_content'>
    <!-- the rendered article HTML -->
</div>
<div id='post_markdown' style='display:none'>
    <textarea readonly></textarea>
</div>
<!-- plus a small script that lazily fetches the raw Markdown into the textarea -->
```

This block sits **inside** your theme's existing `.doc-content.prose` content wrapper, so your content typography still applies. To style the controls, target these hooks in your theme's CSS:

| Selector | What it is |
| --- | --- |
| `.md-toolbar` | The row that holds the two buttons. |
| `.md-toolbar button` | The *View Content* / *View Markdown* buttons. |
| `#post_content` | The rendered-article container (shown by default). |
| `#post_markdown textarea` | The raw-Markdown box (shown after clicking *View Markdown*). |

**The engine ships no CSS for these anymore — styling them is every theme's own responsibility.** Because the markup is fixed by the engine rather than by your template, this is one of the few places where your theme's CSS *must* target a specific, non-negotiable selector rather than a class name of your own choosing. See the "Fixed CSS classes" section of the [HTML Template Guide](/admin/themes/docs) / [C# Template Guide](/admin/themes/docs-csharp) for the baseline CSS block — every shipped theme already carries it in its own stylesheet under `/assets/themes/{slug}/`. Skip it and the toggle renders with no styling at all. Bump your CSS cache-buster after editing. The raw Markdown itself comes from the public endpoint `/api/get-article-markdown?id={postId}` — the same source both engines use.

## Changing the admin login path

The admin panel lives under a single URL segment. By default this is `/admin`. You can rename it to a value of your choosing — this renamed segment is called the **Hidden Admin Path**.

| Term | URL | Description |
| --- | --- | --- |
| Default Path | `/admin` | The system default, the same on every install. |
| Hidden Admin Path | `{hidden_admin_path}` | Your renamed segment, overriding the system default. |

That segment is also the **login screen**: visit it while signed out and you'll be asked to sign in.

**Why rename it.** `/admin` is a universal, guessable entry point — the first thing an automated brute-force or credential-stuffing tool will try. Moving the panel to an uncommon segment (for example `/backend`, `/controlpanel`, or anything meaningful to you) doesn't make the login *itself* stronger, but it removes the obvious target: a bot that doesn't know your path can't hammer a login page it can't find. It lowers your exposure rather than eliminating risk, so treat it as one layer, not a substitute for a strong password. **Choose something meaningful to you but uncommon and not easily guessable.**

There are two ways to set it, and a way to reload after editing the file.

### 1 — First choice — change it in Settings

The normal way. Go to [Settings](/admin/settings) → **Admin URL**, type the new path segment, and save. The panel moves immediately and you'll be redirected to the new address.

- Use letters, numbers, hyphens and underscores only.
- Reserved words (`logout`, `api`, `category`, `reset_app`, `home`, …) are rejected.
- **Bookmark the new URL** as soon as you save — the old one stops working.

This value is stored in the database. It applies unless a `config.txt` override is present (next section), which always wins.

### 2 — Override — `config.txt`

A file-based override that **always wins over the Settings value**. This is the safety hatch: if you ever forget your custom admin path and lock yourself out, you can set it here without touching the database or the code.

Create a file at:

```
/App_Data/config.txt
```

and add a single line naming the path segment:

```
admin_url=backend
```

That puts the panel at `/backend`. To put it back to the default, use:

```
admin_url=admin
```

- Lines starting with `#` or `;` are comments. The format is `key=value`.
- While this line is present, the **Admin URL field in Settings becomes read-only** — the file is in charge. Remove the line (or the file) to hand control back to Settings.
- The same character and reserved-word rules apply; an invalid value is ignored and the system falls back to Settings, then to `admin`.

There is a ready-made template to copy from: `/App_Data/config.txt.example`.

#### Other `config.txt` keys

The same file can carry a `dev_mode` line, which is the file-based override for **Dev Mode** (the developer convenience that auto-logs the first admin so admin and API requests work without signing in):

```
dev_mode=true
```

- Accepts `true`/`1`/`on`/`yes` or `false`/`0`/`off`/`no`.
- Like `admin_url`, the file value **wins over the Settings toggle** and is read once at start-up / on `/reset_app`. When present, the Dev Mode switch in Settings becomes informational only.
- This exists so a developer can flip Dev Mode from a file without touching the database — handy for local work and automated tooling.

> ⚠️ **Dev Mode disables the login wall.** With it on, anyone reaching an admin or API URL is treated as the admin, with no password. Only ever set `dev_mode=true` on a local/development machine, and remove it before exposing the site. The default — no line, and the Settings toggle off — keeps the login required.

### 3 — Loading `config.txt` — `/reset_app`

The file is read **once**, so editing it doesn't take effect on its own. There are two ways it gets loaded:

- **Automatically** when the web app (or the server) restarts — the file is read on start-up.
- **On demand**, without a restart, by visiting [`/reset_app`](/reset_app) in your browser. It re-reads `config.txt` and reports where the admin panel now lives.

So the recovery flow for a forgotten admin path is:

1. Create or edit `/App_Data/config.txt` with `admin_url=admin` (or any path you like).
2. Open `/reset_app` — or just restart the app.
3. Go to the path it reports and sign in.

Tip: `/reset_app` always works regardless of the current admin path, so it's safe to rely on even when you're locked out of the panel.

### Order of precedence

When the site decides where the admin panel lives, it checks, in order:

| # | Source | Wins when… |
| --- | --- | --- |
| 1 | `/App_Data/config.txt` → `admin_url=` | the file exists and the value is valid |
| 2 | Settings → Admin URL (database) | no file override, and a value was saved |
| 3 | Built-in default `admin` | neither of the above is set |

The current Hidden Admin Path for this install is shown in the **This install** panel at the top of this page.

## Resetting a forgotten admin login

If you forget the admin **username or password**, reset them with a one-shot file. This is separate from `config.txt` and does not leave any credentials lying around.

1. Create a file at `/App_Data/reset_admin.txt` with two lines:
   ```
   admin_username=youradmin
   admin_password=your-new-password
   ```
2. Restart the app, or visit [`/reset_app`](/reset_app).
3. The admin login is reset to those values, and the file is **automatically consumed** — deleted if possible, otherwise blanked — so the password never lingers on disk.
4. Sign in with the new credentials, then change the password again from the admin panel if you like.

Notes:

> - Username must be at least 3 characters; password at least 6.
> - The reset is only applied *after* the file has been safely removed or blanked — so the same file can never be replayed.
> - Unlike the old approach, nothing is stored as a standing password in `config.txt`; the new password is hashed into the database like any other.

There is a ready-made template to copy from: `/App_Data/reset_admin.txt.example`.

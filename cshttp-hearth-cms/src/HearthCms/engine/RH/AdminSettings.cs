using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminSettings
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            string siteName = Settings.SiteName;
            string tagline = Settings.SiteTagline;
            string description = Settings.SiteDescription;
            string faviconSource = Settings.FaviconSourceUrl;
            string ogImage = Settings.OgImageUrl;
            string logoUrl = Settings.LogoUrl;
            string logoMode = Settings.LogoMode;
            string themeColor = Settings.ThemeColor;
            string footer = Settings.FooterText;
            string footerColCount = Settings.FooterColCount.ToString();
            string footerCol1 = Settings.FooterCol1;
            string footerCol2 = Settings.FooterCol2;
            string footerCol3 = Settings.FooterCol3;
            string footerCol4 = Settings.FooterCol4;

            // Options for the "number of footer columns" picker (0..4).
            var footerColOpts = new StringBuilder();
            for (int i = 0; i <= 4; i++)
                footerColOpts.Append($"<option value='{i}'{(footerColCount == i.ToString() ? " selected" : "")}>{i}</option>");
            string homeCount = Settings.HomePostCount.ToString();
            string latestCount = Settings.LatestPostCount.ToString();
            string categoriesCount = Settings.CategoriesPostCount.ToString();
            string categoryCount = Settings.CategoryPostCount.ToString();
            string sidebarCount = Settings.ArticleSidebarPostCount.ToString();
            bool showMarkdownBtn = Settings.ShowMarkdownButton;
            bool ramOn = Settings.CacheRamEnabled;
            bool fileOn = Settings.CacheFileEnabled;
            int ramCount = PublicPageCache.Cache.Count;
            string ramMaxMb = Settings.CacheRamMaxMb.ToString();
            double ramUsedMb = PublicPageCache.RamBytesUsed / (1024.0 * 1024.0);
            string ramUsedLabel = ramUsedMb.ToString("0.0") + " MB";
            string ramMaxLabel = (ramMaxMb == "0") ? "unlimited" : (ramMaxMb + " MB");

            // Date display format (e.g. "MMM d, yyyy"). DateFormat already falls
            // back to the default when unset/invalid, so a fresh database shows
            // the default selection.
            string dateFormat = Settings.DateFormat;
            string dateSample = DateDisplay.Format(new DateTime(2026, 6, 18));

            // Sitemap: live URL + last manual-generation time (display only).
            string sitemapUrl = ApiHelper.GetBaseUrl() + "/sitemap.xml";
            string robotsUrl = ApiHelper.GetBaseUrl() + "/robots.txt";
            string sitemapLastRaw = SitemapGenerator.LastGenerated;
            string sitemapLast = "Never (the live sitemap is still always up to date)";
            DateTime sitemapLastDt;
            if (!string.IsNullOrEmpty(sitemapLastRaw) &&
                DateTime.TryParse(sitemapLastRaw, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out sitemapLastDt))
            {
                sitemapLast = sitemapLastDt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " (server local)";
            }

            string homeMode = Settings.HomePageMode;
            if (homeMode != "1" && homeMode != "2" && homeMode != "3" && homeMode != "4") homeMode = "0";

            bool devMode = Settings.IsDevMode;

            bool analyticsEnabled = Settings.AnalyticsEnabled;

            string apiToken = Settings.ApiToken;
            bool apiTokenSet = apiToken.Length > 0;

            // Admin URL slug: the live, resolved value plus whether config.txt is
            // currently pinning it (in which case the field is read-only).
            string adminSlug = AdminSlug.Current;
            bool adminSlugLocked = AdminSlug.IsLockedByConfig;

            var tpl = new AdminTemplate
            {
                Title = "Settings",
                ActiveItem = "settings",
                PageHeading = "Site settings",
                // The favicon field reuses the shared media browser.
                ExtraFooterText = "<script src='/js/media-browser.js'></script>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            sb.Append($@"
<form id='settingsForm' onsubmit='return saveSettings(event)' class='settings-form'>");

            // ===== Site identity & branding card =====
            string sel_text = logoMode == "text" ? " selected" : "";
            string sel_image = logoMode == "image" ? " selected" : "";
            string sel_imgtext = logoMode == "image_text" ? " selected" : "";
            sb.Append($@"
    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-globe'></i> Site identity &amp; branding</h2></div>
        <div class='card-body'>
            <div class='form-field'>
                <label for='site_name'>Site name</label>
                <input type='text' id='site_name' name='site_name' value='{HttpUtility.HtmlAttributeEncode(siteName)}' />
            </div>
            <div class='form-field'>
                <label for='site_tagline'>Tagline</label>
                <input type='text' id='site_tagline' name='site_tagline' value='{HttpUtility.HtmlAttributeEncode(tagline)}' />
            </div>
            <div class='form-field'>
                <label for='site_description'>Description (SEO)</label>
                <textarea id='site_description' name='site_description' rows='3'>{HttpUtility.HtmlEncode(description)}</textarea>
            </div>

            <hr style='border:0;border-top:1px solid var(--border, #e7ddcf);margin:18px 0' />

            <div class='form-field'>
                <label for='logo_url'>Website logo</label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='text' id='logo_url' name='logo_url' value='{HttpUtility.HtmlAttributeEncode(logoUrl)}' placeholder='/media/2026/06/logo.png' style='flex:1' oninput='renderLogoPreview()' />
                    <button type='button' class='btn btn-ghost' onclick='pickImageInto(""logo_url"", renderLogoPreview)'><i class='fa-solid fa-image'></i> Choose</button>
                </div>
                <div id='logoPreview' style='margin-top:10px'></div>
            </div>
            <div class='form-field'>
                <label for='logo_mode'>Logo display</label>
                <select id='logo_mode' name='logo_mode'>
                    <option value='text'{sel_text}>Text only (site name)</option>
                    <option value='image'{sel_image}>Image only</option>
                    <option value='image_text'{sel_imgtext}>Image and text</option>
                </select>
                <p class='form-hint'>Themes render this through the <code>{{{{site_logo}}}}</code> token. Image modes fall back to text if no logo image is set.</p>
            </div>
            <div class='form-field'>
                <label for='favicon_source_url'>Favicon image <span class='form-hint'>(upload one square image &mdash; the full icon set + manifest is generated automatically)</span></label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='text' id='favicon_source_url' name='favicon_source_url' value='{HttpUtility.HtmlAttributeEncode(faviconSource)}' placeholder='/media/2026/06/icon.png' style='flex:1' oninput='renderFaviconSrcPreview()' />
                    <button type='button' class='btn btn-ghost' onclick='pickImageInto(""favicon_source_url"", renderFaviconSrcPreview)'><i class='fa-solid fa-image'></i> Choose</button>
                </div>
                <div id='faviconSrcPreview' style='margin-top:10px'></div>
                <p class='form-hint'>On save, generates <code>/favicon.ico</code> and <code>/media/favicon/</code> (16/32/48/180/192/512 + manifest.json). Leave empty for no favicon.</p>
            </div>
            <div class='form-field'>
                <label for='og_image_url'>Default social share image <span class='form-hint'>(Open Graph &mdash; recommended 1200&times;630)</span></label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='text' id='og_image_url' name='og_image_url' value='{HttpUtility.HtmlAttributeEncode(ogImage)}' placeholder='/media/2026/06/og-image.png' style='flex:1' oninput='renderOgPreview()' />
                    <button type='button' class='btn btn-ghost' onclick='pickImageInto(""og_image_url"", renderOgPreview)'><i class='fa-solid fa-image'></i> Choose</button>
                </div>
                <div id='ogPreview' style='margin-top:10px'></div>
                <p class='form-hint'>Used when sharing pages with no cover image. Posts &amp; pages with a cover image use that instead.</p>
            </div>
            <div class='form-field'>
                <label for='theme_color'>Theme color <span class='form-hint'>(browser UI / PWA tint)</span></label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='color' id='theme_color_picker' value='{(string.IsNullOrEmpty(themeColor) ? "#ffffff" : HttpUtility.HtmlAttributeEncode(themeColor))}' oninput='document.getElementById(""theme_color"").value=this.value' style='width:44px;height:38px;padding:2px' />
                    <input type='text' id='theme_color' name='theme_color' value='{HttpUtility.HtmlAttributeEncode(themeColor)}' placeholder='#E0F3EF (optional)' style='flex:1' />
                </div>
            </div>
        </div>
    </div>");

            // ===== Admin URL card =====
            string lockedHint = adminSlugLocked
                ? "<p class='form-hint' style='color:var(--warn,#d97706)'><i class='fa-solid fa-lock'></i> This is pinned by <code>/App_Data/config.txt</code> and cannot be changed here. Edit that file and visit <code>/reset_app</code> to apply changes.</p>"
                : "";
            sb.Append($@"
    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-user-lock'></i> Admin URL</h2></div>
        <div class='card-body'>
            <div class='form-field'>
                <label for='admin_url'>Admin path</label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <span class='form-hint' style='white-space:nowrap'>{ApiHelper.GetBaseUrl()}/</span>
                    <input type='text' id='admin_url' name='admin_url' value='{HttpUtility.HtmlAttributeEncode(adminSlug)}' placeholder='admin' style='flex:1' {(adminSlugLocked ? "readonly" : "")} />
                </div>
                <p class='form-hint'>The admin panel will live here, e.g. <code>/{HttpUtility.HtmlEncode(adminSlug)}/settings</code>. Letters, numbers, hyphens and underscores only. After saving, the panel moves &mdash; <strong>bookmark the new URL</strong>.</p>
                {lockedHint}
            </div>
            <div class='form-field' style='margin-bottom:0'>
                <p class='form-hint' style='margin:0'><i class='fa-solid fa-life-ring'></i> <strong>Forgot your admin URL?</strong> Create <code>/App_Data/config.txt</code> with a line like <code>admin_url=admin</code>, then open <code>/reset_app</code> in your browser. The file always overrides this setting.</p>
            </div>
        </div>
    </div>");
            sb.Append($@"

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-shoe-prints'></i> Footer</h2></div>
        <div class='card-body'>
            <div class='form-field'>
                <label for='footer_text'>Footer text <span class='form-hint'>(bottom bar &mdash; usually a one-line copyright)</span></label>
                <input type='text' id='footer_text' name='footer_text' value='{HttpUtility.HtmlAttributeEncode(footer)}' />
            </div>

            <hr style='border:0;border-top:1px solid var(--border, #e7ddcf);margin:18px 0' />

            <div class='form-field'>
                <label for='footer_col_count'>Footer columns</label>
                <select id='footer_col_count' name='footer_col_count' class='form-control' onchange='updateFooterCols()'>
                    {footerColOpts}
                </select>
                <p class='form-hint'>Number of content columns shown above the footer bar. Each supports <strong>Markdown</strong> (with raw HTML passthrough). Set to 0 to hide.</p>
            </div>
            <div id='footerColsWrap'>
                <div class='form-field footer-col-field' data-col='1'>
                    <label for='footer_col_1'>Footer Column 1</label>
                    <textarea id='footer_col_1' name='footer_col_1' rows='5' placeholder='### About&#10;A short blurb...'>{HttpUtility.HtmlEncode(footerCol1)}</textarea>
                </div>
                <div class='form-field footer-col-field' data-col='2'>
                    <label for='footer_col_2'>Footer Column 2</label>
                    <textarea id='footer_col_2' name='footer_col_2' rows='5'>{HttpUtility.HtmlEncode(footerCol2)}</textarea>
                </div>
                <div class='form-field footer-col-field' data-col='3'>
                    <label for='footer_col_3'>Footer Column 3</label>
                    <textarea id='footer_col_3' name='footer_col_3' rows='5'>{HttpUtility.HtmlEncode(footerCol3)}</textarea>
                </div>
                <div class='form-field footer-col-field' data-col='4'>
                    <label for='footer_col_4'>Footer Column 4</label>
                    <textarea id='footer_col_4' name='footer_col_4' rows='5'>{HttpUtility.HtmlEncode(footerCol4)}</textarea>
                </div>
            </div>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-table-cells-large'></i> Posts per page</h2></div>
        <div class='card-body'>
            <p class='form-hint' style='margin-top:0;margin-bottom:16px;'>How many post cards each public template displays.</p>
            <div class='form-field'>
                <label for='home_post_count'>Home</label>
                <input type='number' min='1' max='50' id='home_post_count' name='home_post_count' value='{HttpUtility.HtmlAttributeEncode(homeCount)}' />
            </div>
            <div class='form-field'>
                <label for='latest_post_count'>Latest Post</label>
                <input type='number' min='1' max='50' id='latest_post_count' name='latest_post_count' value='{HttpUtility.HtmlAttributeEncode(latestCount)}' />
            </div>
            <div class='form-field'>
                <label for='categories_post_count'>Categories Post <span class='form-hint'>(per category section)</span></label>
                <input type='number' min='1' max='50' id='categories_post_count' name='categories_post_count' value='{HttpUtility.HtmlAttributeEncode(categoriesCount)}' />
            </div>
            <div class='form-field'>
                <label for='category_post_count'>Category Post</label>
                <input type='number' min='1' max='50' id='category_post_count' name='category_post_count' value='{HttpUtility.HtmlAttributeEncode(categoryCount)}' />
            </div>
            <div class='form-field'>
                <label for='article_sidebar_post_count'>Article Sidebar Post <span class='form-hint'>(&ldquo;Keep reading&rdquo; aside)</span></label>
                <input type='number' min='1' max='50' id='article_sidebar_post_count' name='article_sidebar_post_count' value='{HttpUtility.HtmlAttributeEncode(sidebarCount)}' />
            </div>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-house'></i> Home page</h2></div>
        <div class='card-body'>
            <div class='form-field'>
                <label for='home_page_mode'>Main home page</label>
                <select id='home_page_mode' name='home_page_mode' class='form-control' onchange='onHomeModeChange()'>
                    <option value='0'{(homeMode == "0" ? " selected" : "")}>Default</option>
                    <option value='1'{(homeMode == "1" ? " selected" : "")}>Page</option>
                    <option value='2'{(homeMode == "2" ? " selected" : "")}>Latest Post</option>
                    <option value='3'{(homeMode == "3" ? " selected" : "")}>Category List + Latest Post</option>
                </select>
            </div>
            <div class='form-field' id='homePageSelectField' style='{(homeMode == "1" ? "" : "display:none")}'>
                <label for='home_page_id'>Select page</label>
                <div id='homePageSelectContainer'></div>
            </div>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-calendar-day'></i> Date display</h2></div>
        <div class='card-body'>
            <div class='form-field'>
                <label for='date_format'>Date format</label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='text' id='date_format' name='date_format' value='{HttpUtility.HtmlAttributeEncode(dateFormat)}' placeholder='MMM d, yyyy' style='flex:1' oninput='renderDatePreview()' />
                    <span class='form-hint' style='white-space:nowrap'>Preview: <strong id='datePreview'>{HttpUtility.HtmlEncode(dateSample)}</strong></span>
                </div>
                <p class='form-hint'>Controls how every date is shown across the site and admin. Uses standard C# date format codes. Default is <code>MMM d, yyyy</code>.</p>
                <div class='form-hint' style='margin-top:8px;line-height:1.7'>
                    <strong>Day:</strong> <code>d</code> (1-31), <code>dd</code> (01-31), <code>ddd</code> (Mon), <code>dddd</code> (Monday)<br/>
                    <strong>Month:</strong> <code>M</code> (1-12), <code>MM</code> (01-12), <code>MMM</code> (Jan), <code>MMMM</code> (January)<br/>
                    <strong>Year:</strong> <code>y</code> (0-99), <code>yy</code> (00-99), <code>yyyy</code> (e.g. 2026)<br/>
                    <strong>Separators:</strong> space, comma <code>,</code> dash <code>-</code> dot <code>.</code> slash <code>/</code> backslash <code>\</code>
                </div>
            </div>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-sitemap'></i> Sitemap &amp; search engines</h2></div>
        <div class='card-body'>
            <p class='form-hint' style='margin-top:0;margin-bottom:14px;'>
                Your sitemap is generated <strong>live</strong> from your published pages, posts and categories &mdash;
                it is always current and never needs re-uploading. Submit the URL below once to
                <a href='https://search.google.com/search-console' target='_blank' rel='noopener'>Google Search Console</a> or
                <a href='https://www.bing.com/webmasters' target='_blank' rel='noopener'>Bing Webmaster Tools</a>.
                <code>robots.txt</code> already points crawlers at it automatically.
            </p>
            <div class='form-field'>
                <label>Sitemap URL <span class='form-hint'>(submit this to your webmaster tools)</span></label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='text' id='sitemapUrlBox' value='{HttpUtility.HtmlAttributeEncode(sitemapUrl)}' readonly style='flex:1' onclick='this.select()' />
                    <a class='btn btn-ghost' href='{HttpUtility.HtmlAttributeEncode(sitemapUrl)}' target='_blank' rel='noopener'><i class='fa-solid fa-up-right-from-square'></i> View</a>
                </div>
                <p class='form-hint'>Also published: <a href='{HttpUtility.HtmlAttributeEncode(robotsUrl)}' target='_blank' rel='noopener'>{HttpUtility.HtmlEncode(robotsUrl)}</a></p>
            </div>
            <div class='form-field' style='margin-bottom:0'>
                <p class='form-hint' style='margin:0 0 10px 0'>Last manual regeneration: <strong id='sitemapLast'>{HttpUtility.HtmlEncode(sitemapLast)}</strong></p>
                <button type='button' class='btn btn-ghost btn-sm' onclick='generateSitemapNow()'><i class='fa-solid fa-rotate'></i> Regenerate sitemap now</button>
                <p class='form-hint' style='margin-top:8px'>The live sitemap auto-updates whenever you publish or edit content. Use this button only if you want to force an immediate rebuild and confirm the URL count.</p>
            </div>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-code'></i> Post content</h2></div>
        <div class='card-body'>
            <label class='switch'>
                <input type='checkbox' name='show_markdown_button' {(showMarkdownBtn ? "checked" : "")} />
                <span>Show Markdown button on posts</span>
            </label>
            <p class='form-hint' style='margin-top:10px'>
                Display Markdown button on posts <span class='form-hint'>(renders <strong>View Content</strong> / 
                <strong>View Markdown</strong> buttons at the top of every post's content. 
                The rendered content shows by default; switching to Markdown fetches the raw source live from the server. 
                Applies to all themes.)</span>
            </p>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'>
            <h2><i class='fa-solid fa-bolt'></i> Page Content Caching</h2>
            <span class='text-muted' style='font-size:12.5px'>RAM: <strong>{ramCount}</strong> page(s), <strong>{ramUsedLabel}</strong> / {ramMaxLabel}</span>
        </div>
        <div class='card-body'>
            <label class='switch'>
                <input type='checkbox' name='cache_ram_enabled' {(ramOn ? "checked" : "")} />
                <span>Enable RAM cache <span class='form-hint'>(in-process, lost on app restart; also governs template-file caching)</span></span>
            </label>
            <label class='switch'>
                <input type='checkbox' name='cache_file_enabled' {(fileOn ? "checked" : "")} />
                <span>Enable File cache <span class='form-hint'>(/App_Data/page_cache/, survives restarts)</span></span>
            </label>
            <div class='form-group form-field' style='margin-top:14px;max-width:320px'>
                <label for='cache_ram_max_mb'>Maximum RAM cache (MB)</label>
                <input type='number' id='cache_ram_max_mb' name='cache_ram_max_mb' class='form-control' min='0' max='4096' step='1' value='{ramMaxMb}' />
                <p class='form-hint'>Applies to the RAM page cache only. When usage exceeds this, the least-recently-accessed pages are evicted first. Set <strong>0</strong> for no limit (uses as much memory as needed). Values 1&ndash;4 are raised to 5; the cap is 4096.</p>
            </div>
            <p class='form-hint' style='margin-top:10px'>Cache is fully invalidated on any content, theme, or settings change. Toggling a switch and saving will clear all cached pages.</p>
            <div style='margin-top:14px'>
                <button type='button' class='btn btn-ghost btn-sm' onclick='clearCacheNow()'><i class='fa-solid fa-broom'></i> Clear cache now</button>
            </div>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-chart-line'></i> Internal Content Analytics</h2></div>
        <div class='card-body'>
            <label class='switch'>
                <input type='checkbox' name='analytics_enabled' {(analyticsEnabled ? "checked" : "")} />
                <span>Enable analytics</span>
            </label>
            <p class='form-hint' style='margin-top:10px'>
                Tracks page views and reading time on <strong>public pages only</strong> (never admin, never login).
                No IP addresses are ever recorded. Data is stored in its own local file
                (<code>App_Data/hearth_analytics.sqlite</code>), separate from the main CMS database.
                When disabled, the tracking script is not included on the site at all.
            </p>
            <p class='form-hint' style='margin-top:8px'>
                View reports on the <a href='/admin/analytics'>Analytics</a> page, where you can also clear all
                collected data.
            </p>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-flask'></i> Dev / Testing</h2></div>
        <div class='card-body'>
            <label class='switch'>
                <input type='checkbox' name='dev_mode' {(devMode ? "checked" : "")} />
                <span>Enable Dev Mode</span>
            </label>
            <p class='form-hint' style='margin-top:10px'>
                When Dev Mode is on, the request pipeline <strong>auto-logs the admin user</strong>,
                so admin pages are reachable without going through the login screen. This is meant
                for local development and testing only.
            </p>
            <p class='form-hint' style='color:var(--warn,#d97706);margin-top:8px'>
                <i class='fa-solid fa-triangle-exclamation'></i> <strong>Do not enable this on a public site.</strong>
                With Dev Mode on, anyone reaching an admin URL is signed in as the first admin account
                with no password.
            </p>
        </div>
    </div>

    <div class='card'>
        <div class='card-header'><h2><i class='fa-solid fa-key'></i> API access token</h2></div>
        <div class='card-body'>
            <p class='form-hint' style='margin-top:0'>
                A shared secret that lets external tools call the API <strong>without a login</strong> &mdash;
                for migration scripts, automation, or an AI agent posting over MCP. Send it as the
                <code>X-Api-Token</code> request header, or as an <code>api_token</code> query-string or form field.
                See <a href='/admin/guidelines'>General Guidelines</a> for the full import protocol.
            </p>
            <div class='form-field'>
                <label for='api_token_box'>Current token</label>
                <div style='display:flex;gap:8px;align-items:center'>
                    <input type='text' id='api_token_box' value='{HttpUtility.HtmlAttributeEncode(apiToken)}' readonly placeholder='(no token set &mdash; token access is disabled)' style='flex:1;font-family:monospace' onclick='this.select()' />
                    <button type='button' class='btn btn-ghost' onclick='copyApiToken()'><i class='fa-solid fa-copy'></i> Copy</button>
                </div>
                <p class='form-hint'>{(apiTokenSet ? "Token access is <strong>enabled</strong>. Treat this string like a password." : "No token is set, so token access is <strong>disabled</strong>. Generate one to enable it.")}</p>
            </div>
            <div style='display:flex;gap:8px;flex-wrap:wrap'>
                <button type='button' class='btn btn-primary btn-sm' onclick='generateApiToken()'><i class='fa-solid fa-rotate'></i> {(apiTokenSet ? "Regenerate token" : "Generate token")}</button>
                <button type='button' class='btn btn-ghost btn-sm' onclick='clearApiToken()' {(apiTokenSet ? "" : "disabled")}><i class='fa-solid fa-ban'></i> Clear (disable)</button>
            </div>
            <p class='form-hint' style='color:var(--warn,#d97706);margin-top:10px'>
                <i class='fa-solid fa-triangle-exclamation'></i> Regenerating <strong>immediately invalidates the old token</strong>;
                update anything that uses it. Anyone holding this token can create and modify content as the admin.
            </p>
        </div>
    </div>

    <div class='settings-actions'>
        <button type='submit' class='btn btn-primary'><i class='fa-solid fa-floppy-disk'></i> Save settings</button>
    </div>
</form>
<script>
async function saveSettings(e) {{
    e.preventDefault();
    var form = document.getElementById('settingsForm');
    var fd = new FormData(form);
    fd.append('action', 'save');
    // Normalize every checkbox to a clean string (including cache toggles).
    form.querySelectorAll('input[type=checkbox]').forEach(function(cb) {{
        fd.set(cb.name, cb.checked ? '1' : '0');
    }});
    try {{
        var r = await fetch('/api/admin/settings', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success) {{
            // The admin URL may have just changed. Redirect to the resolved
            // base so we don't reload a path that no longer routes.
            var base = (d.data && d.data.adminBase) ? d.data.adminBase : '/admin';
            var target = base.replace(/\/$/, '') + '/settings';
            if (window.location.pathname !== target) {{
                window.location = target;
            }} else {{
                flashGoodAndReload('Saved', 'Settings updated.');
            }}
        }} else showErrorMessage('Save failed', d.message);
    }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
    return false;
}}
async function clearCacheNow() {{
    var fd = new FormData();
    fd.append('action', 'clear-cache');
    try {{
        var r = await fetch('/api/admin/settings', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success) flashGoodAndReload('Cache cleared', 'Cache has been cleared.');
        else showErrorMessage('Failed', d.message);
    }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
}}
async function generateApiToken() {{
    if (document.getElementById('api_token_box').value &&
        !confirm('Regenerate the API token? The current token will stop working immediately.')) return;
    var fd = new FormData();
    fd.append('action', 'generate-api-token');
    try {{
        var r = await fetch('/api/admin/settings', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success && d.data && d.data.token) {{
            // Reload so the box, hints and button states reflect the new token
            // (which is now saved). The token is shown in the box after reload.
            flashGoodAndReload('Token generated', 'Copy your new token from the API access section.');
        }} else showErrorMessage('Failed', d.message || 'Could not generate token.');
    }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
}}
async function clearApiToken() {{
    if (!confirm('Clear the API token? Token-based API access will be disabled until you generate a new one.')) return;
    var fd = new FormData();
    fd.append('action', 'clear-api-token');
    try {{
        var r = await fetch('/api/admin/settings', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success) flashGoodAndReload('Token cleared', 'Token access disabled.');
        else showErrorMessage('Failed', d.message);
    }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
}}
function copyApiToken() {{
    var box = document.getElementById('api_token_box');
    if (!box.value) {{ showErrorMessage('Nothing to copy', 'Generate a token first.'); return; }}
    box.select();
    var done = function() {{ showGoodMessage('Copied', 'Token copied to clipboard.'); }};
    if (navigator.clipboard && navigator.clipboard.writeText) {{
        navigator.clipboard.writeText(box.value).then(done, function() {{
            try {{ document.execCommand('copy'); done(); }} catch (e) {{ showErrorMessage('Copy failed', 'Select and copy manually.'); }}
        }});
    }} else {{
        try {{ document.execCommand('copy'); done(); }} catch (e) {{ showErrorMessage('Copy failed', 'Select and copy manually.'); }}
    }}
}}
async function generateSitemapNow() {{
    var fd = new FormData();
    fd.append('action', 'generate-sitemap');
    try {{
        var r = await fetch('/api/admin/settings', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success) flashGoodAndReload('Sitemap regenerated', d.message);
        else showErrorMessage('Failed', d.message);
    }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
}}
async function loadHomePagesSelect() {{
    var container = document.getElementById('homePageSelectContainer');
    if (container.getAttribute('data-loaded') === '1') return;
    container.innerHTML = '<span class=""form-hint"">Loading pages...</span>';
    var fd = new FormData();
    fd.append('action', 'home-pages-select');
    try {{
        var r = await fetch('/api/admin/settings', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success && d.data && typeof d.data.html === 'string') {{
            container.innerHTML = d.data.html;
            container.setAttribute('data-loaded', '1');
        }} else {{
            container.innerHTML = '<span class=""form-hint"">Could not load pages.</span>';
        }}
    }} catch (ex) {{
        container.innerHTML = '<span class=""form-hint"">Network error loading pages.</span>';
    }}
}}
function onHomeModeChange() {{
    var mode = document.getElementById('home_page_mode').value;
    var field = document.getElementById('homePageSelectField');
    if (mode === '1') {{
        field.style.display = '';
        loadHomePagesSelect();
    }} else {{
        field.style.display = 'none';
    }}
}}
// Preload the page list when the saved mode is already 'Page'.
if (document.getElementById('home_page_mode').value === '1') {{
    loadHomePagesSelect();
}}
// Show only as many footer-column textareas as the chosen count.
function updateFooterCols() {{
    var n = parseInt(document.getElementById('footer_col_count').value, 10) || 0;
    document.querySelectorAll('#footerColsWrap .footer-col-field').forEach(function(f) {{
        var c = parseInt(f.getAttribute('data-col'), 10);
        f.style.display = (c <= n) ? '' : 'none';
    }});
}}
updateFooterCols();

// Generic media picker: fills the given input id, then runs an optional callback.
async function pickImageInto(inputId, cb) {{
    if (typeof mediaBrowser === 'undefined') {{ showErrorMessage('Media browser unavailable', 'media-browser.js did not load.'); return; }}
    var url = await mediaBrowser.pick({{ accept: ['image/*'] }});
    if (!url) return;
    if (Array.isArray(url)) url = url[0];
    document.getElementById(inputId).value = url;
    if (typeof cb === 'function') cb();
}}
function imgPreview(boxId, url, size) {{
    var v = (url || '').trim();
    document.getElementById(boxId).innerHTML = v
        ? ""<img src='"" + v + ""' alt='' style='max-width:"" + size + ""px;max-height:"" + size + ""px;object-fit:contain;border:1px solid #e5e7eb;border-radius:6px;background:#fff;padding:4px;vertical-align:middle' />""
        : '';
}}
function renderLogoPreview() {{ imgPreview('logoPreview', document.getElementById('logo_url').value, 120); }}
function renderFaviconSrcPreview() {{ imgPreview('faviconSrcPreview', document.getElementById('favicon_source_url').value, 64); }}
function renderOgPreview() {{ imgPreview('ogPreview', document.getElementById('og_image_url').value, 240); }}
renderLogoPreview(); renderFaviconSrcPreview(); renderOgPreview();

// Live date-format preview, mirroring the C# tokens against a fixed sample
// date (Thu 18 June 2026). Unknown text is left as-is here; the server
// re-validates and falls back to the default on save.
function renderDatePreview() {{
    var d = new Date(2026, 5, 18); // month is 0-based: 5 = June
    var days = ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
    var daysShort = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
    var months = ['January','February','March','April','May','June','July','August','September','October','November','December'];
    var monthsShort = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
    function pad(n) {{ return (n < 10 ? '0' : '') + n; }}
    var fmt = document.getElementById('date_format').value;
    // Replace longest tokens first so 'dddd' wins over 'ddd' etc.
    var map = [
        ['dddd', days[d.getDay()]],
        ['ddd',  daysShort[d.getDay()]],
        ['dd',   pad(d.getDate())],
        ['d',    String(d.getDate())],
        ['MMMM', months[d.getMonth()]],
        ['MMM',  monthsShort[d.getMonth()]],
        ['MM',   pad(d.getMonth() + 1)],
        ['M',    String(d.getMonth() + 1)],
        ['yyyy', String(d.getFullYear())],
        ['yy',   pad(d.getFullYear() % 100)],
        ['y',    String(d.getFullYear() % 100)]
    ];
    var out = '', i = 0;
    outer: while (i < fmt.length) {{
        for (var t = 0; t < map.length; t++) {{
            var tok = map[t][0];
            if (fmt.substr(i, tok.length) === tok) {{ out += map[t][1]; i += tok.length; continue outer; }}
        }}
        out += fmt[i]; i++;
    }}
    document.getElementById('datePreview').textContent = out;
}}
renderDatePreview();
</script>
");
            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Text;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Hearth
{
    public partial class Hearth : CsTemplate
    {
        public override void HandleHome()
        {
            // Honor the admin "Main home page" setting, mirroring the contract in
            // HomePage.HandleRequest(). Each non-default target keeps its own route
            // (e.g. /latest-post); selecting it here ALSO renders it at the root.
            // We delegate to this theme's own handlers so the root stays themed.
            switch (Settings.HomePageMode)
            {
                case "1": // A specific published Page
                    if (TryRenderSelectedHomePage()) return;
                    // Fall through to the default home layout if missing/unpublished.
                    break;
                case "2": // Latest posts (flat list) -> /latest-post
                    HandleLatestPost(); return;
                case "3": // Categories + latest post -> /categories-latest-post
                    HandleCategoriesLatestPost(); return;
            }

            RenderDefaultHome();
        }

        // Renders the admin-selected published page as the home page (themed).
        // Returns false when no valid published page is configured, so the
        // caller falls back to the default home layout.
        bool TryRenderSelectedHomePage()
        {
            obPage page = GetPageById(Settings.HomePageId);
            if (page == null) return false;
            // Reuse the themed single-page renderer via its slug.
            return HandlePage(page.Slug);
        }

        // ----- default home : hero + latest-posts grid + feature row -----
        void RenderDefaultHome()
        {
            var layout = new Layout
            {
                Title = GetSiteName(),
                MetaDescription = GetSiteDescription(),
                AssetBaseUrl = AssetBase
            };

            // How many recent posts to feature (admin-tunable, same as HTML home).
            int homeCount = GetCountSetting("home_post_count");

            // Recent posts via the CsTemplate data helper (no direct SQLite here).
            var recent = GetRecentPost(homeCount);

            string siteName = GetSiteName();
            string siteTagline = GetSiteTagline(Config.SiteTagline);
            string siteDescription = GetSiteDescription("A warm, minimalist CMS for posts, pages and quiet reading.");

            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());

            // ----- hero (mirrors home.html) -----
            sb.Append(string.Format(@"
<section class='hero'>
    <div class='container'>
        <span class='hero-eyebrow'><i class='fa-solid fa-fire-flame-simple'></i> <span>{0}</span></span>
        <h1 class='hero-title'>{1}</h1>
        <p class='hero-sub'>{2}</p>
        <div class='hero-actions'>
            <a href='/latest-post' class='btn btn-primary'><span>Read the latest posts</span> <i class='fa-solid fa-arrow-right'></i></a>
            <a href='/about' class='btn btn-ghost'><span>About</span></a>
        </div>
        <figure class='hero-figure' style='max-width:560px;margin:36px auto 0'>
            <img src='{3}/img/showcase.jpg' alt='' style='width:100%;height:auto;display:block;border-radius:16px;border:1px solid var(--border);box-shadow:var(--shadow-sm)' />
        </figure>
    </div>
</section>
", H(siteName), H(siteTagline), H(siteDescription), Attr(AssetBase)));

            // ----- latest writing (mirrors components/section-latest-posts.html
            //       + components/post-card.html); omitted when there are no posts -----
            if (recent.Count > 0)
            {
                sb.Append(@"
<section class='section'>
    <div class='container'>
        <div class='section-heading'>
            <h2>Latest writing</h2>
            <a href='/latest-post' class='section-link'>All posts <i class='fa-solid fa-arrow-right'></i></a>
        </div>
        <div class='post-grid'>");

                for (int i = 0; i < recent.Count; i++)
                {
                    var p = recent[i];
                    string excerpt = PostExcerpt(p, 160);
                    string pinnedCls = p.IsPinned == 1 ? " is-pinned" : "";
                    string pinnedBadge = p.IsPinned == 1 ? "<span class='post-pinned'><i class='fa-solid fa-thumbtack'></i> Pinned</span> " : "";

                    sb.Append(string.Format(@"
            <article class='post-card{0}'>
                <a href='/{1}' class='post-card-inner'>
                    <h3 class='post-card-title'>{2}</h3>
                    <p class='post-card-excerpt'>{3}</p>
                    <span class='post-card-meta'>{4}<i class='fa-regular fa-calendar'></i> {5}</span>
                </a>
            </article>", pinnedCls, Attr(p.Slug), H(p.Title), H(excerpt), pinnedBadge, H(DateDisplay.Format(p.DatePublished))));
                }

                sb.Append(@"
        </div>
    </div>
</section>
");
            }

            // ----- feature grid (mirrors home.html) -----
            sb.Append(@"
<section class='section section-bordered'>
    <div class='container features-grid'>
        <div class='feature'>
            <span class='feature-icon'><i class='fa-solid fa-feather-pointed'></i></span>
            <h3>Made to read</h3>
            <p>Warm typography, generous margins, and a single ember accent. Words first.</p>
        </div>
        <div class='feature'>
            <span class='feature-icon'><i class='fa-solid fa-bolt'></i></span>
            <h3>Fast &amp; quiet</h3>
            <p>Server-rendered, cached, and dependency-light. Pages arrive instantly.</p>
        </div>
        <div class='feature'>
            <span class='feature-icon'><i class='fa-solid fa-mug-hot'></i></span>
            <h3>A place to gather</h3>
            <p>Posts, pages, and categories around one hearth &mdash; calm and uncluttered.</p>
        </div>
    </div>
</section>
");

            // ----- background-image banner (mirrors home.html) -----
            sb.Append(string.Format(@"
<section class='section'>
    <div class='container'>
        <div style='position:relative;min-height:260px;border-radius:16px;overflow:hidden;background-image:url(""{0}/img/showcase.jpg"");background-size:cover;background-position:center;display:flex;align-items:flex-end;padding:26px;border:1px solid var(--border)'>
            <span style='position:relative;z-index:1;font-family:var(--serif);font-size:1.4rem;font-weight:600;color:#fff;text-shadow:0 2px 14px rgba(0,0,0,.55)'>A background-image region</span>
        </div>
    </div>
</section>
", Attr(AssetBase)));

            sb.Append(layout.RenderFooter());

            // Sanctioned output: through the public page cache.
            WriteCached(sb.ToString());
        }
    }
}

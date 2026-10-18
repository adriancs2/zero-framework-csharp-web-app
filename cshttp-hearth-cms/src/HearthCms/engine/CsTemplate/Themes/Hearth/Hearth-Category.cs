using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Hearth
{
    public partial class Hearth : CsTemplate
    {
        // ----- /category/{slug} : one category, scoped + search -----
        public override void HandleCategory(string slug)
        {
            string q = (HttpContext.Current.Request.QueryString["q"] + "").Trim();
            int limit = GetCountSetting("category_post_count");

            obCategory matchedCat = CategoryManager.GetBySlug(slug);
            if (matchedCat == null || matchedCat.Id <= 0)
            {
                HandleNotFound();
                return;
            }

            // Data via the CsTemplate helpers - no direct SQLite in theme code.
            List<obPost> posts = string.IsNullOrEmpty(q)
                ? GetCategoryRecentPost(matchedCat.Id, limit)
                : SearchPosts(q, matchedCat.Id, limit);

            string matchedCategory = matchedCat.Name;

            string crumbs = string.Format(@"<nav class='breadcrumbs' aria-label='Breadcrumb'>
                <a href='/'>Home</a><span class='sep'>/</span>
                <a href='/categories-latest-post'>Categories</a><span class='sep'>/</span>
                <span class='crumb-current'>{0}</span>
            </nav>", H(matchedCategory));
            string searchMeta = string.IsNullOrEmpty(q)
                ? ""
                : string.Format("<p class='search-meta'>{0} result(s) for &ldquo;{1}&rdquo; in {2} &middot; <a href='/category/{3}'>Clear</a></p>",
                    posts.Count, H(q), H(matchedCategory), Attr(slug));

            var layout = NewLayout(matchedCategory);
            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());
            sb.Append(string.Format(@"
<section class='section'>
    <div class='container container-narrow'>
        <div class='list-head'>
            {0}
            <h1>{1}</h1>
            <p class='list-sub'>Posts in this category.</p>
        </div>
        {2}
        {3}
        {4}
    </div>
</section>",
                crumbs,
                H(matchedCategory),
                RenderSearchBar("/category/" + HttpUtility.UrlEncode(slug), q),
                searchMeta,
                RenderRowList(posts, false)));
            sb.Append(layout.RenderFooter());
            WriteCached(sb.ToString());
        }
    }
}

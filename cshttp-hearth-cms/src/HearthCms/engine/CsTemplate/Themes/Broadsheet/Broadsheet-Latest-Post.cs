using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Broadsheet
{
    public partial class Broadsheet : CsTemplate
    {
        // ----- /latest-post : flat row list + search -----
        public override void HandleLatestPost()
        {
            string q = (HttpContext.Current.Request.QueryString["q"] + "").Trim();
            int perPage = GetCountSetting("latest_post_count");
            int page = PageParam();

            // Total + page slice via the CsTemplate helpers - no direct SQLite.
            int total = string.IsNullOrEmpty(q)
                ? GetPublishedPostCount()
                : SearchPostsCount(q, 0);
            int totalPages = TotalPages(total, perPage);
            if (page > totalPages) page = totalPages;
            int offset = (page - 1) * perPage;

            List<obPost> posts = string.IsNullOrEmpty(q)
                ? GetRecentPostPaged(perPage, offset)
                : SearchPostsPaged(q, 0, perPage, offset);

            string subheading = string.IsNullOrEmpty(q)
                ? "<p class='list-sub'>Fresh writing, newest first.</p>" : "";
            string searchMeta = string.IsNullOrEmpty(q)
                ? ""
                : string.Format("<p class='search-meta'>{0} result(s) for &ldquo;{1}&rdquo; &middot; <a href='/latest-post'>Clear</a></p>",
                    total, H(q));
            string pagination = RenderPagination("/latest-post", q, page, totalPages);

            var layout = NewLayout("Latest posts");
            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());
            sb.Append(string.Format(@"
<section class='section'>
    <div class='container container-narrow'>
        <div class='list-head'>
            <h1>Latest posts</h1>
            {0}
        </div>
        {1}
        {2}
        {3}
        {4}
    </div>
</section>",
                subheading,
                RenderSearchBar("/latest-post", q),
                searchMeta,
                RenderRowList(posts, true),
                pagination));
            sb.Append(layout.RenderFooter());
            WriteCached(sb.ToString());
        }
    }
}

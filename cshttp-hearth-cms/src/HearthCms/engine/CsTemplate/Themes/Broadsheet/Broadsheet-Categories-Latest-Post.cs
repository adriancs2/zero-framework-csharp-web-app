using System;
using System.Collections.Generic;
using System.Text;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Broadsheet
{
    public partial class Broadsheet : CsTemplate
    {
        // ----- /categories-latest-post : per-category sections -----
        public override void HandleCategoriesLatestPost()
        {
            int perCat = GetCountSetting("categories_post_count");

            // Data via the CsTemplate helper: every category with posts, each
            // paired with its latest posts (the loop + inner loop live in the
            // engine, not in theme code).
            List<obCategoryPost> catPosts = GetAllCategoriesRecentPost(perCat);

            var sectionsHtml = new StringBuilder();
            foreach (var cp in catPosts)
            {
                sectionsHtml.Append(RenderCategorySection(cp.Category, cp.Posts));
            }

            string sections = catPosts.Count == 0
                ? "<div class='container'><p class='empty-state'>No categorised posts yet.</p></div>"
                : sectionsHtml.ToString();

            var layout = NewLayout("Browse by category");
            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());
            sb.Append(string.Format(@"
<div class='section list-intro'>
    <div class='container container-narrow'>
        <div class='list-head'>
            <h1>Browse by category</h1>
            <p class='list-sub'>The latest from every category.</p>
        </div>
    </div>
</div>
{0}", sections));
            sb.Append(layout.RenderFooter());
            WriteCached(sb.ToString());
        }
    }
}

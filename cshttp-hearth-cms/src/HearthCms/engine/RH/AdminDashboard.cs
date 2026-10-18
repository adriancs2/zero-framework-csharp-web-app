using System.Data.SQLite;
using System.Text;

namespace System.engine.RH
{
    public static class AdminDashboard
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            int pageCount = 0, postCount = 0, mediaCount = 0, publishedPosts = 0, draftPosts = 0;
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    pageCount = s.ExecuteScalar<int>("SELECT COUNT(*) FROM pages WHERE is_deleted=0;");
                    postCount = s.ExecuteScalar<int>("SELECT COUNT(*) FROM posts WHERE is_deleted=0;");
                    mediaCount = s.ExecuteScalar<int>("SELECT COUNT(*) FROM media;");
                    publishedPosts = s.ExecuteScalar<int>("SELECT COUNT(*) FROM posts WHERE is_published=1 AND is_deleted=0;");
                    draftPosts = postCount - publishedPosts;
                }
            }

            var tpl = new AdminTemplate
            {
                Title = "Dashboard",
                ActiveItem = "dashboard",
                PageHeading = "Dashboard"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            // Database upgrade status. Empty on success (show nothing); on a failed
            // schema upgrade it carries: "DB Version x. Upgrade error, refer log
            // file yyyy-MM-dd_HHmmss.txt".
            string upgradeStatus = Db.UpgradeStatus;
            if (!string.IsNullOrEmpty(upgradeStatus))
            {
                sb.Append(@"
<div class='alert alert-danger' style='margin-bottom:1rem;padding:.85rem 1rem;border:1px solid #e3b1b1;background:#fbecec;color:#8a1f1f;border-radius:8px;display:flex;align-items:center;gap:.6rem'>
    <i class='fa-solid fa-triangle-exclamation'></i>
    <span>" + System.Web.HttpUtility.HtmlEncode(upgradeStatus) + @"</span>
</div>");
            }

            sb.Append($@"
<div class='stat-grid'>
    <a href='/admin/pages' class='stat-card'>
        <div class='stat-icon'><i class='fa-solid fa-file-lines'></i></div>
        <div class='stat-info'>
            <div class='stat-value'>{pageCount}</div>
            <div class='stat-label'>Pages</div>
        </div>
    </a>
    <a href='/admin/posts' class='stat-card'>
        <div class='stat-icon'><i class='fa-solid fa-pen-nib'></i></div>
        <div class='stat-info'>
            <div class='stat-value'>{postCount}</div>
            <div class='stat-label'>Posts</div>
        </div>
    </a>
    <a href='/admin/media' class='stat-card'>
        <div class='stat-icon'><i class='fa-solid fa-images'></i></div>
        <div class='stat-info'>
            <div class='stat-value'>{mediaCount}</div>
            <div class='stat-label'>Media files</div>
        </div>
    </a>
    <div class='stat-card'>
        <div class='stat-icon'><i class='fa-solid fa-circle-check'></i></div>
        <div class='stat-info'>
            <div class='stat-value'>{publishedPosts}</div>
            <div class='stat-label'>Published posts</div>
        </div>
    </div>
</div>

<div class='card-grid card-grid-2'>
    <section class='card'>
        <div class='card-header'>
            <h2><i class='fa-solid fa-bolt'></i> Quick actions</h2>
        </div>
        <div class='card-body quick-actions'>
            <a href='/admin/posts/new' class='quick-action'>
                <i class='fa-solid fa-feather-pointed'></i>
                <span>Write a new post</span>
            </a>
            <a href='/admin/pages/new' class='quick-action'>
                <i class='fa-solid fa-plus'></i>
                <span>Create a new page</span>
            </a>
            <a href='/admin/media' class='quick-action'>
                <i class='fa-solid fa-cloud-arrow-up'></i>
                <span>Upload media</span>
            </a>
            <a href='/admin/settings' class='quick-action'>
                <i class='fa-solid fa-sliders'></i>
                <span>Edit site settings</span>
            </a>
        </div>
    </section>
    <section class='card'>
        <div class='card-header'>
            <h2><i class='fa-solid fa-circle-info'></i> Status</h2>
        </div>
        <div class='card-body'>
            <ul class='status-list'>
                <li><span>Published posts</span><strong>{publishedPosts}</strong></li>
                <li><span>Drafts</span><strong>{draftPosts}</strong></li>
                <li><span>Total pages</span><strong>{pageCount}</strong></li>
                <li><span>Media files</span><strong>{mediaCount}</strong></li>
            </ul>
        </div>
    </section>
</div>
");
            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }
    }
}

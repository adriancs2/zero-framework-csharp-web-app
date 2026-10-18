using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminCategories
    {
        // /admin/categories
        public static void HandleListRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            List<obCategory> cats = CategoryManager.GetAll();

            // Count posts per category id for a quick overview.
            var counts = new Dictionary<int, int>();
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var rows = s.GetObjectList<obSetting>(
                        "SELECT category_id AS skey, COUNT(*) AS svalue FROM posts WHERE category_id <> 0 AND is_deleted=0 GROUP BY category_id;");
                    foreach (var r in rows)
                    {
                        int cid = 0; int.TryParse(r.Skey + "", out cid);
                        int c = 0; int.TryParse(r.Svalue + "", out c);
                        if (cid > 0) counts[cid] = c;
                    }
                }
            }

            var tpl = new AdminTemplate
            {
                Title = "Categories",
                ActiveItem = "categories",
                PageHeading = "Categories",
                PageHeadingActionsHtml = "<a href='/admin/categories/new' class='btn btn-primary btn-sm'><i class='fa-solid fa-plus'></i> New category</a>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            if (cats.Count == 0)
            {
                sb.Append(@"
<div class='empty-card'>
    <i class='fa-solid fa-tags empty-icon'></i>
    <h2>No categories yet</h2>
    <p>Categories group your posts and power the category pages.</p>
    <a href='/admin/categories/new' class='btn btn-primary'><i class='fa-solid fa-plus'></i> Create your first category</a>
</div>
");
            }
            else
            {
                sb.Append(@"
<div class='data-table-wrap'>
<table class='data-table'>
    <thead>
        <tr>
            <th class='col-narrow'>Order</th>
            <th>Name</th>
            <th>Slug</th>
            <th>Description</th>
            <th class='col-narrow'>Posts</th>
            <th class='col-actions'></th>
        </tr>
    </thead>
    <tbody>
");
                foreach (var c in cats)
                {
                    int postCount = counts.ContainsKey(c.Id) ? counts[c.Id] : 0;
                    string desc = string.IsNullOrEmpty(c.Description)
                        ? "<span class='text-muted'>-</span>" : HttpUtility.HtmlEncode(c.Description);
                    sb.Append($@"
        <tr>
            <td class='text-muted'>{c.SortOrder}</td>
            <td><a href='/admin/categories/edit/{c.Id}' class='row-title'>{HttpUtility.HtmlEncode(c.Name)}</a></td>
            <td><code>/category/{HttpUtility.HtmlEncode(c.Slug)}</code></td>
            <td>{desc}</td>
            <td class='text-muted'>{postCount}</td>
            <td class='col-actions'>
                <a href='/category/{HttpUtility.HtmlAttributeEncode(c.Slug)}' target='_blank' class='icon-btn' title='View'><i class='fa-solid fa-arrow-up-right-from-square'></i></a>
                <a href='/admin/categories/edit/{c.Id}' class='icon-btn' title='Edit'><i class='fa-solid fa-pen'></i></a>
                <button type='button' class='icon-btn icon-btn-danger' onclick='deleteCategory({c.Id})' title='Delete'><i class='fa-solid fa-trash'></i></button>
            </td>
        </tr>");
                }
                sb.Append(@"
    </tbody>
</table>
</div>
<script>
async function deleteCategory(id) {
    if (!confirm('Delete this category? Posts keep their category label; only this entry is removed.')) return;
    var fd = new FormData();
    fd.append('action', 'delete');
    fd.append('id', id);
    var r = await fetch('/api/admin/categories', { method: 'POST', body: fd });
    var d = await r.json();
    if (d.success) flashGoodAndReload('Deleted', 'Category deleted.');
    else showErrorMessage('Delete failed', d.message);
}
</script>
");
            }

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        // /admin/categories/new  (id = 0)  and  /admin/categories/edit/{id}
        public static void HandleEditRequest(int id)
        {
            if (!AdminGuard.RequireLogin()) return;

            obCategory cat = new obCategory();
            if (id > 0)
            {
                var got = CategoryManager.GetById(id);
                if (got == null || got.Id == 0)
                {
                    AppSession.SetFlash("Category not found");
                    ApiHelper.Redirect("/admin/categories");
                    return;
                }
                cat = got;
            }

            string heading = (id > 0 ? "Edit category" : "New category");

            var tpl = new AdminTemplate
            {
                Title = heading,
                ActiveItem = "categories",
                PageHeading = heading,
                PageHeadingActionsHtml = "<a href='/admin/categories' class='btn btn-ghost btn-sm'><i class='fa-solid fa-arrow-left'></i> All categories</a>",
                ExtraFooterText = "<script src='/js/media-browser.js'></script>\n"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            string name = HttpUtility.HtmlAttributeEncode(cat.Name);
            string slug = HttpUtility.HtmlAttributeEncode(cat.Slug);
            string description = HttpUtility.HtmlAttributeEncode(cat.Description);
            string cover = HttpUtility.HtmlAttributeEncode(cat.CoverImage);
            string coverPreviewStyle = string.IsNullOrEmpty(cat.CoverImage) ? "display:none" : "";

            sb.Append($@"
<form id='categoryForm' onsubmit='return saveCategory(event)' class='editor-form'>
    <input type='hidden' name='id' value='{cat.Id}' />
    <div class='card' style='max-width:680px;'>
        <div class='card-body'>
            <div class='form-field'>
                <label for='name'>Name</label>
                <input type='text' id='name' name='name' value='{name}' maxlength='120' placeholder='e.g. Design' required />
            </div>
            <div class='form-field'>
                <label for='description'>Short description</label>
                <input type='text' id='description' name='description' value='{description}' maxlength='240' placeholder='One line shown on the category page (optional)' />
            </div>
            <div class='form-field'>
                <label for='slug'>Slug</label>
                <input type='text' id='slug' name='slug' value='{slug}' maxlength='64' pattern='[a-zA-Z0-9_-]+' placeholder='auto-from-name' />
                <small class='form-hint'>URL: /category/<span id='slugPreview'>{HttpUtility.HtmlEncode(cat.Slug)}</span></small>
            </div>
            <div class='form-field'>
                <label for='sort_order'>Display sequence</label>
                <input type='number' id='sort_order' name='sort_order' value='{cat.SortOrder}' />
                <small class='form-hint'>Lower numbers appear first.</small>
            </div>
            <div class='form-field'>
                <label for='cover_image'>Cover image</label>
                <div class='cover-preview' id='coverPreview' style='{coverPreviewStyle};max-width:280px;margin-bottom:10px;'>
                    <img src='{cover}' alt='' style='max-width:100%;border-radius:8px;' />
                </div>
                <input type='text' id='cover_image' name='cover_image' value='{cover}' placeholder='/uploads/photo.jpg' />
                <button type='button' class='btn btn-ghost btn-sm' style='margin-top:8px;' onclick='pickCover()'><i class='fa-solid fa-image'></i> Choose image</button>
            </div>
        </div>
    </div>
    <div class='side-actions' style='max-width:680px;margin-top:16px;'>
        <button type='submit' class='btn btn-primary'><i class='fa-solid fa-floppy-disk'></i> Save category</button>
        <a href='/admin/categories' class='btn btn-ghost'>Cancel</a>
    </div>
</form>

<script>
(function() {{
    var nameEl = document.getElementById('name');
    var slugEl = document.getElementById('slug');
    var slugPreviewEl = document.getElementById('slugPreview');

    function slugify(s) {{
        return s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
    }}
    nameEl.addEventListener('input', function() {{
        if (!slugEl.dataset.touched) {{
            var v = slugify(this.value);
            slugEl.value = v;
            if (slugPreviewEl) slugPreviewEl.textContent = v;
        }}
    }});
    slugEl.addEventListener('input', function() {{
        this.dataset.touched = '1';
        var v = this.value.replace(/[^a-zA-Z0-9_-]+/g, '');
        if (v !== this.value) this.value = v;
        if (slugPreviewEl) slugPreviewEl.textContent = v;
    }});

    var coverInput = document.getElementById('cover_image');
    coverInput.addEventListener('input', function() {{
        var v = this.value.trim();
        var box = document.getElementById('coverPreview');
        if (v) {{ box.style.display = ''; box.querySelector('img').src = v; }}
        else {{ box.style.display = 'none'; }}
    }});
    window.pickCover = async function() {{
        if (window.mediaBrowser && typeof window.mediaBrowser.pick === 'function') {{
            try {{
                var picked = await window.mediaBrowser.pick({{ accept: ['image/*'] }});
                if (picked) {{ coverInput.value = picked.trim(); coverInput.dispatchEvent(new Event('input')); }}
            }} catch (e) {{ console.error('Media picker error:', e); }}
        }} else {{
            var url = prompt('Paste an image URL (e.g. /uploads/photo.jpg):');
            if (url) {{ coverInput.value = url.trim(); coverInput.dispatchEvent(new Event('input')); }}
        }}
    }};

    window.saveCategory = async function(e) {{
        e.preventDefault();
        var fd = new FormData(document.getElementById('categoryForm'));
        fd.append('action', 'save');
        try {{
            var r = await fetch('/api/admin/categories', {{ method: 'POST', body: fd }});
            var d = await r.json();
            if (d.success) {{
                flashGoodAndGo('Saved', 'Category saved.', '/admin/categories');
            }} else {{
                showErrorMessage('Save failed', d.message);
            }}
        }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
        return false;
    }};
}})();
</script>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }
    }
}

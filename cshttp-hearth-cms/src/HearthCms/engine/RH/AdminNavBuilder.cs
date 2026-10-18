using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;
using Newtonsoft.Json;
using System.engine;

namespace System.engine.RH
{
    // ============================================================
    // Navigation menu builder
    // ------------------------------------------------------------
    //   /admin/nav        -> AdminNavBuilder.HandleRequest()    (UI)
    //   /api/admin/nav    -> AdminNavBuilder.HandleApiRequest() (load/save)
    //
    // The menu is stored as a JSON string in the `nav_menu` setting.
    // Two levels are supported: top-level items each with an optional
    // list of children. Anything deeper is flattened away on save by
    // Normalize(), so the public renderer never has to guard for it.
    //
    // Public rendering lives in NavMenu.RenderPublicNav() (now in
    // System.engine, file engine/NavMenu.cs), which is called from
    // PublicTemplate. When no custom menu has been saved it falls back
    // to a sensible default (Home, and any published pages flagged
    // show_in_nav) so existing sites keep their navigation untouched.
    // ============================================================

    // ===== Admin UI + API =====
    public static class AdminNavBuilder
    {
        // ---------- Page : /admin/nav ----------
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            string menuJson = NavMenu.LoadJsonForBuilder();
            string quickJson = BuildQuickAddJson();

            var tpl = new AdminTemplate
            {
                Title = "Navigation",
                ActiveItem = "nav",
                PageHeading = "Navigation menu",
                PageHeadingActionsHtml =
                    "<button type='button' class='btn btn-primary btn-sm' onclick='saveNav()'>"
                    + "<i class='fa-solid fa-floppy-disk'></i> Save menu</button>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            sb.Append(@"
<div class='card'>
    <div class='card-header'><h2><i class='fa-solid fa-bars-staggered'></i> Main navigation</h2></div>
    <div class='card-body'>
        <p class='form-hint' style='margin:0 0 18px'>Build the menu shown in the public site header. Drag-free ordering with the arrows; up to two levels (a top item plus its sub-items). Leave a parent's URL empty to make it a label that only opens its sub-menu.</p>
        <div class='nav-builder-toolbar'>
            <button type='button' class='btn btn-ghost btn-sm' onclick='navAddTop()'><i class='fa-solid fa-plus'></i> Add item</button>
            <span class='nav-builder-quick'>
                <select id='navQuickSelect' class='nav-quick-select'></select>
                <button type='button' class='btn btn-ghost btn-sm' onclick='navAddQuick()'><i class='fa-solid fa-link'></i> Add link</button>
            </span>
        </div>
        <div id='navBuilderRoot' class='nav-builder'></div>
    </div>
</div>
");

            // Hand the initial data to the external builder script.
            tpl.ExtraFooterText =
                "<script>window.__navMenu = " + menuJson + "; window.__navQuick = " + quickJson + ";</script>\n"
                + "<script src='/js/admin-nav-builder.js'></script>";

            sb.Append(tpl.RenderFooter());

            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        // Common link targets offered in the quick-add dropdown.
        static string BuildQuickAddJson()
        {
            var quick = new List<object>
            {
                new { label = "Home", url = "/" },
                new { label = "Latest posts", url = "/latest-post" },
                new { label = "Categories", url = "/categories-latest-post" }
            };
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var lst = s.GetObjectList<obPage>(
                            "SELECT id, slug, title FROM pages WHERE is_published=1 AND is_deleted=0 ORDER BY title;");
                        foreach (var p in lst)
                            quick.Add(new { label = p.Title, url = "/" + p.Slug });
                    }
                }
            }
            catch { }
            return JsonConvert.SerializeObject(quick);
        }

        // ---------- API : /api/admin/nav ----------
        public static void HandleApiRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "load": Load(); break;
                    case "save": Save(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Load()
        {
            ApiHelper.WriteSuccess("OK", new { menu = NavMenu.Load() });
        }

        static void Save()
        {
            var req = HttpContext.Current.Request;
            string menuJson = req.Form["menu"] ?? "";

            List<NavNode> parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<List<NavNode>>(menuJson) ?? new List<NavNode>();
            }
            catch
            {
                ApiHelper.WriteError("Could not read the menu data.");
                return;
            }

            var clean = NavMenu.Normalize(parsed);
            Db.SaveSetting("nav_menu", JsonConvert.SerializeObject(clean));

            // Nav appears on every page, so wipe the whole public cache.
            PublicPageCache.InvalidateAll();

            ApiHelper.WriteSuccess("Menu saved");
        }
    }
}
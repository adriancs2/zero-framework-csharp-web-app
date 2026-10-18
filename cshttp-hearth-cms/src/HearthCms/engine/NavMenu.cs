using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;
using Newtonsoft.Json;

namespace System.engine
{
    // Public navigation menu: load/normalize the admin-built menu (stored as the
    // "nav_menu" setting) and render the public <nav> inner markup. When no menu
    // has been saved it falls back to a sensible default (Home, any
    // published pages flagged show_in_nav) so existing sites keep their nav.
    //
    // Public rendering (RenderPublicNav) is called by PublicTemplate (HTML themes,
    // via the {{nav_items}} token) and by the C# themes' Layout. The admin builder
    // UI/API lives in System.engine.RH.AdminNavBuilder.
    public static class NavMenu
    {
        const string SettingKey = "nav_menu";

        // ----- Load -----

        // Parsed, normalized menu. Falls back to the default when nothing
        // is stored or the stored value can't be parsed.
        public static List<NavNode> Load()
        {
            string raw = Db.GetSetting(SettingKey, "");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var parsed = JsonConvert.DeserializeObject<List<NavNode>>(raw);
                    if (parsed != null && parsed.Count > 0) return Normalize(parsed);
                }
                catch { /* fall through to default */ }
            }
            return DefaultMenu();
        }

        // JSON the builder UI loads. Always returns a populated menu
        // (the default) on first use so the admin has something to edit.
        public static string LoadJsonForBuilder()
        {
            return JsonConvert.SerializeObject(Load());
        }

        // The legacy/default menu: Home, then published nav pages.
        public static List<NavNode> DefaultMenu()
        {
            var list = new List<NavNode>
            {
                new NavNode { Label = "Home", Url = "/", Children = new List<NavNode>() },
                new NavNode { Label = "Latest Posts", Url = "/latest-post", Children = new List<NavNode>() }
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
                            "SELECT id, slug, title FROM pages WHERE is_published=1 AND is_deleted=0 AND show_in_nav=1 ORDER BY sort_order, title;");
                        foreach (var p in lst)
                        {
                            list.Add(new NavNode
                            {
                                Label = p.Title,
                                Url = "/" + p.Slug,
                                Children = new List<NavNode>()
                            });
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        // ----- Normalize / validate -----

        // Trims values, drops empties, and enforces the two-level limit by
        // discarding any grandchildren. Safe to run on both stored and
        // freshly-posted data.
        public static List<NavNode> Normalize(List<NavNode> input)
        {
            var outList = new List<NavNode>();
            if (input == null) return outList;

            foreach (var n in input)
            {
                if (n == null) continue;

                string label = (n.Label ?? "").Trim();
                string url = (n.Url ?? "").Trim();
                bool hasKids = n.Children != null && n.Children.Count > 0;

                // A top node needs either a label or some children to exist.
                if (string.IsNullOrEmpty(label) && !hasKids) continue;

                var node = new NavNode
                {
                    Label = label,
                    Url = url,
                    Children = new List<NavNode>()
                };

                if (hasKids)
                {
                    foreach (var c in n.Children)
                    {
                        if (c == null) continue;
                        string cl = (c.Label ?? "").Trim();
                        string cu = (c.Url ?? "").Trim();
                        if (string.IsNullOrEmpty(cl) && string.IsNullOrEmpty(cu)) continue;
                        // Level 2 only: children never carry their own children.
                        node.Children.Add(new NavNode { Label = cl, Url = cu, Children = null });
                    }
                }

                outList.Add(node);
            }
            return outList;
        }

        // ----- Public rendering (called by PublicTemplate) -----

        // Emits the inner markup for <nav class='site-nav'>. Top-level items
        // are plain <a>; items with children become a .nav-group with a
        // .nav-parent trigger and a .nav-submenu dropdown.
        public static string RenderPublicNav()
        {
            var items = Load();
            var sb = new StringBuilder();

            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                bool hasChildren = n.Children != null && n.Children.Count > 0;
                string label = HttpUtility.HtmlEncode(n.Label ?? "");
                string url = (n.Url ?? "").Trim();

                if (!hasChildren)
                {
                    if (string.IsNullOrEmpty(url)) continue; // skip a label-only leaf
                    if (i == 0)
                    {
                        sb.Append($"<a href='{HttpUtility.HtmlAttributeEncode(url)}'>{label}</a>");
                    }
                    else
                    {
                        sb.Append($"            <a href='{HttpUtility.HtmlAttributeEncode(url)}'>{label}</a>");
                    }

                    if (i + 1 < items.Count)
                        sb.Append("\n");

                    continue;
                }

                sb.Append("            <div class='nav-group'>\n");
                if (string.IsNullOrEmpty(url))
                {
                    // Parent acts purely as a submenu trigger.
                    sb.Append("                <span class='nav-parent' tabindex='0' role='button' aria-haspopup='true'>"
                        + label + " <i class='fa-solid fa-chevron-down nav-caret'></i></span>\n");
                }
                else
                {
                    sb.Append("                <a class='nav-parent' href='" + HttpUtility.HtmlAttributeEncode(url)
                        + "' aria-haspopup='true'>" + label + " <i class='fa-solid fa-chevron-down nav-caret'></i></a>\n");
                }

                sb.Append("                <div class='nav-submenu'>\n");
                foreach (var c in n.Children)
                {
                    string cu = (c.Url ?? "").Trim();
                    if (string.IsNullOrEmpty(cu)) continue;
                    sb.Append("                    <a href='" + HttpUtility.HtmlAttributeEncode(cu) + "'>"
                        + HttpUtility.HtmlEncode(c.Label ?? "") + "</a>\n");
                }
                sb.Append("                </div>\n");
                sb.Append("            </div>\n");
            }

            return sb.ToString();
        }
    }
}

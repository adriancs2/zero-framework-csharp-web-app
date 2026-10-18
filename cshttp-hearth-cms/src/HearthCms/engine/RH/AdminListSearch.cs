using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // Shared search box for the Pages and Posts list screens. A plain
    // GET form that submits ?q=... back to the same list route, so the
    // result page is bookmarkable and works without JavaScript. The
    // active trash filter (?filter=deleted) is preserved as a hidden
    // field so a search stays within the current Active/Deleted view.
    //
    // The list handlers own the SQL (a title/slug/excerpt/content LIKE);
    // this class only renders the form and the result-count line, so the
    // markup stays identical across both screens.
    // ============================================================
    internal static class AdminListSearch
    {
        // The current search term from ?q=..., trimmed.
        public static string Term()
        {
            return (HttpContext.Current.Request.QueryString["q"] + "").Trim();
        }

        // The GET search form. baseUrl is the list route ("/admin/posts").
        public static string SearchBar(string baseUrl, string q, bool showDeleted, string placeholder)
        {
            string action = HttpUtility.HtmlAttributeEncode(baseUrl);
            string val = HttpUtility.HtmlAttributeEncode(q);
            string ph = HttpUtility.HtmlAttributeEncode(placeholder);
            string hidden = showDeleted
                ? "<input type='hidden' name='filter' value='deleted' />"
                : "";
            string clearHref = HttpUtility.HtmlAttributeEncode(baseUrl + (showDeleted ? "?filter=deleted" : ""));
            string clear = string.IsNullOrEmpty(q)
                ? ""
                : "<a class='list-search-clear' href='" + clearHref + "'>Clear</a>";

            return $@"
<form class='list-search' method='get' action='{action}' role='search'>
    {hidden}
    <i class='fa-solid fa-magnifying-glass list-search-ico'></i>
    <input type='search' name='q' class='list-search-input' value='{val}' placeholder='{ph}' aria-label='{ph}' autocomplete='off' />
    {clear}
    <button type='submit' class='btn btn-primary btn-sm'>Search</button>
</form>
";
        }

        // The "N result(s) for ..." line shown above the table when searching.
        public static string ResultMeta(int count, string q)
        {
            return "<p class='list-search-meta'>" + count + " result(s) for &ldquo;" +
                HttpUtility.HtmlEncode(q) + "&rdquo;</p>";
        }

        // ===== Pagination =====

        // Requested page size, resolved entirely server-side so a plain GET
        // navigation renders the list with the input already filled in:
        //   explicit ?per=  >  remembered cookie  >  the default.
        // An explicit ?per= is persisted to a cookie, so later visits without the
        // param (e.g. clicking Posts in the sidebar) still render at the chosen
        // size without any client-side redirect. Clamped to a sane 5..500 range.
        const string PerPageCookie = "hearth_admin_per_page";

        public static int PerPage(int def = 50)
        {
            HttpContext ctx = HttpContext.Current;
            int n;

            string raw = (ctx.Request.QueryString["per"] + "").Trim();
            if (raw.Length > 0 && int.TryParse(raw, out n))
            {
                n = Clamp(n);
                SetPerPageCookie(ctx, n);   // explicit choice -> remember it
                return n;
            }

            HttpCookie ck = ctx.Request.Cookies[PerPageCookie];
            if (ck != null && int.TryParse((ck.Value + "").Trim(), out n))
                return Clamp(n);

            return Clamp(def);
        }

        static int Clamp(int n)
        {
            if (n < 5) n = 5;
            if (n > 500) n = 500;
            return n;
        }

        static void SetPerPageCookie(HttpContext ctx, int n)
        {
            HttpCookie c = new HttpCookie(PerPageCookie, n.ToString());
            c.Path = "/";
            c.HttpOnly = true;
            c.Expires = DateTime.Now.AddYears(1);
            ctx.Response.Cookies.Set(c);
        }

        // Current 1-based page from ?page= (defaults to 1, never < 1).
        public static int PageNum()
        {
            int n;
            if (!int.TryParse((HttpContext.Current.Request.QueryString["page"] + ""), out n)) n = 1;
            if (n < 1) n = 1;
            return n;
        }

        // Page count for a row total at a given page size (always >= 1).
        public static int TotalPages(int total, int perPage)
        {
            if (perPage < 1) perPage = 1;
            if (total <= 0) return 1;
            return (total + perPage - 1) / perPage;
        }

        // A pagination bar: a "x-y of N" range, the numbered page links, and the
        // "Rows per page" input (a GET form that reloads the list at the new size).
        // Rendered both above and below the table (top:true / top:false) so the
        // controls are reachable without scrolling either way. Shown whenever the
        // current view has rows; the page links collapse to nothing on a single
        // page, leaving just the range and the size input. The input carries no
        // id (its label wraps it) so the two bars don't clash on the page.
        public static string PaginationBar(string baseUrl, string q, bool showDeleted,
            int perPage, int currentPage, int totalPages, int totalCount, bool top)
        {
            int start = totalCount <= 0 ? 0 : (currentPage - 1) * perPage + 1;
            int end = currentPage * perPage;
            if (end > totalCount) end = totalCount;

            string action = HttpUtility.HtmlAttributeEncode(baseUrl);
            string hiddenFilter = showDeleted ? "<input type='hidden' name='filter' value='deleted' />" : "";
            string hiddenQ = string.IsNullOrEmpty(q)
                ? ""
                : "<input type='hidden' name='q' value='" + HttpUtility.HtmlAttributeEncode(q) + "' />";
            string pages = PageLinks(baseUrl, q, showDeleted, perPage, currentPage, totalPages);
            string info = start + "–" + end + " of " + totalCount;
            string pos = top ? "is-top" : "is-bottom";

            return $@"
<div class='list-foot {pos}'>
    <div class='list-foot-info'>{info}</div>
    <div class='list-foot-pages'>{pages}</div>
    <form class='per-page' method='get' action='{action}'>
        {hiddenFilter}{hiddenQ}
        <label class='per-page-label'>Rows per page
            <input type='number' name='per' min='5' max='500' step='5' value='{perPage}' class='per-page-input' onchange='this.form.submit()' />
        </label>
        <noscript><button type='submit' class='btn btn-ghost btn-sm'>Apply</button></noscript>
    </form>
</div>
";
        }

        // Prev / windowed-numbers / next links. Empty for a single page.
        static string PageLinks(string baseUrl, string q, bool showDeleted, int perPage, int currentPage, int totalPages)
        {
            if (totalPages <= 1) return "";

            var sb = new StringBuilder();
            sb.Append(PageLink(baseUrl, q, showDeleted, perPage, currentPage - 1,
                "<i class='fa-solid fa-chevron-left'></i>", currentPage <= 1, false));

            int last = 0;
            for (int i = 1; i <= totalPages; i++)
            {
                bool show = i <= 1 || i > totalPages - 1 || (i >= currentPage - 2 && i <= currentPage + 2);
                if (!show) continue;
                if (last > 0 && i - last > 1) sb.Append("<span class='list-page-gap'>…</span>");
                sb.Append(PageLink(baseUrl, q, showDeleted, perPage, i, i.ToString(), false, i == currentPage));
                last = i;
            }

            sb.Append(PageLink(baseUrl, q, showDeleted, perPage, currentPage + 1,
                "<i class='fa-solid fa-chevron-right'></i>", currentPage >= totalPages, false));
            return sb.ToString();
        }

        // One page control: a link, or an inert span when active or disabled.
        static string PageLink(string baseUrl, string q, bool showDeleted, int perPage, int page,
            string label, bool disabled, bool active)
        {
            string cls = "list-page";
            if (active) cls += " is-active";
            if (disabled) cls += " is-disabled";
            if (active || disabled)
                return "<span class='" + cls + "'>" + label + "</span>";
            string href = HttpUtility.HtmlAttributeEncode(PageUrl(baseUrl, q, showDeleted, perPage, page));
            return "<a class='" + cls + "' href='" + href + "'>" + label + "</a>";
        }

        // Build a list URL preserving the trash filter, search term and page size.
        // Page 1 omits the page param so the canonical URL stays clean.
        static string PageUrl(string baseUrl, string q, bool showDeleted, int perPage, int page)
        {
            var sb = new StringBuilder(baseUrl);
            char sep = '?';
            if (showDeleted) { sb.Append(sep).Append("filter=deleted"); sep = '&'; }
            if (!string.IsNullOrEmpty(q)) { sb.Append(sep).Append("q=").Append(HttpUtility.UrlEncode(q)); sep = '&'; }
            sb.Append(sep).Append("per=").Append(perPage); sep = '&';
            if (page > 1) sb.Append(sep).Append("page=").Append(page);
            return sb.ToString();
        }

    }
}

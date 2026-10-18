using System.Collections.Generic;
using System.Text;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Hearth
{
    // ============================================================
    // Hearth (C#) - the reference code-rendered theme.
    //
    // Lives in /engine/CsTemplate/Themes/ and is compiled into the main
    // assembly. The code keeps to a conservative C# style (string.Format /
    // concat instead of interpolation) for consistency across the theme set.
    //
    // Demonstrates the CsTemplate model: the home page is built imperatively
    // (query posts via the CsTemplate data helpers, for-loop the cards,
    // StringBuilder + string.Format) instead of token-replacing an HTML file.
    //
    // Activate by setting active_theme = "hearth-cs".
    // Its CSS/JS/images live in /assets/themes/hearth-cs/ like any theme.
    // ============================================================
    public partial class Hearth : CsTemplate
    {
        public override string Slug { get { return "hearth-cs"; } }
        public override string Name { get { return "Hearth (C#)"; } }
        public override string Description
        {
            get { return "The default theme. A warm editorial style featuring cream paper, an ember accent, and Fraunces + Inter typography."; }
        }
        public override string Author { get { return "Hearth CMS"; } }
        public override string Url { get { return "Github Hearth CMS"; } }
        public override string Version { get { return "1"; } }

        // Stylesheet the live <head> links (see Layout.RenderHeader) - exposed so
        // the admin Markdown preview loads the same CSS instead of the folder-theme
        // default (/assets/themes/{slug}/style.css), which this theme does not ship.
        public override string CssHref { get { return AssetBase + "/hearth.css"; } }

        // Shared layout factory: title suffixed with the site name, plus the
        // theme's asset base so CSS/JS resolve to /assets/themes/hearth-cs/.
        Layout NewLayout(string pageTitle)
        {
            string site = GetSiteName();
            string title = string.IsNullOrEmpty(pageTitle) ? site : pageTitle + " - " + site;
            // Default the meta description to the site's SEO Description (admin
            // Settings -> "Description (SEO)") so listing/home/page routes are not
            // emitted with an empty <meta name='description'>. Handlers with a more
            // specific description (e.g. a post's excerpt) override it afterwards.
            return new Layout { Title = title, MetaDescription = GetSiteDescription(), AssetBaseUrl = AssetBase };
        }
    }
}

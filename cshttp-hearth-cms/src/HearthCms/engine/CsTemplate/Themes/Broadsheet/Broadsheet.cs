using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Broadsheet
{
    // ============================================================
    // Broadsheet (C#) - code-rendered port of the Broadsheet folder theme.
    //
    // Lives in /engine/CsTemplate/Themes/ and is compiled into the main
    // assembly. The code keeps to a conservative C# style (string.Format /
    // concat instead of interpolation) for consistency across the theme set.
    //
    // A newspaper-style editorial theme: deep navy masthead, a bright blue
    // accent, condensed Ubuntu headlines over Noto Sans body. Where the folder
    // theme (App_Data/themes/Broadsheet) replaces {{tokens}} in HTML files,
    // this version emits each page imperatively in C# - structurally in
    // lock-step with those templates, so the two render identically.
    //
    // Activate by setting active_theme = "broadsheet-cs". It reuses the existing
    // Broadsheet asset folder (/assets/themes/Broadsheet/) for CSS/JS/images, so
    // no asset duplication is needed.
    // ============================================================
    public partial class Broadsheet : CsTemplate
    {
        public override string Slug { get { return "broadsheet-cs"; } }
        public override string Name { get { return "Broadsheet (C#)"; } }
        public override string Description
        {
            get { return "Code-rendered port of the Broadsheet editorial theme: deep navy masthead, bright blue accent, condensed Ubuntu headlines over Noto Sans body - built for long technical writing, emitted directly in C# instead of HTML token templates."; }
        }
        public override string Author { get { return "Hearth CMS"; } }
        public override string Url { get { return "Github Hearth CMS"; } }
        public override string Version { get { return "1"; } }

        // The Broadsheet (C#) theme renders with the existing Broadsheet folder
        // theme's assets rather than a slug-named folder, so CSS/JS/images resolve
        // to /assets/themes/Broadsheet/ exactly like the folder theme's _layout.html.
        const string BroadsheetAssetBase = "/assets/themes/Broadsheet-cs";

        // Stylesheet the live <head> links (see Layout.RenderHeader) - exposed so
        // the admin Markdown preview loads the same CSS instead of the folder-theme
        // default (/assets/themes/{slug}/style.css), which this theme does not ship.
        public override string CssHref { get { return BroadsheetAssetBase + "/broadsheet-cs.css?v=1"; } }

        // Shared layout factory: title suffixed with the site name, plus the
        // shared Broadsheet asset base so CSS/JS resolve correctly.
        Layout NewLayout(string pageTitle)
        {
            string site = GetSiteName();
            string title = string.IsNullOrEmpty(pageTitle) ? site : pageTitle + " - " + site;
            // Default the meta description to the site's SEO Description (admin
            // Settings -> "Description (SEO)") so listing/home/page routes are not
            // emitted with an empty <meta name='description'>. Handlers with a more
            // specific description (e.g. a post's excerpt) override it afterwards.
            return new Layout { Title = title, MetaDescription = GetSiteDescription(), AssetBaseUrl = BroadsheetAssetBase };
        }
    }
}

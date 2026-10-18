using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Hearth
{
    public partial class Hearth : CsTemplate
    {
        // ----- 404 : rendered inside the Hearth (C#) shell -----
        //
        // Overrides the engine default (NotFoundPage), which renders the active
        // FOLDER theme's 404.html via PublicTemplate.RenderPage - i.e. the wrong
        // shell for a C# theme. Here we emit the Hearth header/footer around a
        // body kept in lock-step with App_Data/themes/hearth/404.html.
        //
        // NOTE: a 404 must NOT go through WriteCached(). An unknown single-segment
        // URL ("/does-not-exist") is a cacheable root-content path, so caching the
        // error body would poison the cache (and could later shadow a real slug).
        // We mirror NotFoundPage: set status 404 and write straight to ApiHelper.
        public override void HandleNotFound()
        {
            HttpContext.Current.Response.StatusCode = 404;

            var layout = NewLayout("Page not found");

            string siteName = H(GetSiteName());
            string body = string.Format(@"
<section class='section'>
    <div class='container container-narrow text-center'>
        <div class='error-code'>404</div>
        <h1>Page not found</h1>
        <p class='error-sub'>Sorry &mdash; the page you were looking for on {0} doesn't exist, or it may have been moved or removed.</p>
        <p class='error-actions' style='line-height: 300%;'>
            <a href='/' class='btn btn-primary'><i class='fa-solid fa-house'></i> Back to home</a>
            <a href='/latest-post' class='btn btn-ghost'><i class='fa-solid fa-newspaper'></i> Browse latest posts</a>
        </p>
    </div>
</section>
", siteName);

            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());
            sb.Append(body);
            sb.Append(layout.RenderFooter());

            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }
    }
}

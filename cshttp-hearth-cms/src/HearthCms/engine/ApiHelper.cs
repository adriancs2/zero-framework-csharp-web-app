using Newtonsoft.Json;
using System.Web;
using CsHttp.Web;

namespace System
{
    public static class ApiHelper
    {
        static HttpRequest Request
        {
            get
            {
                if (HttpContext.Current == null)
                    throw new InvalidOperationException("ApiHelper called outside an HTTP request.");
                return HttpContext.Current.Request;
            }
        }

        static HttpResponse Response
        {
            get
            {
                if (HttpContext.Current == null)
                    throw new InvalidOperationException("ApiHelper called outside an HTTP request.");
                return HttpContext.Current.Response;
            }
        }

        public static string GetBaseUrl()
        {
            Uri url = Request.Url;
            return string.Format("{0}://{1}{2}",
                url.Scheme, url.Host,
                url.IsDefaultPort ? "" : ":" + url.Port);
        }

        public static void EndResponse()
        {
            Response.TrySkipIisCustomErrors = true;
            try { Response.Flush(); } catch { }
            Response.SuppressContent = true;
            HttpContext.Current.ApplicationInstance.CompleteRequest();
        }

        public static void WriteHtml(string html)
        {
            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(html);
        }

        public static void WriteJson(object obj)
        {
            Response.ContentType = "application/json";
            Response.Write(JsonConvert.SerializeObject(obj));
        }

        public static void WriteSuccess(string message = "Success")
        {
            WriteJson(new { success = true, message });
        }

        public static void WriteSuccess(string message, object data)
        {
            WriteJson(new { success = true, message, data });
        }

        public static void WriteError(string message, int statusCode = 400)
        {
            Response.StatusCode = statusCode;
            WriteJson(new { success = false, message });
        }

        public static void Redirect(string url)
        {
            Response.Redirect(url, false);
            HttpContext.Current.ApplicationInstance.CompleteRequest();
        }
    }
}

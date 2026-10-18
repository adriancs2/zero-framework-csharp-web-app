using System.IO;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    public static class Config
    {
        // ===== Site identity (overridden by Settings table after load) =====
        public static string SiteName = "Hearth CMS";
        public static string SiteTagline = "A clean place to write.";
        public static string AdminBrand = "CMS Admin";

        // ===== Paths =====
        public static string DbFileName = "hearth-cms.sqlite";
        public static string UploadsFolderRelative = "/uploads/";

        // ===== Computed =====
        public static string GetDbPath()
        {
            string dataDir = HttpContext.Current.Server.MapPath("~/App_Data/");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            return Path.Combine(dataDir, DbFileName);
        }

        public static string GetConnString()
        {
            return "Data Source=" + GetDbPath() + ";Version=3;Pooling=True;Max Pool Size=100;";
        }

        public static string GetUploadsFolder()
        {
            string p = HttpContext.Current.Server.MapPath("~" + UploadsFolderRelative);
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }
    }
}

using System;

namespace System.engine
{
    public class obMedia
    {
        int id = 0;
        string file_name = "";
        string original_name = "";
        string mime_type = "";
        long file_size = 0;
        int width = 0;
        int height = 0;
        int uploader_id = 0;
        DateTime date_created = DateTime.MinValue;

        public int Id { get { return id; } set { id = value; } }
        public string FileName { get { return file_name; } set { file_name = value; } }
        public string OriginalName { get { return original_name; } set { original_name = value; } }
        public string MimeType { get { return mime_type; } set { mime_type = value; } }
        public long FileSize { get { return file_size; } set { file_size = value; } }
        public int Width { get { return width; } set { width = value; } }
        public int Height { get { return height; } set { height = value; } }
        public int UploaderId { get { return uploader_id; } set { uploader_id = value; } }
        public DateTime DateCreated { get { return date_created; } set { date_created = value; } }

        // Public URL of the original file.
        //  - New uploads store a full root-relative path in file_name
        //    (e.g. "/media/2026/06/photo.jpg"); returned verbatim.
        //  - Legacy rows store just a bare file name; mapped to /uploads/.
        public string Url
        {
            get
            {
                if (string.IsNullOrEmpty(file_name)) return "";
                if (file_name[0] == '/') return file_name;
                if (file_name.IndexOf('/') >= 0) return "/" + file_name;
                return "/uploads/" + file_name;
            }
        }

        // URL of the resized thumbnail mirror if one exists on disk,
        // otherwise the original (so listings always render something).
        public string ThumbUrl { get { return ImageThumb.DisplayUrl(Url); } }
    }
}

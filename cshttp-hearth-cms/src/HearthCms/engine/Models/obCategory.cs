using System;

namespace System.engine
{
    public class obCategory
    {
        int id = 0;
        string slug = "";
        string name = "";
        string description = "";
        string cover_image = "";
        int sort_order = 0;
        DateTime date_created = DateTime.MinValue;
        DateTime date_modified = DateTime.MinValue;

        public int Id { get { return id; } set { id = value; } }
        public string Slug { get { return slug; } set { slug = value; } }
        public string Name { get { return name; } set { name = value; } }
        public string Description { get { return description; } set { description = value; } }
        public string CoverImage { get { return cover_image; } set { cover_image = value; } }
        public int SortOrder { get { return sort_order; } set { sort_order = value; } }
        public DateTime DateCreated { get { return date_created; } set { date_created = value; } }
        public DateTime DateModified { get { return date_modified; } set { date_modified = value; } }

        public string PublicUrl { get { return "/category/" + slug; } }
    }
}

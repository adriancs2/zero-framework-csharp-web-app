using System;

namespace System.engine
{
    public class obPage
    {
        int id = 0;
        string slug = "";
        string title = "";
        string content = "";
        string content_format = "html";
        string excerpt = "";
        string layout = "";
        int is_published = 0;
        int is_deleted = 0;
        int show_in_nav = 0;
        int sort_order = 0;
        int author_id = 0;
        DateTime date_created = DateTime.MinValue;
        DateTime date_modified = DateTime.MinValue;
        DateTime date_published = DateTime.MinValue;
        DateTime date_deleted = DateTime.MinValue;

        public int Id { get { return id; } set { id = value; } }
        public string Slug { get { return slug; } set { slug = value; } }
        public string Title { get { return title; } set { title = value; } }
        public string Content { get { return content; } set { content = value; } }
        public string ContentFormat { get { return content_format; } set { content_format = value; } }
        public string Excerpt { get { return excerpt; } set { excerpt = value; } }
        public string Layout { get { return layout; } set { layout = value; } }
        public int IsPublished { get { return is_published; } set { is_published = value; } }
        public int IsDeleted { get { return is_deleted; } set { is_deleted = value; } }
        public int ShowInNav { get { return show_in_nav; } set { show_in_nav = value; } }
        public int SortOrder { get { return sort_order; } set { sort_order = value; } }
        public int AuthorId { get { return author_id; } set { author_id = value; } }
        public DateTime DateCreated { get { return date_created; } set { date_created = value; } }
        public DateTime DateModified { get { return date_modified; } set { date_modified = value; } }
        public DateTime DatePublished { get { return date_published; } set { date_published = value; } }
        public DateTime DateDeleted { get { return date_deleted; } set { date_deleted = value; } }
    }
}

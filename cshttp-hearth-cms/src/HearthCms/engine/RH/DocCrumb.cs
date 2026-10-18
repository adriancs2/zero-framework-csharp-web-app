namespace System.engine.RH
{
    // One breadcrumb entry in a DocModel trail.
    public class DocCrumb
    {
        public string Label;
        public string Href;   // null/empty => rendered as current (no link)
        public DocCrumb(string label, string href = null) { Label = label; Href = href; }
    }
}

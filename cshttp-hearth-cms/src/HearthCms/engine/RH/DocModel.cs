using System;
using System.Collections.Generic;

namespace System.engine.RH
{
    // The data carried into the article renderer (DocLayout.RenderTemplated).
    // Shared by the RH page handlers (PostPage / PagePage) and the CsTemplate
    // themes, so it lives in its own file independent of any renderer.
    public class DocModel
    {
        public string Title = "";
        public int PostId = 0;                    // >0 for a post (enables the markdown toggle); 0 for a page
        public string Layout = "split";          // "split" | "stack"
        public bool ShowAside = true;            // false for pages
        public string CoverImage = "";           // may be empty
        public string RenderedContentHtml = "";  // already-rendered (markdown->html or passthrough)
        public DateTime? PublishedDate = null;   // null => no date meta
        public DateTime? UpdatedDate = null;     // null => no "updated" meta
        public string Author = "";               // empty => no author meta
        public string AsideHeading = "Recent posts";
        public List<DocCrumb> Breadcrumbs = new List<DocCrumb>();
        public List<DocRecentItem> Recent = new List<DocRecentItem>();
    }
}

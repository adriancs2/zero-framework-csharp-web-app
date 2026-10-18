using System.Collections.Generic;

namespace System.engine
{
    // A category together with its most recent posts. Returned by
    // CsTemplate.GetAllCategoriesRecentPost() so C# themes can render
    // per-category sections without touching SQLite directly.
    public class obCategoryPost
    {
        public obCategory Category;
        public List<obPost> Posts = new List<obPost>();
    }
}

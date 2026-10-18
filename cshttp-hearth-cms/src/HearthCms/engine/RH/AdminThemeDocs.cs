namespace System.engine.RH
{
    // /admin/themes/docs - HTML (token-based) theme authoring guide.
    // Content: /App_Data/AdminDocumentation/ThemeAuthoringGuide.md, rendered by
    // AdminDocRenderer.
    public static class AdminThemeDocs
    {
        public static void HandleRequest()
        {
            AdminDocRenderer.Render(
                "ThemeAuthoringGuide.md",
                "Theme guide",
                "Theme authoring guide",
                "themes",
                null,
                null,
                "<a href='/admin/themes' class='btn btn-ghost btn-sm'><i class='fa-solid fa-arrow-left'></i> Back to Themes</a>");
        }
    }
}

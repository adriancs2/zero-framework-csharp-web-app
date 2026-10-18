namespace System.engine.RH
{
    // /admin/themes/docs-csharp - C# (code-rendered) theme authoring guide.
    // Content: /App_Data/AdminDocumentation/CSharpTemplateGuide.md, rendered by
    // AdminDocRenderer.
    public static class AdminThemeDocsCSharp
    {
        public static void HandleRequest()
        {
            AdminDocRenderer.Render(
                "CSharpTemplateGuide.md",
                "C# Template guide",
                "C# Template authoring guide",
                "themes",
                null,
                null,
                "<a href='/admin/themes' class='btn btn-ghost btn-sm'><i class='fa-solid fa-arrow-left'></i> Back to Themes</a>");
        }
    }
}

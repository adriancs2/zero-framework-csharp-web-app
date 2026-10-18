namespace System.engine.RH
{
    // /admin/markdown-docs - Markdown syntax & rendering reference.
    // Content lives in /App_Data/AdminDocumentation/MarkdownReference.md and is
    // rendered by Hearth's own MarkdownToHtml engine (dogfooding the parser the
    // page documents). See AdminDocRenderer.
    public static class AdminMarkdownDocs
    {
        public static void HandleRequest()
        {
            AdminDocRenderer.Render(
                "MarkdownReference.md",
                "Markdown reference",
                "Markdown syntax and rendering",
                "guidelines");
        }
    }
}

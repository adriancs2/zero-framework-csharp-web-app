using System.Collections.Generic;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // Configuration for the shared content editor (pages, posts,
    // and any future content type with a similar shape).
    // ============================================================
    public class ContentEditorConfig
    {
        // ===== Identity =====
        public int Id = 0;                              // 0 = create, >0 = edit
        public string EntityLabel = "Item";             // "Page" | "Post"
        public string ListUrl = "/admin";               // back-to-list URL
        public string ApiUrl = "/api/admin";            // POST endpoint
        public string ActiveNavItem = "";               // for sidebar highlight
        public string PublicUrlPrefix = "/";            // pages & posts are served prefix-less at the root

        // ===== Field schema =====
        public bool ShowExcerpt = true;
        public bool ShowCoverImage = false;
        public bool ShowCategory = false;
        public bool ShowLayoutSelect = false;
        public bool ShowInNavToggle = false;
        public bool ShowPinToggle = false;
        public bool ShowSortOrder = false;
        public bool ShowPublishDate = true;
        public bool ShowEditorTypeSelect = true;

        // ===== Hints =====
        public string ContentPlaceholder = "<p>Write here…</p>";
        public string ExcerptPlaceholder = "Short summary (optional)";

        // ===== Data =====
        public string Title = "";
        public string Slug = "";
        public string Content = "";
        public string ContentFormat = "markdown";   // "html" or "markdown" - default when no stored value
        // True only when this value came from a real saved DB row. When false (new
        // item, or legacy row with empty content_format), the client falls back to
        // the LocalStorage preference. Precedence: DB > LocalStorage > Default.
        public bool HasStoredFormat = false;
        public string Excerpt = "";
        public string CoverImage = "";
        public int CategoryId = 0;
        public List<obCategory> CategoryOptions = null;  // managed categories for the dropdown
        public string Layout = "";   // "split" | "stack" (resolved default by caller)
        public int IsPublished = 0;
        public int IsPinned = 0;
        public int ShowInNav = 0;
        public int SortOrder = 0;
        public DateTime DatePublished = DateTime.MinValue;
    }

    public static class ContentEditor
    {
        public static void Render(ContentEditorConfig cfg)
        {
            string heading = (cfg.Id > 0 ? "Edit " : "New ") + cfg.EntityLabel.ToLowerInvariant();

            string viewLinkHtml = "";
            if (cfg.Id > 0 && !string.IsNullOrEmpty(cfg.Slug))
            {
                string publicUrl = cfg.PublicUrlPrefix + HttpUtility.HtmlAttributeEncode(cfg.Slug);
                viewLinkHtml = "<a href='" + publicUrl + "' target='_blank' class='btn btn-ghost btn-sm'><i class='fa-solid fa-arrow-up-right-from-square'></i> View</a>";
            }

            var tpl = new AdminTemplate
            {
                Title = heading,
                ActiveItem = cfg.ActiveNavItem,
                PageHeading = heading,
                PageHeadingActionsHtml = viewLinkHtml,
                ExtraHeaderText =
                    "<link rel='stylesheet' href='/css/editor.css' />\n",
                ExtraFooterText =
                    "<script src='/js/media-browser.js'></script>\n" +
                    "<script src='/js/format-html.js'></script>\n" +
                    "<script src='/js/editor.js'></script>\n" +
                    // Loaded last so its dependencies (editor.js, media-browser.js)
                    // are present. Reads the window.HEARTH_EDITOR config emitted in
                    // the body. See engine/ContentEditor.cs JS section.
                    "<script src='/js/editor-page.js'></script>\n"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            string title = HttpUtility.HtmlAttributeEncode(cfg.Title);
            string slug = HttpUtility.HtmlAttributeEncode(cfg.Slug);
            string excerpt = HttpUtility.HtmlAttributeEncode(cfg.Excerpt);
            string cover = HttpUtility.HtmlAttributeEncode(cfg.CoverImage);
            string contentHtml = HttpUtility.HtmlEncode(cfg.Content);
            string excerptText = HttpUtility.HtmlEncode(cfg.Excerpt);
            string publishedAttr = cfg.IsPublished == 1 ? "checked" : "";
            string pinnedAttr = cfg.IsPinned == 1 ? "checked" : "";
            string showInNavAttr = cfg.ShowInNav == 1 ? "checked" : "";

            string isMarkdown = cfg.ContentFormat == "markdown" ? "markdown" : "html";

            // ===== Form open =====
            sb.Append($@"
<form id='contentForm' onsubmit='return saveItem(event)' class='editor-form'>
    <input type='hidden' name='id' value='{cfg.Id}' />

    <div class='editor-layout'>
        <div class='editor-main'>
            <div class='form-field'>
                <label for='title'>Title</label>
                <input type='text' id='title' name='title' value='{title}' placeholder='{HttpUtility.HtmlAttributeEncode(cfg.EntityLabel)} title' required />
            </div>
");

            // ===== Excerpt - now ALWAYS above content, uniform textarea rows=2 =====
            if (cfg.ShowExcerpt)
            {
                sb.Append($@"            <div class='form-field'>
                <label for='excerpt'>Excerpt</label>
                <textarea id='excerpt' name='excerpt' rows='2' placeholder='{HttpUtility.HtmlAttributeEncode(cfg.ExcerptPlaceholder)}'>{excerptText}</textarea>
            </div>
");
            }

            // ===== Editor type selector + Content =====
            if (cfg.ShowEditorTypeSelect)
            {
                string htmlSelected = isMarkdown == "html" ? "selected" : "";
                string mdSelected = isMarkdown == "markdown" ? "selected" : "";
                sb.Append($@"            <div class='form-field editor-type-row'>
                <label for='content_format'>Editor Type</label>
                <select id='content_format' name='content_format' class='editor-type-select'>
                    <option value='html' {htmlSelected}>HTML Editor (WYSIWYG)</option>
                    <option value='markdown' {mdSelected}>Markdown</option>
                </select>
                <a href='/admin/markdown-docs' target='_blank' class='editor-type-help'><i class='fa-brands fa-markdown'></i> Markdown reference</a>
            </div>
");
            }
            else
            {
                sb.Append($"            <input type='hidden' name='content_format' value='{isMarkdown}' />\n");
            }

            sb.Append($@"            <div class='form-field' id='wysiwyg-wrap' style='{(isMarkdown == "html" ? "" : "display:none")}'>
                <label>Content</label>
                {RenderWysiwygMarkup()}
            </div>
            <div class='form-field' id='markdown-wrap' style='{(isMarkdown == "markdown" ? "" : "display:none")}'>
                <label>Content (Markdown)</label>
                <div class='md-tabs' id='md-tabs'>
                    <div class='md-tab-bar' role='tablist'>
                        <button type='button' class='md-tab is-active' data-md-tab='edit' role='tab' aria-selected='true'><i class='fa-solid fa-pen'></i> Edit</button>
                        <button type='button' class='md-tab' data-md-tab='preview' role='tab' aria-selected='false'><i class='fa-solid fa-eye'></i> Preview</button>
                    </div>
                    <div class='md-tab-panel is-active' data-md-panel='edit'>
                        <textarea id='markdown_content' spellcheck='false' class='markdown-editor' placeholder='# Heading\n\nWrite in Markdown - use **bold**, *italic*, and `code`.'>{contentHtml}</textarea>
                    </div>
                    <div class='md-tab-panel' data-md-panel='preview'>
                        <div class='md-preview-status' id='md-preview-status'>Loading preview…</div>
                        <iframe id='md-preview-frame' class='md-preview-frame' title='Markdown preview'></iframe>
                    </div>
                </div>
            </div>
            <textarea name='content' data-editor-source style='display:none' aria-hidden='true'>{contentHtml}</textarea>
");

            // ===== Side panel - Publish =====
            sb.Append($@"        </div>
        <aside class='editor-side'>
            <div class='side-card'>
                <h3>Publish</h3>
                <div class='side-actions'>
                    <button type='submit' class='btn btn-primary btn-block'><i class='fa-solid fa-floppy-disk'></i> Save {HttpUtility.HtmlEncode(cfg.EntityLabel.ToLowerInvariant())}</button>
                    <a href='{HttpUtility.HtmlAttributeEncode(cfg.ListUrl)}' class='btn btn-ghost btn-block'>Cancel</a>
                </div>
                <label class='switch'>
                    <input type='checkbox' name='is_published' {publishedAttr} />
                    <span>Published</span>
                </label>
");
            if (cfg.ShowPinToggle)
            {
                sb.Append($@"                <label class='switch'>
                    <input type='checkbox' name='is_pinned' {pinnedAttr} />
                    <span>Pin Post</span>
                </label>
");
            }
            if (cfg.ShowInNavToggle)
            {
                sb.Append($@"                <label class='switch'>
                    <input type='checkbox' name='show_in_nav' {showInNavAttr} />
                    <span>Show in navigation</span>
                </label>
");
            }
            if (cfg.ShowSortOrder)
            {
                sb.Append($@"                <div class='form-field'>
                    <label for='sort_order'>Sort order</label>
                    <input type='number' id='sort_order' name='sort_order' value='{cfg.SortOrder}' />
                </div>
");
            }
            if (cfg.ShowPublishDate)
            {
                string dateValue = cfg.DatePublished > DateTime.MinValue
                    ? cfg.DatePublished.ToString("yyyy-MM-dd")
                    : (cfg.Id == 0 ? DateTime.Today.ToString("yyyy-MM-dd") : "");
                sb.Append($@"                <div class='form-field'>
                    <label for='date_published'>Publish date</label>
                    <input type='date' id='date_published' name='date_published' value='{dateValue}' />
                </div>
");
            }
            sb.Append("            </div>\n");

            // ===== Side panel - Layout (optional) =====
            if (cfg.ShowLayoutSelect)
            {
                string splitSel = cfg.Layout == "split" ? "selected" : "";
                string stackSel = cfg.Layout == "stack" ? "selected" : "";
                sb.Append($@"            <div class='side-card'>
                <h3>Layout</h3>
                <div class='form-field'>
                    <select id='layout' name='layout' class='editor-type-select'>
                        <option value='split' {splitSel}>Left / Right (sidebar)</option>
                        <option value='stack' {stackSel}>Top / Bottom (full width)</option>
                    </select>
                    <small class='form-hint'>How this {HttpUtility.HtmlEncode(cfg.EntityLabel.ToLowerInvariant())} arranges its content.</small>
                </div>
            </div>
");
            }

            // ===== Side panel - Slug =====
            sb.Append($@"            <div class='side-card'>
                <h3>Slug</h3>
                <div class='form-field'>
                    <input type='text' id='slug' name='slug' value='{slug}' placeholder='auto-from-title' />
                    <small class='form-hint'>URL: {HttpUtility.HtmlEncode(cfg.PublicUrlPrefix)}<span id='slugPreview'>{HttpUtility.HtmlEncode(cfg.Slug)}</span></small>
                </div>
            </div>
");

            // ===== Side panel - Category (optional) =====
            // Dropdown of managed categories. The post stores a category id
            // (posts.category_id); 0 / "" = uncategorized ("— None —"). Because a
            // deleted category resets dependent posts back to 0, the post always
            // points at either a live category or none — so there is no "unmanaged"
            // fallback option to preserve.
            if (cfg.ShowCategory)
            {
                var options = new StringBuilder();
                options.Append("<option value='0'>— None —</option>");

                if (cfg.CategoryOptions != null)
                {
                    foreach (var c in cfg.CategoryOptions)
                    {
                        bool sel = (c.Id == cfg.CategoryId);
                        options.Append($"<option value='{c.Id}'{(sel ? " selected" : "")}>{HttpUtility.HtmlEncode(c.Name)}</option>");
                    }
                }

                sb.Append($@"            <div class='side-card'>
                <h3>Category</h3>
                <div class='form-field'>
                    <select id='category_id' name='category_id' class='editor-type-select'>
                        {options}
                    </select>
                    <small class='form-hint'>Manage the list under <a href='/admin/categories' target='_blank'>Categories</a>.</small>
                </div>
            </div>
");
            }

            // ===== Side panel - Cover image (optional) =====
            if (cfg.ShowCoverImage)
            {
                string previewStyle = string.IsNullOrEmpty(cfg.CoverImage) ? "display:none" : "";
                sb.Append($@"            <div class='side-card'>
                <h3>Cover image</h3>
                <div class='cover-preview' id='coverPreview' style='{previewStyle}'>
                    <img src='{cover}' alt='' />
                </div>
                <div class='form-field'>
                    <input type='text' id='cover_image' name='cover_image' value='{cover}' placeholder='/uploads/photo.jpg' />
                </div>
                <button type='button' class='btn btn-ghost btn-sm btn-block' onclick='pickCover()'><i class='fa-solid fa-image'></i> Choose Image</button>
            </div>
");
            }

            // ===== Side panel - actions =====
            sb.Append($@"        </aside>
    </div>
</form>
");

            // ===== JS =====
            // The page logic lives in the static /js/editor-page.js (registered in
            // ExtraFooterText, loaded after its deps). Here we emit only the
            // per-request dynamic values, as a window.HEARTH_EDITOR config that is
            // defined in the body - i.e. BEFORE the footer scripts run.
            //   hasStoredFormat -> editor-type precedence (DB > LocalStorage > Default)
            //   apiUrl          -> save endpoint (POST)
            //   listUrl         -> back-to-list URL after a successful save
            string apiUrlJs = HttpUtility.JavaScriptStringEncode(cfg.ApiUrl);
            string listUrlJs = HttpUtility.JavaScriptStringEncode(cfg.ListUrl);
            string hasStoredFormatJs = cfg.HasStoredFormat ? "true" : "false";

            sb.Append($@"
<script>
window.HEARTH_EDITOR = {{
    hasStoredFormat: {hasStoredFormatJs},
    apiUrl: '{apiUrlJs}',
    listUrl: '{listUrlJs}'
}};
</script>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        // ============================================================
        // WYSIWYG editor markup - toolbar uses FontAwesome icons.
        // Wires to /js/editor.js, which reads the hidden
        // <textarea data-editor-source> and syncs on form submit.
        // ============================================================
        static string RenderWysiwygMarkup()
        {
            return @"
<div class='wysiwyg' id='editor'>
    <div class='wysiwyg__toolbar' role='toolbar' aria-label='Formatting'>

        <div class='wysiwyg__group'>
            <select class='wysiwyg__select' data-format-block title='Paragraph format'>
                <option value='p'>Paragraph</option>
                <option value='h1'>Heading 1</option>
                <option value='h2'>Heading 2</option>
                <option value='h3'>Heading 3</option>
                <option value='h4'>Heading 4</option>
                <option value='blockquote'>Quote</option>
            </select>
        </div>

        <div class='wysiwyg__group'>
            <button type='button' class='wysiwyg__btn' data-cmd='bold' title='Bold (Ctrl+B)' aria-label='Bold'><i class='fa-solid fa-bold'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='italic' title='Italic (Ctrl+I)' aria-label='Italic'><i class='fa-solid fa-italic'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='underline' title='Underline (Ctrl+U)' aria-label='Underline'><i class='fa-solid fa-underline'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='strikeThrough' title='Strikethrough' aria-label='Strikethrough'><i class='fa-solid fa-strikethrough'></i></button>
        </div>

        <div class='wysiwyg__group'>
            <label title='Text color' style='display:flex;align-items:center;'>
                <input type='color' class='wysiwyg__color' data-cmd='foreColor' value='#000000' />
            </label>
            <label title='Highlight' style='display:flex;align-items:center;'>
                <input type='color' class='wysiwyg__color' data-cmd='hiliteColor' value='#ffff00' />
            </label>
        </div>

        <div class='wysiwyg__group'>
            <button type='button' class='wysiwyg__btn' data-cmd='insertUnorderedList' title='Bullet list' aria-label='Bullet list'><i class='fa-solid fa-list-ul'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='insertOrderedList' title='Numbered list' aria-label='Numbered list'><i class='fa-solid fa-list-ol'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='outdent' title='Decrease indent' aria-label='Outdent'><i class='fa-solid fa-outdent'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='indent' title='Increase indent' aria-label='Indent'><i class='fa-solid fa-indent'></i></button>
        </div>

        <div class='wysiwyg__group'>
            <button type='button' class='wysiwyg__btn' data-cmd='justifyLeft' title='Align left' aria-label='Align left'><i class='fa-solid fa-align-left'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='justifyCenter' title='Align center' aria-label='Align center'><i class='fa-solid fa-align-center'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='justifyRight' title='Align right' aria-label='Align right'><i class='fa-solid fa-align-right'></i></button>
        </div>

        <div class='wysiwyg__group'>
            <button type='button' class='wysiwyg__btn' data-action='link' title='Insert link (Ctrl+K)' aria-label='Insert link'><i class='fa-solid fa-link'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='unlink' title='Remove link' aria-label='Remove link'><i class='fa-solid fa-link-slash'></i></button>
            <button type='button' class='wysiwyg__btn' data-action='image' title='Insert image' aria-label='Insert image'><i class='fa-solid fa-image'></i></button>
            <button type='button' class='wysiwyg__btn' data-action='table' title='Insert table' aria-label='Insert table'><i class='fa-solid fa-table'></i></button>
            <button type='button' class='wysiwyg__btn' data-action='inlinecode' title='Inline code' aria-label='Inline code'><i class='fa-solid fa-terminal'></i></button>
            <button type='button' class='wysiwyg__btn' data-action='code' title='Insert code block' aria-label='Insert code block'><i class='fa-solid fa-code'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='insertHorizontalRule' title='Horizontal rule' aria-label='Horizontal rule'><i class='fa-solid fa-minus'></i></button>
        </div>

        <div class='wysiwyg__group'>
            <button type='button' class='wysiwyg__btn' data-cmd='undo' title='Undo (Ctrl+Z)' aria-label='Undo'><i class='fa-solid fa-rotate-left'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='redo' title='Redo (Ctrl+Y)' aria-label='Redo'><i class='fa-solid fa-rotate-right'></i></button>
            <button type='button' class='wysiwyg__btn' data-cmd='removeFormat' title='Clear formatting' aria-label='Clear formatting'><i class='fa-solid fa-eraser'></i></button>
        </div>

        <div class='wysiwyg__group' style='margin-left:auto;'>
            <button type='button' class='wysiwyg__btn' data-action='source' title='View HTML source' aria-label='View HTML source'><i class='fa-solid fa-file-code'></i></button>
        </div>
    </div>

    <div class='wysiwyg__table-tools'>
        <button type='button' class='wysiwyg__btn' data-table='insertRowAbove' title='Insert row above'><i class='fa-solid fa-arrow-up'></i> Row</button>
        <button type='button' class='wysiwyg__btn' data-table='insertRowBelow' title='Insert row below'><i class='fa-solid fa-arrow-down'></i> Row</button>
        <button type='button' class='wysiwyg__btn' data-table='insertColLeft' title='Insert column left'><i class='fa-solid fa-arrow-left'></i> Col</button>
        <button type='button' class='wysiwyg__btn' data-table='insertColRight' title='Insert column right'><i class='fa-solid fa-arrow-right'></i> Col</button>
        <button type='button' class='wysiwyg__btn' data-table='deleteRow' title='Delete row'><i class='fa-solid fa-xmark'></i> Row</button>
        <button type='button' class='wysiwyg__btn' data-table='deleteCol' title='Delete column'><i class='fa-solid fa-xmark'></i> Col</button>
        <button type='button' class='wysiwyg__btn' data-table='deleteTable' title='Delete table'><i class='fa-solid fa-xmark'></i> Table</button>
    </div>

    <div class='wysiwyg__content' contenteditable='true' spellcheck='true' data-placeholder='Start typing here...'></div>
    <textarea class='wysiwyg__source' spellcheck='false'></textarea>

    <div class='wysiwyg__status'>
        <span class='wysiwyg__status-mode'>Visual</span>
        <span class='wysiwyg__status-count'>0 words</span>
    </div>
</div>

<!-- Image dialog -->
<div class='wy-modal' id='image-modal' role='dialog' aria-modal='true' aria-labelledby='image-modal-title'>
    <div class='wy-modal__dialog'>
        <div class='wy-modal__header'>
            <h3 class='wy-modal__title' id='image-modal-title'>Insert Image</h3>
            <button type='button' class='wy-modal__close' data-close aria-label='Close'><i class='fa-solid fa-xmark'></i></button>
        </div>
        <div class='wy-modal__body'>
            <button type='button' class='wy-btn' id='btn-select-media-top'><i class='fa-solid fa-photo-film'></i> Select Image From Media Library</button>

            <div class='wy-image-grid'>
                <div class='wy-image-grid__fields'>
                    <div class='wy-field'>
                        <label for='img-url'>Image URL</label>
                        <input type='text' id='img-url' placeholder='https://...' />
                    </div>
                    <div class='wy-field'>
                        <label for='img-alt'>Alt text</label>
                        <input type='text' id='img-alt' placeholder='Describe the image' />
                    </div>
                    <div class='wy-row'>
                        <div class='wy-field'>
                            <label for='img-width'>Width</label>
                            <input type='text' id='img-width' placeholder='auto' />
                        </div>
                        <div class='wy-field'>
                            <label for='img-height'>Height</label>
                            <input type='text' id='img-height' placeholder='auto' />
                        </div>
                    </div>
                </div>
                <div id='div-img-preview' class='wy-img-preview'></div>
            </div>

            <button type='button' class='wy-btn' id='btn-select-media-bottom'><i class='fa-solid fa-photo-film'></i> Select Image From Media Library</button>
        </div>
        <div class='wy-modal__footer'>
            <button type='button' class='wy-btn' data-close>Cancel</button>
            <button type='button' class='wy-btn wy-btn--primary' id='img-insert'><i class='fa-solid fa-check'></i> Insert</button>
        </div>
    </div>
</div>

<!-- Link dialog -->
<div class='wy-modal' id='link-modal' role='dialog' aria-modal='true'>
    <div class='wy-modal__dialog'>
        <div class='wy-modal__header'>
            <h3 class='wy-modal__title'>Insert Link</h3>
            <button type='button' class='wy-modal__close' data-close aria-label='Close'><i class='fa-solid fa-xmark'></i></button>
        </div>
        <div class='wy-modal__body'>
            <div class='wy-field'>
                <label for='link-url'>URL</label>
                <input type='text' id='link-url' placeholder='https://...' />
            </div>
            <div class='wy-field'>
                <label for='link-text'>Text to display</label>
                <input type='text' id='link-text' placeholder='(uses selected text)' />
            </div>
            <div class='wy-field'>
                <label><input type='checkbox' id='link-newtab' /> Open in new tab</label>
            </div>
        </div>
        <div class='wy-modal__footer'>
            <button type='button' class='wy-btn' data-close>Cancel</button>
            <button type='button' class='wy-btn wy-btn--primary' id='link-insert'><i class='fa-solid fa-check'></i> Insert</button>
        </div>
    </div>
</div>

<!-- Table dialog -->
<div class='wy-modal' id='table-modal' role='dialog' aria-modal='true'>
    <div class='wy-modal__dialog'>
        <div class='wy-modal__header'>
            <h3 class='wy-modal__title'>Insert Table</h3>
            <button type='button' class='wy-modal__close' data-close aria-label='Close'><i class='fa-solid fa-xmark'></i></button>
        </div>
        <div class='wy-modal__body'>
            <div class='wy-row'>
                <div class='wy-field'>
                    <label for='tbl-rows'>Rows</label>
                    <input type='number' id='tbl-rows' min='1' max='50' value='3' />
                </div>
                <div class='wy-field'>
                    <label for='tbl-cols'>Columns</label>
                    <input type='number' id='tbl-cols' min='1' max='20' value='3' />
                </div>
            </div>
            <div class='wy-field'>
                <label><input type='checkbox' id='tbl-header' checked /> Include header row</label>
            </div>
        </div>
        <div class='wy-modal__footer'>
            <button type='button' class='wy-btn' data-close>Cancel</button>
            <button type='button' class='wy-btn wy-btn--primary' id='tbl-insert'><i class='fa-solid fa-check'></i> Insert</button>
        </div>
    </div>
</div>

<!-- Code block dialog -->
<div class='wy-modal' id='code-modal' role='dialog' aria-modal='true'>
    <div class='wy-modal__dialog'>
        <div class='wy-modal__header'>
            <h3 class='wy-modal__title'>Insert Code Block</h3>
            <button type='button' class='wy-modal__close' data-close aria-label='Close'><i class='fa-solid fa-xmark'></i></button>
        </div>
        <div class='wy-modal__body'>
            <div class='wy-field'>
                <label for='code-lang'>Language</label>
                <select id='code-lang'>
                    <option value='plaintext'>Plain text</option>
                    <option value='javascript'>JavaScript</option>
                    <option value='typescript'>TypeScript</option>
                    <option value='html'>HTML</option>
                    <option value='css'>CSS</option>
                    <option value='python'>Python</option>
                    <option value='java'>Java</option>
                    <option value='csharp'>C#</option>
                    <option value='cpp'>C++</option>
                    <option value='go'>Go</option>
                    <option value='rust'>Rust</option>
                    <option value='php'>PHP</option>
                    <option value='ruby'>Ruby</option>
                    <option value='sql'>SQL</option>
                    <option value='bash'>Bash</option>
                    <option value='json'>JSON</option>
                    <option value='yaml'>YAML</option>
                    <option value='xml'>XML</option>
                    <option value='markdown'>Markdown</option>
                </select>
            </div>
            <div class='wy-field'>
                <label for='code-content'>Code</label>
                <textarea id='code-content' rows='10' placeholder='Paste or type your code here...'
                          style='font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px;'></textarea>
            </div>
        </div>
        <div class='wy-modal__footer'>
            <button type='button' class='wy-btn' data-close>Cancel</button>
            <button type='button' class='wy-btn wy-btn--primary' id='code-insert'><i class='fa-solid fa-check'></i> Insert</button>
        </div>
    </div>
</div>
";
        }
    }
}
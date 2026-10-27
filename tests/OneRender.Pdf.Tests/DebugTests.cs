using OneRender.Image;
using OneRender.Layout;
using OneRender.Painting;

namespace OneRender.Pdf.Tests;

public class DebugTests
{
    [Fact]
    public void Dump()
    {
        string html = File.ReadAllText(Support.TemplatePath("statement") + "/index.html");
        var res = Support.TemplateResources("statement");
        var po = new HtmlPageOptions(FontProviders.ForCurrentThread()) { ViewportWidth = 700, ViewportHeight = 1000, StylesheetBytes = res };
        HtmlPage page = HtmlPage.Create(html, po);
        var list = new DisplayList();
        new Painter().Paint(page.Fragments, list, new PaintOptions(700, 1000) { Tags = true, Fonts = po.Fonts });
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        for (int i = 0; i < list.Count && shown < 120; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind is ItemKind.BeginTag or ItemKind.EndTag or ItemKind.GlyphRun or ItemKind.FillRect)
            {
                sb.Append(i).Append(' ').Append(it.Kind);
                if (it.Kind == ItemKind.BeginTag) { sb.Append(' ').Append(it.Role).Append(" key=").Append(it.Id.Value >> 16); }
                if (it.Kind == ItemKind.GlyphRun) { sb.Append(' ').Append(it.GlyphRun.Text); }
                sb.AppendLine();
                shown++;
            }
        }

        File.WriteAllText(Path.Combine(Support.OutDir ?? ".", "dump.txt"), sb.ToString());
    }
}

using System.Net;
using System.Text;

namespace ImageDupeFinder.Core;

/// <summary>Writes a self-contained HTML report (thumbnails embedded) that can be opened in any browser.</summary>
public static class HtmlReportWriter
{
    public static void Write(ScanResult result, string path) => File.WriteAllText(path, Build(result), Encoding.UTF8);

    public static string Build(ScanResult result)
    {
        var o = result.Options;
        var sb = new StringBuilder();
        int nNew = result.Count(MatchCategory.New), nDup = result.Count(MatchCategory.Duplicate), nPos = result.Count(MatchCategory.PossibleDuplicate);

        sb.Append("""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Image duplicate report</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--ink:#1c2230;--muted:#6b7280;--line:#e3e6ea;--new:#1f7a4d;--dup:#b42318;--pos:#b54708;--hl:#fff4e5}
@media (prefers-color-scheme: dark){:root{--bg:#14171c;--card:#1d2128;--ink:#e6e8eb;--muted:#9aa3ad;--line:#2d333c;--new:#4cc38a;--dup:#f97066;--pos:#fdb022;--hl:#3a2e1c}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 "Segoe UI",system-ui,sans-serif}
main{max-width:1200px;margin:0 auto;padding:24px 16px 64px}h1{margin:0 0 4px;font-size:22px}
.meta{color:var(--muted);margin:0 0 20px}.meta code{font-size:12px;word-break:break-all}
.pills{display:flex;gap:10px;flex-wrap:wrap;margin-bottom:28px}
.pill{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:10px 16px;text-decoration:none;color:inherit;min-width:150px}
.pill b{display:block;font-size:24px}.pill.new b{color:var(--new)}.pill.dup b{color:var(--dup)}.pill.pos b{color:var(--pos)}
h2{font-size:17px;margin:32px 0 10px;padding-bottom:6px;border-bottom:2px solid var(--line)}
h2.new{border-color:var(--new)}h2.dup{border-color:var(--dup)}h2.pos{border-color:var(--pos)}
.row{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.3fr);gap:16px;background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px;margin-bottom:10px}
.side{display:flex;gap:12px;min-width:0}.side img{width:96px;height:96px;object-fit:contain;background:repeating-conic-gradient(#8881 0 25%,transparent 0 50%) 0 0/16px 16px;border-radius:6px;flex:none}
.info{min-width:0}.info a{color:inherit;font-weight:600;word-break:break-all}.sub{color:var(--muted);font-size:12.5px}
.reason{font-weight:600}.dup .reason{color:var(--dup)}.pos .reason{color:var(--pos)}
ul{margin:4px 0 0;padding-left:18px}li{font-size:12.5px}.diff{background:var(--hl);border-radius:4px;padding:0 4px}
details{margin-top:6px;font-size:12.5px}summary{cursor:pointer;color:var(--muted)}
.none{color:var(--muted);font-style:italic}.lbl{font-size:11px;text-transform:uppercase;letter-spacing:.04em;color:var(--muted)}
@media (max-width:720px){.row{grid-template-columns:1fr}}
</style></head><body><main>
""");
        sb.Append("<h1>Image duplicate report</h1>");
        sb.Append($"<p class=\"meta\">Folder 1 (reference): <code>{H(o.ReferenceFolder)}</code><br>Folder 2 (checked): <code>{H(o.CheckFolder)}</code><br>");
        sb.Append($"{result.ReferenceImages.Count} reference images · {result.Items.Count} checked · similarity tolerance {o.SimilarityTolerance} · {(o.IncludeSubfolders ? "subfolders included" : "top level only")} · {result.CompletedAt:yyyy-MM-dd HH:mm}</p>");

        sb.Append("<div class=\"pills\">");
        sb.Append($"<a class=\"pill new\" href=\"#new\"><b>{nNew}</b>New</a>");
        sb.Append($"<a class=\"pill dup\" href=\"#dup\"><b>{nDup}</b>Duplicate</a>");
        sb.Append($"<a class=\"pill pos\" href=\"#pos\"><b>{nPos}</b>Possible duplicate</a>");
        sb.Append("</div>");

        Section(sb, result, MatchCategory.PossibleDuplicate, "pos", "Possible duplicates");
        Section(sb, result, MatchCategory.Duplicate, "dup", "Duplicates");
        Section(sb, result, MatchCategory.New, "new", "New");

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, ScanResult result, MatchCategory cat, string cls, string title)
    {
        var items = result.InCategory(cat).ToList();
        sb.Append($"<h2 id=\"{cls}\" class=\"{cls}\">{title} ({items.Count})</h2>");
        if (items.Count == 0) { sb.Append("<p class=\"none\">None.</p>"); return; }

        foreach (var item in items)
        {
            sb.Append($"<div class=\"row {cls}\">");
            sb.Append("<div class=\"side\">");
            Thumb(sb, item.Image);
            sb.Append("<div class=\"info\"><div class=\"lbl\">Folder 2</div>");
            FileLine(sb, item.Image);
            if (item.Image.DecodeError is not null) sb.Append($"<div class=\"sub\">Could not read image: {H(item.Image.DecodeError)}</div>");
            sb.Append("</div></div>");

            var best = item.BestMatch;
            if (best is null)
            {
                sb.Append("<div class=\"side\"><div class=\"info none\">No match in folder 1</div></div>");
            }
            else
            {
                sb.Append("<div class=\"side\">");
                Thumb(sb, best.Reference);
                sb.Append("<div class=\"info\"><div class=\"lbl\">Folder 1 match</div>");
                FileLine(sb, best.Reference);
                sb.Append($"<div class=\"reason\">{H(best.Reason)}</div>");
                Diffs(sb, best);
                if (item.Matches.Count > 1)
                {
                    sb.Append($"<details><summary>{item.Matches.Count - 1} other candidate(s)</summary><ul>");
                    foreach (var m in item.Matches.Skip(1))
                        sb.Append($"<li><a href=\"{FileUrl(m.Reference.FullPath)}\">{H(m.Reference.RelativePath)}</a> — {H(m.Reason)}</li>");
                    sb.Append("</ul></details>");
                }
                sb.Append("</div></div>");
            }
            sb.Append("</div>");
        }
    }

    private static void Diffs(StringBuilder sb, MatchCandidate m)
    {
        if (m.Differences.Count == 0)
        {
            sb.Append(m.IsExactFile ? "" : "<div class=\"sub\">No differences in the compared properties.</div>");
            return;
        }
        sb.Append("<ul>");
        foreach (var d in m.Differences) sb.Append($"<li><span class=\"diff\">{H(d)}</span></li>");
        sb.Append("</ul>");
    }

    private static void FileLine(StringBuilder sb, ImageRecord r)
    {
        sb.Append($"<a href=\"{FileUrl(r.FullPath)}\">{H(r.RelativePath)}</a>");
        sb.Append($"<div class=\"sub\">{H(r.DimensionsText)} · {Comparison.FormatSize(r.FileSize)} · {H(r.Format ?? "?")}");
        if (r.DateTaken is DateTime dt) sb.Append($" · taken {dt:yyyy-MM-dd HH:mm}");
        sb.Append("</div>");
    }

    private static void Thumb(StringBuilder sb, ImageRecord r)
    {
        if (r.ThumbnailPng is null) { sb.Append("<img alt=\"(no preview)\">"); return; }
        sb.Append($"<img alt=\"\" src=\"data:image/png;base64,{Convert.ToBase64String(r.ThumbnailPng)}\">");
    }

    private static string FileUrl(string path) => H(new Uri(path).AbsoluteUri);
    private static string H(string s) => WebUtility.HtmlEncode(s);
}

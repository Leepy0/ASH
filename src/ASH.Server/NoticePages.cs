using System.Text;
using static ASH.Server.JobPages;

namespace ASH.Server;

/// <summary>/admin/notices: version notes pulled from GitHub (검토 대기 → 공지 / 숨김) and general notices an admin writes. Plain forms; every button posts the whole form,
/// so an approve also keeps the text just typed.</summary>
public static class NoticePages
{
    const string Css = @"<style>
.nt-h{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:14px 0 4px}.nt-h h2{margin:0;font-size:16px}.nt-h .sp{flex:1}
.nt{margin-top:8px}.nt>summary b{overflow-wrap:anywhere}.nt .tg{font-size:12px;font-weight:600;padding:1px 8px;border-radius:10px;background:var(--fill);color:var(--sub2)}
.nt .tg.imp{background:var(--red-b);color:var(--red)}.nt .tg.chg{background:var(--orange-b);color:#7a3500}
.nt form{display:grid;gap:8px}.nt label{display:flex;align-items:center;gap:8px;flex-wrap:wrap;font-size:14px}.nt label input[type=checkbox]{width:auto;min-height:0}
.nt textarea{font-family:Consolas,monospace;min-height:110px}.nt .row{display:flex;gap:8px;flex-wrap:wrap}.nt .gh{background:#f5f5f7;border-radius:8px;padding:8px 12px;font-size:13px}.nt .gh ul{margin:4px 0 0;padding-left:18px}
</style>";

    static string Input(NoticeRec? n) => $"<input type=\"text\" name=\"title\" value=\"{H(n?.Title)}\" maxlength=\"{NoticeStore.TitleMax}\" placeholder=\"제목\" aria-label=\"제목\" required>";

    static string Fields(NoticeRec? n) =>
        $"<label><input type=\"checkbox\" name=\"important\" value=\"1\"{(n?.Important == true ? " checked" : "")}>중요 (PC에서 팝업)</label>" +
        $"<textarea name=\"lines\" rows=\"6\" aria-label=\"항목(한 줄에 하나)\" placeholder=\"항목, 한 줄에 하나\">{H(string.Join("\n", n?.Lines ?? new()))}</textarea>";

    static string Period(NoticeRec? n) =>
        $"<div class=\"row\"><label>시작 <input type=\"date\" name=\"from\" value=\"{H(n?.From)}\"></label><label>종료 <input type=\"date\" name=\"to\" value=\"{H(n?.To)}\"></label><span class=\"muted\">비우면 기한 없음</span></div>";

    static string Btn(string id, string act, string label, string cls = "") =>
        $"<button formaction=\"/admin/notices/{H(id)}/{act}\" class=\"{cls}\"{(act == "delete" ? " formnovalidate onclick=\"return confirm('지울까요?')\"" : "")}>{label}</button>";

    static void Card(StringBuilder sb, NoticeRec n)
    {
        var tags = new StringBuilder();
        if (n.Important) tags.Append(" <span class=\"tg imp\">중요</span>");
        if (n.Changed) tags.Append(" <span class=\"tg chg\">GitHub 내용 바뀜</span>");
        else if (n.Edited) tags.Append(" <span class=\"tg\">수정함</span>");
        if (n.General && (n.From.Length > 0 || n.To.Length > 0)) tags.Append($" <span class=\"tg\">{H(n.From)} ~ {H(n.To)}</span>");
        sb.Append($"<details class=\"box nt\" id=\"{H(n.Id)}\"{(n.Changed ? " open" : "")}><summary><b>{H(n.General ? n.Title : n.Version + " · " + n.Title)}</b>{tags}</summary>")
          .Append($"<form method=\"post\" action=\"/admin/notices/{H(n.Id)}/save\">").Append(Input(n)).Append(Fields(n));
        if (n.General) sb.Append(Period(n));
        if (n.Changed && n.Github != null)
            sb.Append("<div class=\"gh\"><b>GitHub 내용</b> · ").Append(H(n.Github.Title)).Append(n.Github.Important ? " · 중요" : "").Append("<ul>")
              .AppendJoin("", n.Github.Lines.Select(l => $"<li>{H(l)}</li>")).Append("</ul></div>");
        sb.Append("<div class=\"row\">").Append(Btn(n.Id, "save", "저장"));
        if (n.State != NoticeStore.Approved) sb.Append(Btn(n.Id, "approve", "공지", "primary"));
        if (n.State != NoticeStore.Hidden) sb.Append(Btn(n.Id, "hide", "숨김"));
        if (n.State == NoticeStore.Hidden) sb.Append(Btn(n.Id, "draft", "검토 대기로"));
        if (n.Changed) sb.Append(Btn(n.Id, "github", "GitHub 것으로"));
        if (n.General) sb.Append(Btn(n.Id, "delete", "삭제", "danger"));
        sb.Append("</div></form></details>");
    }

    static void Section(StringBuilder sb, string title, IEnumerable<NoticeRec> list)
    {
        var l = list.ToList();
        sb.Append($"<div class=\"nt-h\"><h2>{title}</h2><span class=\"muted\">{l.Count}</span></div>");
        if (l.Count == 0) sb.Append("<div class=\"muted\" style=\"margin:4px 2px\">없음</div>");
        foreach (var n in l) Card(sb, n);
    }

    static int Key(NoticeRec n) => Version.TryParse(n.Version.Split('-', '+')[0], out var v) ? v.Major * 1_000_000 + v.Minor * 1_000 + v.Build : 0;

    public static string Page(IEnumerable<NoticeRec> all, string githubResult, string? msgHtml = null)
    {
        var sb = new StringBuilder(Css).Append(ServerPages.AdminHead("notices")).Append("<div class=\"panel\">");
        if (msgHtml != null) sb.Append(msgHtml);
        sb.Append($"<div class=\"muted\">GitHub 가져오기: {H(githubResult)} · <a href=\"/admin/settings?tab=server\">서버 설정에서 지금 확인</a></div>");
        sb.Append("<details class=\"box nt\"><summary><b>+ 일반 공지</b></summary><form method=\"post\" action=\"/admin/notices\">").Append(Input(null)).Append(Fields(null)).Append(Period(null))
          .Append("<div class=\"row\"><button class=\"primary\">추가</button><span class=\"muted\">검토 대기로 들어갑니다</span></div></form></details>");
        var list = all.OrderByDescending(Key).ThenByDescending(n => n.Id.Length).ThenByDescending(n => n.Id, StringComparer.Ordinal).ToList();
        Section(sb, "검토 대기", list.Where(n => n.State == NoticeStore.Draft));
        Section(sb, "공지", list.Where(n => n.State == NoticeStore.Approved));
        Section(sb, "숨김", list.Where(n => n.State == NoticeStore.Hidden));
        return JobPages.Page("ASH 공지", sb.Append("</div>").ToString(), nav: "admin");
    }
}

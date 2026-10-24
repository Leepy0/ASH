using ASH.Server;

namespace ASH.Server.Tests;

public class PageTests
{
    [Fact]
    public void Page_Has_Nav_Tokens_And_No_Meta_Refresh()
    {
        var html = JobPages.Page("t", "<p>b</p>", "<script>1</script>");
        Assert.DoesNotContain("http-equiv", html);
        Assert.Contains("href=\"/analyses\"", html); Assert.Contains("href=\"/files\"", html); Assert.Contains("href=\"/admin/settings\"", html);
        Assert.DoesNotContain("href=\"/jobs\"", html);
        Assert.Contains("--blue:#007aff", html); Assert.Contains(".grid2", html);
        Assert.Contains("href=\"/films\"", html); Assert.Contains("class=\"side\"", html); // the collapsing sidebar replaces the top bar
        Assert.DoesNotContain(" on\"", JobPages.Page("t", "")); // no entry marked without nav
        Assert.Contains("aria-current=\"page\"", JobPages.Page("t", "", nav: "home"));
        Assert.EndsWith("<script>1</script></body></html>", html);
    }
}

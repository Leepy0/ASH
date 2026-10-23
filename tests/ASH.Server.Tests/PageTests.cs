using ASH.Server;

namespace ASH.Server.Tests;

public class PageTests
{
    [Fact]
    public void Page_Has_Nav_Tokens_And_No_Meta_Refresh()
    {
        var html = JobPages.Page("t", "<p>b</p>", "<script>1</script>");
        Assert.DoesNotContain("http-equiv", html);
        Assert.Contains("href=\"/analyses\"", html); Assert.Contains("href=\"/admin/settings\"", html);
        Assert.DoesNotContain("href=\"/jobs\"", html);
        Assert.Contains("--navy:#1f3a5f", html); Assert.Contains(".grid2", html);
        Assert.EndsWith("<script>1</script></body></html>", html);
    }
}

using Microsoft.AspNetCore.Http;
using MulticlassRace.Web;

namespace MulticlassRace.Tests.Infrastructure;

public class AppAccessMiddlewareTests
{
    private static async Task<(int Status, bool ReachedNext)> SendAsync(AppAccessToken token, string host, string? cookie)
    {
        var reached = false;
        var middleware = new AppAccessMiddleware(_ => { reached = true; return Task.CompletedTask; }, token);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);

        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{AppAccessToken.CookieName}={cookie}";
        }

        await middleware.InvokeAsync(context);

        return (context.Response.StatusCode, reached);
    }

    [Fact]
    public async Task Allows_request_with_secret_on_loopback()
    {
        var token = new AppAccessToken();

        var (_, reached) = await SendAsync(token, "127.0.0.1:5000", token.Value);

        Assert.True(reached);
    }

    [Fact]
    public async Task Rejects_request_without_cookie()
    {
        var (status, reached) = await SendAsync(new AppAccessToken(), "127.0.0.1:5000", cookie: null);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task Rejects_request_with_wrong_secret()
    {
        var (status, reached) = await SendAsync(new AppAccessToken(), "127.0.0.1:5000", "not-the-secret");

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Theory]
    [InlineData("localhost:5000")]
    [InlineData("evil.example:5000")]
    public async Task Rejects_other_host_names_even_with_secret(string host)
    {
        var token = new AppAccessToken();

        var (status, reached) = await SendAsync(token, host, token.Value);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public void Each_launch_gets_a_different_secret()
    {
        Assert.NotEqual(new AppAccessToken().Value, new AppAccessToken().Value);
    }
}

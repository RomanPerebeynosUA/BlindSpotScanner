namespace BlindSpotScanner.Api.Auth;

/// <summary>
/// CSRF defence for the cookie session (D26): browsers label every request with Sec-Fetch-Site, so
/// a state-changing request to the API or hub from another site (or a sibling subdomain) is refused.
/// Requests without the header (non-browser clients) and the app's own same-origin calls pass.
/// </summary>
public static class CrossSiteGuard
{
    public static IApplicationBuilder UseCrossSiteGuard(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        var unsafeMethod = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (unsafeMethod && (site is "cross-site" or "same-site") && (request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/hubs")))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    });
}

namespace Pos.Branch.Api.Hosting;

public static class BranchMobileHosting
{
    public static void MapBranchMobileIfEnabled(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        var section = configuration.GetSection("BranchMobileHosting");
        if (!section.GetValue<bool>("Enabled")) { return; }

        var root = section["BuildDirectory"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("Branch Mobile hosting requires an absolute build directory.");
        }

        var index = Path.Combine(root, "index.html");
        var assets = Path.Combine(root, "assets");
        if (!File.Exists(index) || !Directory.Exists(assets))
        {
            throw new InvalidOperationException("Branch Mobile hosting requires a Vite build with index.html and assets.");
        }

        var methods = new[] { HttpMethods.Get, HttpMethods.Head };
        endpoints.MapMethods("/mobile", methods, (HttpContext context) =>
        {
            if (!context.Request.IsHttps) { return Results.BadRequest(); }
            if (!context.Request.Path.Value!.EndsWith('/')) { return Results.Redirect("/mobile/"); }
            return Serve(context, index, "text/html; charset=utf-8");
        }).ExcludeFromDescription();
        endpoints.MapMethods("/mobile/favicon.svg", methods, (HttpContext context) =>
            Serve(context, Path.Combine(root, "favicon.svg"), "image/svg+xml")).ExcludeFromDescription();
        endpoints.MapMethods("/mobile/assets/{name}", methods, (HttpContext context, string name) =>
        {
            if (name.Length is < 1 or > 160 || name[0] == '.' ||
                name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
            {
                return Results.NotFound();
            }

            var contentType = Path.GetExtension(name) switch
            {
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                _ => null
            };
            return contentType is null ? Results.NotFound() : Serve(context, Path.Combine(assets, name), contentType);
        }).ExcludeFromDescription();
    }

    private static IResult Serve(HttpContext context, string path, string contentType)
    {
        if (!context.Request.IsHttps) { return Results.BadRequest(); }
        if (!File.Exists(path)) { return Results.NotFound(); }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
        return Results.File(path, contentType);
    }
}

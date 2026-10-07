using System.IO.Compression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Pitacor.Server;

/// <summary>
/// Dev convenience: serves a Unity Web build at "/" from the game server itself, so a phone on the LAN can open
/// http://&lt;pc-ip&gt;:5080 and play. Handles Unity's pre-compressed files (*.br, *.gz) and "/sala/{code}" deep links.
/// </summary>
public static class WebBuildHosting
{
    private static readonly FileExtensionContentTypeProvider Defaults = new();

    /// <summary>Adds the static file middleware when the configured folder exists. Returns the folder or null.</summary>
    public static string? Use(WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.WebBuildPath)) return null;
        string root = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, options.WebBuildPath));
        if (!Directory.Exists(root)) return null;

        var files = new PhysicalFileProvider(root);
        app.Use((ctx, next) =>
        {
            RewriteRoomLinks(ctx, files);
            return ServeBrotliWithoutSupport(ctx, files) ?? next(ctx);
        });
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = new UnityContentTypeProvider(),
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                string? encoding = EncodingOf(ctx.File.Name);
                if (encoding != null)
                {
                    headers.ContentEncoding = encoding;
                    headers.Vary = HeaderNames.AcceptEncoding;
                }
                headers.CacheControl = "no-cache"; // dev builds change often
            },
        });
        return root;
    }

    /// <summary>"/sala/ABCDE" serves index.html; relative assets requested under /sala/ resolve from the build root.</summary>
    private static void RewriteRoomLinks(HttpContext ctx, IFileProvider files)
    {
        string? path = ctx.Request.Path.Value;
        if (path == null || !path.StartsWith("/sala/", StringComparison.OrdinalIgnoreCase)) return;
        string rest = path["/sala".Length..];
        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 1 && !files.GetFileInfo(rest).Exists)
        {
            ctx.Request.Path = "/index.html";
        }
        else if (files.GetFileInfo(rest).Exists)
        {
            ctx.Request.Path = rest; // "/sala/Build/x.js" (page at /sala/ABCDE)
        }
        else if (segments.Length > 1)
        {
            string inner = "/" + string.Join('/', segments.Skip(1)); // "/sala/ABCDE/Build/x.js" (page at /sala/ABCDE/)
            if (files.GetFileInfo(inner).Exists) ctx.Request.Path = inner;
        }
    }

    /// <summary>Browsers only accept "Content-Encoding: br" over HTTPS. On plain HTTP (LAN testing) the .br file is
    /// decompressed on the fly so a Brotli build still loads.</summary>
    private static Task? ServeBrotliWithoutSupport(HttpContext ctx, IFileProvider files)
    {
        string? path = ctx.Request.Path.Value;
        if (path == null || !path.EndsWith(".br", StringComparison.OrdinalIgnoreCase)) return null;
        if (ctx.Request.Headers.AcceptEncoding.ToString().Contains("br", StringComparison.OrdinalIgnoreCase)) return null;
        var file = files.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory) return null;
        return SendDecompressedAsync(ctx, file);
    }

    private static async Task SendDecompressedAsync(HttpContext ctx, IFileInfo file)
    {
        new UnityContentTypeProvider().TryGetContentType(file.Name, out var contentType);
        ctx.Response.ContentType = contentType ?? "application/octet-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        if (HttpMethods.IsHead(ctx.Request.Method)) return;
        await using var source = file.CreateReadStream();
        await using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        await brotli.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }

    private static string? EncodingOf(string fileName) =>
        fileName.EndsWith(".br", StringComparison.OrdinalIgnoreCase) ? "br"
        : fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? "gzip"
        : null;

    /// <summary>Content types of the file under the compression suffix (x.wasm.br is application/wasm).</summary>
    public sealed class UnityContentTypeProvider : IContentTypeProvider
    {
        public bool TryGetContentType(string subpath, out string contentType)
        {
            bool compressed = EncodingOf(subpath) != null;
            string inner = compressed ? subpath[..^3] : subpath; // ".br" and ".gz" are both 3 chars
            switch (Path.GetExtension(inner).ToLowerInvariant())
            {
                case ".wasm": contentType = "application/wasm"; return true;
                case ".js": contentType = "application/javascript"; return true;
                case ".data":
                case ".unityweb":
                case ".bundle": contentType = "application/octet-stream"; return true;
                // The Android app is offered next to the Web build (https://<host>/pitacor.apk).
                case ".apk": contentType = "application/vnd.android.package-archive"; return true;
            }
            if (Defaults.TryGetContentType(inner, out var known))
            {
                contentType = known;
                return true;
            }
            contentType = "application/octet-stream";
            return compressed;
        }
    }
}

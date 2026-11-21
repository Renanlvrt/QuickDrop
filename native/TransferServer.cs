using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace QuickDrop;

internal sealed class TransferServer : IAsyncDisposable
{
    private const long MaxUpload = 10L * 1024 * 1024 * 1024;
    private readonly WebApplication app;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Action<string>? received;
    public string Url { get; }

    private TransferServer(WebApplication app, string url, Action<string>? received)
    {
        this.app = app;
        Url = url;
        this.received = received;
        _ = ExpireAsync();
    }

    public static async Task<TransferServer> StartAsync(string? destination, IReadOnlyList<string> selected, Action<string>? received = null, IPAddress? listenAddress = null)
    {
        if (destination is null && selected.Count == 0) throw new ArgumentException("Choose a file first");
        foreach (string file in selected) if (!File.Exists(file)) throw new FileNotFoundException("A selected file is missing", file);

        IPAddress address = listenAddress ?? FindPrivateAddress();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        string root = $"/t/{token}";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => {
            options.Listen(address, 0);
            options.AllowSynchronousIO = true; // ZipArchive writes its small central directory synchronously on dispose.
            options.Limits.MaxRequestBodySize = MaxUpload;
            options.Limits.MaxConcurrentConnections = 8;
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        var app = builder.Build();
        IPAddress? pairedAddress = null;
        object pairLock = new();
        bool PairOrCheck(HttpContext context)
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null) return false;
            lock (pairLock) { pairedAddress ??= remote; return pairedAddress.Equals(remote); }
        }
        bool IsPaired(HttpContext context)
        {
            var remote = context.Connection.RemoteIpAddress;
            lock (pairLock) return remote is not null && pairedAddress is not null && pairedAddress.Equals(remote);
        }
        app.Use(async (context, next) => {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next();
        });

        long uploadedBytes = 0;
        if (destination is not null)
        {
            app.MapGet(root, (HttpContext context) => {
                if (!PairOrCheck(context)) return Results.StatusCode(403);
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'";
                return Results.Content(UploadPage(), "text/html; charset=utf-8");
            });
            app.MapPost(root + "/upload", async (HttpContext context) => {
                if (!IsPaired(context)) return Results.StatusCode(403);
                string name = SafeName(context.Request.Query["name"].ToString());
                if (context.Request.ContentLength > MaxUpload) return Results.Json(new { error = "File exceeds 10 GB" }, statusCode: 413);
                Directory.CreateDirectory(destination);
                string path = UniquePath(destination, name);
                bool created = false;
                try
                {
                    await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    created = true;
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    int count;
                    while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
                    {
                        total += count;
                        if (total > MaxUpload || Interlocked.Add(ref uploadedBytes, count) > MaxUpload) throw new InvalidDataException("Transfer session exceeds 10 GB");
                        await file.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
                    }
                    received?.Invoke(Path.GetFileName(path));
                    return Results.Json(new { ok = true, name = Path.GetFileName(path) });
                }
                catch (Exception error)
                {
                    if (created) { try { File.Delete(path); } catch { /* already removed */ } }
                    return Results.Json(new { error = error is InvalidDataException ? error.Message : "Upload failed" }, statusCode: 422);
                }
            });
        }
        else
        {
            app.MapGet(root + "/download", async (HttpContext context) => {
                if (!PairOrCheck(context)) { context.Response.StatusCode = 403; return; }
                if (selected.Count == 1)
                {
                    string file = selected[0];
                    if (!File.Exists(file)) { context.Response.StatusCode = 404; return; }
                    await Results.File(file, ContentType(file), Path.GetFileName(file), enableRangeProcessing: true).ExecuteAsync(context);
                    return;
                }
                context.Response.ContentType = "application/zip";
                context.Response.Headers.ContentDisposition = "attachment; filename=QuickDrop-files.zip";
                using var archive = new ZipArchive(context.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in selected)
                {
                    string name = Path.GetFileName(file);
                    string stem = Path.GetFileNameWithoutExtension(name);
                    string extension = Path.GetExtension(name);
                    for (int number = 2; !used.Add(name); number++) name = $"{stem} ({number}){extension}";
                    await using var input = File.OpenRead(file);
                    await using var output = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                    await input.CopyToAsync(output, context.RequestAborted);
                }
            });
        }
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        string addressText = addresses?.Addresses.FirstOrDefault() ?? throw new InvalidOperationException("No LAN listener");
        int port = new Uri(addressText).Port;
        string url = $"http://{address}:{port}{root}{(destination is null ? "/download" : "")}";
        return new TransferServer(app, url, received);
    }

    private async Task ExpireAsync()
    {
        try { await Task.Delay(TimeSpan.FromMinutes(10), lifetime.Token); await app.StopAsync(); }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await app.StopAsync();
        await app.DisposeAsync();
        lifetime.Dispose();
    }

    private static IPAddress FindPrivateAddress()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up)
            .OrderBy(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1);
        foreach (var adapter in adapters)
            foreach (var entry in adapter.GetIPProperties().UnicastAddresses)
            {
                var ip = entry.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                byte[] bytes = ip.GetAddressBytes();
                if (bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return ip;
            }
        throw new InvalidOperationException("Connect the laptop and iPhone to the same Wi-Fi network");
    }

    internal static string SafeName(string raw)
    {
        string name = Path.GetFileName(raw.Replace('/', Path.DirectorySeparatorChar)).TrimEnd('.', ' ');
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray());
        if (name.Length > 180) name = name[..180];
        string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9") name = "_" + name;
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static string UniquePath(string folder, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        string path = Path.Combine(folder, name);
        for (int number = 2; File.Exists(path); number++) path = Path.Combine(folder, $"{stem} ({number}){extension}");
        return path;
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif",
        ".heic" => "image/heic", ".webp" => "image/webp", ".mp4" => "video/mp4", ".mov" => "video/quicktime",
        _ => "application/octet-stream"
    };

    private static string UploadPage() => """
        <!doctype html><html lang="en"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="referrer" content="no-referrer"><title>QuickDrop</title>
        <style>body{font:17px system-ui;max-width:32rem;margin:2rem auto;padding:1rem;background:#f4f7fa;color:#17343b}button,input{font:inherit;display:block;box-sizing:border-box;width:100%;margin:1rem 0;padding:.9rem;border-radius:.8rem}button{background:#096c75;color:white;border:0}p{line-height:1.5}</style>
        <h1>Send to laptop</h1><p>Choose photos, videos or documents. They save to the folder selected on the laptop.</p>
        <input id="files" type="file" multiple><button id="send">Send files</button><p id="status" role="status"></p>
        <script>
        document.querySelector('#send').onclick=async()=>{const files=[...document.querySelector('#files').files],status=document.querySelector('#status');if(!files.length){status.textContent='Choose files first.';return}for(let i=0;i<files.length;i++){status.textContent='Sending '+(i+1)+' of '+files.length+': '+files[i].name;try{const response=await fetch(location.pathname+'/upload?name='+encodeURIComponent(files[i].name),{method:'POST',body:files[i]});const result=await response.json();if(!response.ok)throw Error(result.error||'Transfer failed')}catch(error){status.textContent='Could not send '+files[i].name+': '+error.message;return}}status.textContent='All files saved on the laptop.'};
        </script></html>
        """;
}

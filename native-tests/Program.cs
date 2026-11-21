using System.Net;
using System.IO.Compression;
using System.Text;
using QuickDrop;

if (args.Contains("--screenshots")) { ScreenshotTool.Capture(); return; }

string folder = Path.Combine(Path.GetTempPath(), "quickdrop-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
using var client = new HttpClient();
try
{
    string destination = Path.Combine(folder, "received");
    await using (var server = await TransferServer.StartAsync(destination, [], listenAddress: IPAddress.Loopback))
    {
        using var unpaired = await client.PostAsync(server.Url + "/upload?name=blocked.jpg", new ByteArrayContent([1]));
        Check(unpaired.StatusCode == HttpStatusCode.Forbidden, "Upload worked before QR pairing");
        string page = await client.GetStringAsync(server.Url);
        Check(page.Contains("Send to laptop"), "Upload page missing");
        using var pageResponse = await client.GetAsync(server.Url);
        Check(pageResponse.Headers.TryGetValues("Content-Security-Policy", out _), "Upload page lacks CSP");
        string badUrl = server.Url.Replace("/t/", "/t/wrong-");
        Check((await client.GetAsync(badUrl)).StatusCode == HttpStatusCode.NotFound, "Wrong token was accepted");
        byte[] bytes = [0, 1, 2, 255];
        using var response = await client.PostAsync(server.Url + "/upload?name=..%2Fphoto.jpg", new ByteArrayContent(bytes));
        Check(response.IsSuccessStatusCode, "Upload failed");
        Check(File.ReadAllBytes(Path.Combine(destination, "photo.jpg")).SequenceEqual(bytes), "Upload bytes differ");
        using var again = await client.PostAsync(server.Url + "/upload?name=photo.jpg", new ByteArrayContent([7, 8]));
        Check(again.IsSuccessStatusCode, "Second upload failed");
        Check(File.ReadAllBytes(Path.Combine(destination, "photo.jpg")).SequenceEqual(bytes), "First file was overwritten");
        Check(File.ReadAllBytes(Path.Combine(destination, "photo (2).jpg")).SequenceEqual(new byte[] { 7, 8 }), "Unique name failed");
        Check(TransferServer.SafeName("CON.txt") == "_CON.txt", "Windows device name was not escaped");
    }

    string source = Path.Combine(folder, "sample.txt");
    File.WriteAllText(source, "original bytes", Encoding.UTF8);
    await using (var server = await TransferServer.StartAsync(null, [source], listenAddress: IPAddress.Loopback))
    {
        using var response = await client.GetAsync(server.Url);
        Check(response.IsSuccessStatusCode, "Download failed");
        Check(response.Content.Headers.ContentDisposition?.DispositionType == "attachment", "Download is not an attachment");
        Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(File.ReadAllBytes(source)), "Download bytes differ");
    }
    string second = Path.Combine(folder, "second.txt");
    File.WriteAllText(second, "second file");
    await using (var server = await TransferServer.StartAsync(null, [source, second], listenAddress: IPAddress.Loopback))
    {
        using var response = await client.GetAsync(server.Url);
        Check(response.IsSuccessStatusCode, "ZIP response failed: " + response.StatusCode);
        Check(response.Content.Headers.ContentDisposition?.ToString().Contains("attachment", StringComparison.OrdinalIgnoreCase) == true, "ZIP is not a download: " + response.Headers + response.Content.Headers);
        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
        Check(archive.Entries.Count == 2, "ZIP file count differs");
        using var first = new StreamReader(archive.Entries[0].Open());
        Check(await first.ReadToEndAsync() == File.ReadAllText(source), "ZIP member bytes differ");
    }
    Console.WriteLine("QuickDrop loopback security and transfer checks passed");
}
finally { Directory.Delete(folder, recursive: true); }

static void Check(bool value, string message) { if (!value) throw new Exception(message); }

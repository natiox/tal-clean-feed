using System.Text.RegularExpressions;
using System.Xml.Linq;

const string SourceFeed = "https://www.thisamericanlife.org/podcast/rss.xml";
var outPath = args.Length > 0 ? args[0] : "docs/tal-clean.xml";

using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
http.DefaultRequestHeaders.UserAgent.ParseAdd("tal-beeped-feed/1.0 (personal use)");

var cleanRx = new Regex(
    @"https?://www\.thisamericanlife\.org/sites/default/files/audio/upload/schedule/clean/\d+\.mp3",
    RegexOptions.IgnoreCase);

var doc = XDocument.Parse(await http.GetStringAsync(SourceFeed));
var channel = doc.Root!.Element("channel")!;

var feedUrl = Environment.GetEnvironmentVariable("FEED_URL");
if (!string.IsNullOrWhiteSpace(feedUrl))
{
    XNamespace atom = "http://www.w3.org/2005/Atom";
    XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

    var selfLink = channel.Element(atom + "link");
    selfLink?.SetAttributeValue("href", feedUrl);

    channel.Element(itunes + "new-feed-url")?.Remove();
}

// Make it obvious in your podcast app that this is the modified feed
var title = channel.Element("title");
if (title != null && !title.Value.Contains("Beeped"))
    title.Value += " (Beeped)";

int swapped = 0, total = 0;

foreach (var item in channel.Elements("item"))
{
    total++;
    var link = item.Element("link")?.Value;
    var enclosure = item.Element("enclosure");
    if (string.IsNullOrWhiteSpace(link) || enclosure == null) continue;

    try
    {
        var html = await http.GetStringAsync(link);
        var m = cleanRx.Match(html);
        if (!m.Success) continue;

        var cleanUrl = m.Value;

        // Confirm the file really exists and grab its size
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, cleanUrl));
        if (!head.IsSuccessStatusCode) continue;

        enclosure.SetAttributeValue("url", cleanUrl);
        enclosure.SetAttributeValue("type", "audio/mpeg");
        if (head.Content.Headers.ContentLength is long len)
            enclosure.SetAttributeValue("length", len.ToString());

        swapped++;
        Console.WriteLine($"Beeped: {link}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Skipping {link}: {ex.Message}");
    }

    await Task.Delay(500); // be polite to their server
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
doc.Save(outPath);
Console.WriteLine($"Done. {swapped}/{total} episodes use beeped audio.");
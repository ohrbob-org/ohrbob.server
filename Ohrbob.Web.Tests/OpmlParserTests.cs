using System.Text;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class OpmlParserTests
{
    private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    private const string SampleOpml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no"" ?>
<opml version=""2.0"">
  <head>
    <title>AntennaPod Subscriptions</title>
  </head>
  <body>
    <outline text=""Alternativlos"" title=""Alternativlos"" type=""rss"" xmlUrl=""http://alternativlos.org/alternativlos.rss"" htmlUrl=""https://www.alternativlos.org/"" />
    <outline text=""Chaosradio"" title=""Chaosradio"" type=""rss"" xmlUrl=""https://chaosradio.de/feed/m4a"" htmlUrl=""https://chaosradio.de"" />
  </body>
</opml>";

    [Test]
    public void ExtractFeedUrls_ReturnsXmlUrlsInDocumentOrder()
    {
        var urls = OpmlParser.ExtractFeedUrls(ToStream(SampleOpml));

        Assert.That(urls, Is.EqualTo(new[]
        {
            "http://alternativlos.org/alternativlos.rss",
            "https://chaosradio.de/feed/m4a"
        }));
    }

    [Test]
    public void ExtractFeedUrls_ReadsOutlinesNestedInGroups()
    {
        const string opml = @"<opml version=""2.0"">
  <body>
    <outline text=""Group"">
      <outline text=""A"" type=""rss"" xmlUrl=""https://a.example/feed"" />
      <outline text=""B"" type=""rss"" xmlUrl=""https://b.example/feed"" />
    </outline>
  </body>
</opml>";

        var urls = OpmlParser.ExtractFeedUrls(ToStream(opml));

        Assert.That(urls, Is.EqualTo(new[] { "https://a.example/feed", "https://b.example/feed" }));
    }

    [Test]
    public void ExtractFeedUrls_IgnoresOutlinesWithoutXmlUrl()
    {
        const string opml = @"<opml version=""2.0"">
  <body>
    <outline text=""Group without url"" />
    <outline text=""A"" type=""rss"" xmlUrl=""https://a.example/feed"" />
    <outline text=""Blank"" type=""rss"" xmlUrl="""" />
  </body>
</opml>";

        var urls = OpmlParser.ExtractFeedUrls(ToStream(opml));

        Assert.That(urls, Is.EqualTo(new[] { "https://a.example/feed" }));
    }

    [Test]
    public void ExtractFeedUrls_DeduplicatesPreservingFirstOccurrence()
    {
        const string opml = @"<opml version=""2.0"">
  <body>
    <outline text=""A"" type=""rss"" xmlUrl=""https://a.example/feed"" />
    <outline text=""B"" type=""rss"" xmlUrl=""https://b.example/feed"" />
    <outline text=""A again"" type=""rss"" xmlUrl=""https://a.example/feed"" />
  </body>
</opml>";

        var urls = OpmlParser.ExtractFeedUrls(ToStream(opml));

        Assert.That(urls, Is.EqualTo(new[] { "https://a.example/feed", "https://b.example/feed" }));
    }

    [Test]
    public void ExtractFeedUrls_ReturnsEmptyWhenNoFeedsPresent()
    {
        const string opml = @"<opml version=""2.0""><head><title>Empty</title></head><body></body></opml>";

        var urls = OpmlParser.ExtractFeedUrls(ToStream(opml));

        Assert.That(urls, Is.Empty);
    }

    [Test]
    public void ExtractFeedUrls_ThrowsWhenRootIsNotOpml()
    {
        const string xml = @"<html><body><p>not opml</p></body></html>";

        Assert.Throws<InvalidDataException>(() => OpmlParser.ExtractFeedUrls(ToStream(xml)));
    }

    [Test]
    public void ExtractFeedUrls_ThrowsOnMalformedXml()
    {
        const string xml = @"<opml version=""2.0""><body><outline xmlUrl=""https://a.example/feed""></body>";

        Assert.Throws<System.Xml.XmlException>(() => OpmlParser.ExtractFeedUrls(ToStream(xml)));
    }
}

using Ohrbob.Api.Services;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedMetadataParserTests
{
    private const string XmlWithUpdateFrequency = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:podcasthttps="https://podcastindex.org/namespace/1.0">
          <channel>
            <title>Show</title>
            <podcasthttps:updateFrequency rrule="FREQ=WEEKLY;BYDAY=MO,WE">twice weekly</podcasthttps:updateFrequency>
            <item><title>Episode</title></item>
          </channel>
        </rss>
        """;

    [Test]
    public void ParseUpdateFrequencyRrule_ReturnsTheDeclaredRrule()
    {
        var result = FeedMetadataParser.ParseUpdateFrequencyRrule(XmlWithUpdateFrequency);

        Assert.That(result, Is.EqualTo("FREQ=WEEKLY;BYDAY=MO,WE"));
    }

    [Test]
    public void ParseUpdateFrequencyRrule_ReturnsNullWhenTheElementIsMissing()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel><title>Show</title></channel></rss>
            """;

        Assert.That(FeedMetadataParser.ParseUpdateFrequencyRrule(xml), Is.Null);
    }

    [Test]
    public void ParseUpdateFrequencyRrule_ReturnsNullWhenTheRruleAttributeIsEmpty()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:p="https://podcastindex.org/namespace/1.0">
              <channel><p:updateFrequency>daily</p:updateFrequency></channel>
            </rss>
            """;

        Assert.That(FeedMetadataParser.ParseUpdateFrequencyRrule(xml), Is.Null);
    }

    [Test]
    public void ParseUpdateFrequencyRrule_ReturnsNullForInvalidXml()
    {
        Assert.That(FeedMetadataParser.ParseUpdateFrequencyRrule("this is not xml"), Is.Null);
    }

    [Test]
    public void ParseKeywords_ReturnsTheDeclaredItunesKeywords()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
              <channel>
                <itunes:keywords>comedy, interviews ,, Storytelling</itunes:keywords>
              </channel>
            </rss>
            """;

        var result = FeedMetadataParser.ParseKeywords(xml);

        Assert.That(result, Is.EqualTo(new[] { "comedy", "interviews", "Storytelling" }));
    }

    [Test]
    public void ParseKeywords_ReturnsNullWhenTheElementIsMissing()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel><title>Show</title></channel></rss>
            """;

        Assert.That(FeedMetadataParser.ParseKeywords(xml), Is.Null);
    }
}

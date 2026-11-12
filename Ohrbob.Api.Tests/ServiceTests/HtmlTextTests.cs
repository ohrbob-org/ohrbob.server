using Ohrbob.Server.Shared.Text;

namespace Ohrbob.Api.Tests.ServiceTests;

public class HtmlTextTests
{
    [Test]
    public void ToPlainText_DecodesNumericCharacterReferences()
    {
        // This is the shape feeds like radioeins deliver: HTML entities inside CDATA, so the
        // XML parser leaves "&#252;" untouched and it must be decoded before display.
        var result = HtmlText.ToPlainText("R&#252;cktritt von B&#228;rbel und gr&#246;&#223;te");

        Assert.That(result, Is.EqualTo("Rücktritt von Bärbel und größte"));
    }

    [Test]
    public void ToPlainText_DecodesNamedEntities()
    {
        Assert.That(HtmlText.ToPlainText("Tom &amp; Jerry &quot;quoted&quot;"),
            Is.EqualTo("Tom & Jerry \"quoted\""));
    }

    [Test]
    public void ToPlainText_StripsHtmlTags()
    {
        Assert.That(HtmlText.ToPlainText("<p>Hello <b>World</b></p>"), Is.EqualTo("Hello World"));
    }

    [Test]
    public void ToPlainText_PreservesEscapedMarkupAsLiteralText()
    {
        // Tags are stripped before entities are decoded, so escaped markup survives.
        Assert.That(HtmlText.ToPlainText("&lt;b&gt;not bold&lt;/b&gt;"), Is.EqualTo("<b>not bold</b>"));
    }

    [Test]
    public void ToPlainText_CombinesTagStrippingAndEntityDecoding()
    {
        Assert.That(HtmlText.ToPlainText("<p>R&#252;cktritt &amp; gr&#246;&#223;te</p>"),
            Is.EqualTo("Rücktritt & größte"));
    }

    [Test]
    public void ToDisplayText_StripsTagsAndDecodesEntities()
    {
        Assert.That(HtmlText.ToDisplayText("<p>R&#252;cktritt &amp; gr&#246;&#223;te</p>"),
            Is.EqualTo("Rücktritt & größte"));
    }

    [Test]
    public void ToDisplayText_StripsMarkupRevealedByEntityDecoding()
    {
        Assert.That(HtmlText.ToDisplayText("&lt;a href=&quot;https://example.com&quot;&gt;My Show&lt;/a&gt;"),
            Is.EqualTo("My Show"));
        Assert.That(HtmlText.ToDisplayText("&amp;lt;b&amp;gt;Nested&amp;lt;/b&amp;gt;"),
            Is.EqualTo("Nested"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void ToDisplayText_ReturnsEmptyForMissingInput(string? input)
    {
        Assert.That(HtmlText.ToDisplayText(input!), Is.EqualTo(string.Empty));
    }

    [TestCase(null)]
    [TestCase("")]
    public void ToPlainText_ReturnsEmptyForMissingInput(string? input)
    {
        Assert.That(HtmlText.ToPlainText(input!), Is.EqualTo(string.Empty));
    }
}

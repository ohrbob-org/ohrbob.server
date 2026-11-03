using Ohrbob.Server.Shared.Helpers;

namespace Ohrbob.Api.Tests.ServiceTests;

public class ShareCodeGeneratorTests
{
    [Test]
    public void Generate_ReturnsTenUrlSafeCharacters()
    {
        var code = ShareCodeGenerator.Generate();

        Assert.That(code, Has.Length.EqualTo(10));
        Assert.That(code, Does.Match("^[A-Za-z0-9]{10}$"));
    }

    [Test]
    public void Generate_ProducesDifferentCodes()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => ShareCodeGenerator.Generate()).ToHashSet();

        Assert.That(codes, Has.Count.EqualTo(100));
    }
}

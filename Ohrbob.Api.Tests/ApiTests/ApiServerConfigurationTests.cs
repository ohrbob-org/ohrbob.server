using Ohrbob.Api.Configuration;

namespace Ohrbob.Api.Tests.ApiTests;

public class ApiServerConfigurationTests
{
    [Test]
    public void Validate_DefaultInstance_DoesNotThrow()
    {
        var config = new ApiServerConfiguration();
        Assert.DoesNotThrow(() => config.Validate());
    }

    [Test]
    public void Validate_EmptyVersion_Throws()
    {
        var config = new ApiServerConfiguration { Version = "" };
        Assert.Throws<Exception>(() => config.Validate());
    }

    [Test]
    public void Validate_EmptyTag_Throws()
    {
        var config = new ApiServerConfiguration { Tag = "" };
        Assert.Throws<Exception>(() => config.Validate());
    }
}

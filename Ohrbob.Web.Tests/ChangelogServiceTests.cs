using NUnit.Framework;
using Ohrbob.Web.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Threading.Tasks;

namespace Ohrbob.Web.Tests;

public class ChangelogServiceTests
{
    private string _testDir;
    
    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ohrbob_changelog_test_" + System.Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }
    
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Test]
    public async Task GetChangelogHtmlAsync_FileExists_ReturnsCachedHtml()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ChangelogService>>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_testDir);
        
        var changelogPath = Path.Combine(_testDir, "changelog.md");
        await File.WriteAllTextAsync(changelogPath, "# Test Changelog\n* Feature 1");
        
        var service = new ChangelogService(mockLogger.Object, mockEnv.Object);
        
        // Act
        var html = await service.GetChangelogHtmlAsync();
        
        // Assert
        Assert.That(html, Is.Not.Null);
        Assert.That(html, Does.Contain("<h1 id=\"test-changelog\">Test Changelog</h1>"));
        Assert.That(html, Does.Contain("<li>Feature 1</li>"));
    }

    [Test]
    public async Task GetChangelogHtmlAsync_FileNotFound_ReturnsNull()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ChangelogService>>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_testDir);
        
        var service = new ChangelogService(mockLogger.Object, mockEnv.Object);
        
        // Act
        var html = await service.GetChangelogHtmlAsync();
        
        // Assert
        Assert.That(html, Is.Null);
    }
    
    [Test]
    public async Task GetChangelogHtmlAsync_CachesResult()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ChangelogService>>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(_testDir);
        
        var changelogPath = Path.Combine(_testDir, "changelog.md");
        await File.WriteAllTextAsync(changelogPath, "# Initial");
        
        var service = new ChangelogService(mockLogger.Object, mockEnv.Object);
        
        // Act 1
        var html1 = await service.GetChangelogHtmlAsync();
        
        // Modify file
        await File.WriteAllTextAsync(changelogPath, "# Modified");
        
        // Act 2
        var html2 = await service.GetChangelogHtmlAsync();
        
        // Assert
        Assert.That(html1, Is.EqualTo(html2));
    }
}

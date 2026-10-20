using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Markdig;
using System.IO;
using System.Threading.Tasks;

namespace Ohrbob.Web.Services;

public class ChangelogService
{
    private readonly ILogger<ChangelogService> _logger;
    private readonly IWebHostEnvironment _env;
    private string? _cachedHtml;

    public ChangelogService(ILogger<ChangelogService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async Task<string?> GetChangelogHtmlAsync()
    {
        if (_cachedHtml != null)
        {
            return _cachedHtml;
        }

        var changelogPath = Path.Combine(_env.ContentRootPath, "changelog.md");
        if (!File.Exists(changelogPath))
        {
            _logger.LogInformation("changelog.md not found at {Path}", changelogPath);
            return null;
        }

        try
        {
            var markdown = await File.ReadAllTextAsync(changelogPath);
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            _cachedHtml = Markdown.ToHtml(markdown, pipeline);
            return _cachedHtml;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to read or parse changelog.md");
            return null;
        }
    }
}

using CodeHollow.FeedReader;

namespace Ohrbob.Api.Services;

/// <summary>The parts of a parsed feed that Ohrbob stores or schedules from.</summary>
public sealed record ParsedFeed(
    string? Title,
    string? Description,
    string? ImageUrl,
    string? Xml,
    IReadOnlyList<FeedItem> Items);

/// <summary>Fetches and parses a podcast feed. Abstracted so the update logic is testable.</summary>
public interface IFeedReader
{
    Task<ParsedFeed> ReadAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class CodeHollowFeedReader : IFeedReader
{
    public async Task<ParsedFeed> ReadAsync(string url, CancellationToken cancellationToken = default)
    {
        var parsed = await FeedReader.ReadAsync(url, cancellationToken);
        return new ParsedFeed(
            parsed?.Title,
            parsed?.Description,
            parsed?.ImageUrl,
            parsed?.OriginalDocument as string,
            parsed?.Items?.ToList() ?? new List<FeedItem>());
    }
}

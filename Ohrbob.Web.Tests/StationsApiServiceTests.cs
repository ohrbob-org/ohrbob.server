using Ohrbob.Server.Shared.Requests;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class StationsApiServiceTests
{
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private Mock<IHttpClientFactory> _httpClientFactoryMock;
    private Mock<ILogger<OhrbobApiService>> _loggerMock;
    private StationsApiService _service;
    
    [SetUp]
    public void Setup()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        
        var client = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://test.api/")
        };

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(nameof(OhrbobApiService)))
            .Returns(client);

        _loggerMock = new Mock<ILogger<OhrbobApiService>>();

        var options = Options.Create(new OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "test-api-key"
        });

        _service = new StationsApiService(_loggerMock.Object, options, _httpClientFactoryMock.Object);
    }

    [Test]
    public async Task GetStationByIdAsync_SendsCorrectRequestAndReturnsStation()
    {
        var stationId = "st-123";
        var station = new Station { Id = stationId, Name = "St 123" };
        
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(station))
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri.ToString() == $"https://test.api/stations/{stationId}"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var result = await _service.GetStationByIdAsync(stationId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("St 123"));
    }

    [Test]
    public async Task GetStationsByAccountIdAsync_SendsCorrectRequestAndReturnsStations()
    {
        var accountId = "acc-123";
        var stations = new List<StationWithFeedsDto> { new StationWithFeedsDto { Station = new Station { Name = "St 1" } } };
        
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(stations))
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri.ToString() == $"https://test.api/stations/account/{accountId}"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var result = await _service.GetStationsByAccountIdAsync(accountId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Station.Name, Is.EqualTo("St 1"));
    }
    
    [Test]
    public async Task CreateStationAsync_SendsCorrectRequestAndReturnsCreatedStation()
    {
        var accountId = "acc-123";
        var station = new Station { Name = "New St", Feeds = new[] { "url" } };
        
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(station))
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Post && 
                    req.RequestUri.ToString() == $"https://test.api/stations/{accountId}"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var result = await _service.CreateStationAsync(accountId, station);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("New St"));
    }
    
    [Test]
    public async Task UpdateStationAsync_SendsCorrectRequestAndReturnsUpdatedStation()
    {
        var station = new Station { Id = "st-1", Name = "Updated St" };
        
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(station))
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Put && 
                    req.RequestUri.ToString() == $"https://test.api/stations"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var result = await _service.UpdateStationAsync(station);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Updated St"));
    }
    
    [Test]
    public async Task DeleteStationAsync_SendsCorrectRequest()
    {
        var stationId = "st-1";
        
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Delete && 
                    req.RequestUri.ToString() == $"https://test.api/stations/{stationId}"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        await _service.DeleteStationAsync(stationId);
        
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Delete),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Test]
    public async Task GetStationCompletedPlaylistAsync_SendsCorrectRequestAndReturnsCompletedPlaylist()
    {
        var stationId = "st-1";
        var profileId = "prof-123";
        var completedList = new List<EpisodePlayStateDto>
        {
            new EpisodePlayStateDto { Episode = new Episode { Id = "ep-1", Title = "Completed 1" }, PositionSeconds = 120, IsActive = false }
        };

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(completedList))
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri.ToString() == $"https://test.api/stations/{stationId}/completed?profileId={profileId}"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);

        var result = await _service.GetStationCompletedPlaylistAsync(stationId, profileId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Episode.Title, Is.EqualTo("Completed 1"));
        Assert.That(result[0].IsActive, Is.False);
    }

    [Test]
    public async Task UpdateStationAsync_LimitExceeded_ThrowsLimitExceededException()
    {
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Forbidden,
            Content = new StringContent(
                "{\"title\":\"Limit exceeded\",\"detail\":\"Feed limit reached for this station.\"}",
                System.Text.Encoding.UTF8, "application/json")
        };
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Put),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var ex = Assert.ThrowsAsync<LimitExceededException>(
            async () => await _service.UpdateStationAsync(new Station { Id = "st-1" }));
        Assert.That(ex!.Message, Is.EqualTo("Feed limit reached for this station."));
    }

    [Test]
    public async Task AddFeedsAsync_PostsUrlsToTheStationFeedsEndpoint_AndReturnsResult()
    {
        var stationId = "st-1";
        var urls = new List<string> { "https://a.example/feed", "https://b.example/feed" };
        var importResult = new FeedImportResultDto
        {
            Added = { urls[0] },
            NotAddedDueToLimit = { urls[1] }
        };

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(importResult))
        };

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString() == $"https://test.api/stations/{stationId}/feeds"),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedRequest = req;
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(response);

        var result = await _service.AddFeedsAsync(stationId, urls);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Added, Is.EqualTo(new[] { urls[0] }));
        Assert.That(result.NotAddedDueToLimit, Is.EqualTo(new[] { urls[1] }));

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Headers.Contains("X-API-KEY"), Is.True);
        var sent = JsonSerializer.Deserialize<FeedImportRequest>(capturedBody!);
        Assert.That(sent!.Urls, Is.EqualTo(urls));
    }
}

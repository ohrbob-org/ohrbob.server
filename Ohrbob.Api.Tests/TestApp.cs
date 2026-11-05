using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;

namespace Ohrbob.Api.Tests;


public class ApiTestApp<T> : WebApplicationFactory<T> where T : class
{
    public const string MongodbConnectionStringEnvVariable = "MONGODB_CONNECTION_STRING";
    public const string MongodbDatabaseNameEnvVariable = "MONGODB_DATABASE_NAME";
    private readonly Action<IServiceCollection>? _serviceOverride;
    private readonly Action<IConfigurationBuilder>? _configurationOverride;
    
    public string? ConnectionString { get;private set;}
    public string? DatabaseName { get; private set; }
    public HttpClient? DefaultClient { get; private set; }

    public MongoDbContext? MongoDbContext { get; private set; }
    
    /// <summary>
     
    /// </summary>
    /// <param name="serviceOverride"></param>
    /// <param name="configurationOverride"></param>
    /// <param name="setupMongoDbContext">Will create and regeister an instance of <see cref="MongoDbContext"/>. It will make sure this context has no databases. </param>
    /// <param name="createDefaultClient">If true the <see cref="DefaultClient"/> is populated automatically and the host is created automatically for you. </param>
    /// <param name="connectionString">Custom Connection string for use with <paramref name="setupMongoDbContext"/></param>
    public ApiTestApp(Action<IServiceCollection>? serviceOverride = null,
        Action<IConfigurationBuilder>? configurationOverride = null,
        bool setupMongoDbContext = false,
        bool createDefaultClient = true,
        string? connectionString = null,
        string? databaseName = null)
    {
        _serviceOverride = serviceOverride;
        _configurationOverride = configurationOverride;
        if (setupMongoDbContext)
        {
            if(connectionString == null)
                connectionString = Environment.GetEnvironmentVariable(MongodbConnectionStringEnvVariable) ?? "mongodb://localhost:27017";
            ConnectionString = connectionString;
            if(databaseName == null)
                databaseName = Environment.GetEnvironmentVariable(MongodbDatabaseNameEnvVariable) ?? "_testdb";
            DatabaseName = databaseName;
        }
        else
        {
            ConnectionString = null;
        }

        if (createDefaultClient)
            CreateAndStoreDefaultClient(); // ensures CreateHost is called
    }

    private void CreateAndStoreDefaultClient()
    {
        DefaultClient = CreateDefaultClient();
    }


    protected override IHost CreateHost(IHostBuilder builder)
    {
        
        builder.ConfigureHostConfiguration((c) =>
        {
            _configurationOverride?.Invoke(c);
        });
        builder.ConfigureServices((s) =>
        {
            if (ConnectionString != null)
            {
                MongoDbContext = CreateMongoContext(ConnectionString, DatabaseName);
                CleanUpMongoDb(MongoDbContext);
                s.AddSingleton(MongoDbContext);
            }

            _serviceOverride?.Invoke(s);
        });
        return base.CreateHost(builder);
    }

    
    public static MongoDbContext CreateMongoContext(string connectionString, string databaseName)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        MongoDbContext ctx = new MongoDbContext(connectionString, databaseName);
        return ctx;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Never touch MongoDB from the finalizer: an exception there aborts the test host.
        if (disposing)
            CleanUpMongoDb(MongoDbContext);

        if (DefaultClient != null)
            DefaultClient.Dispose();
    
    }

    public static void CleanUpMongoDb(MongoDbContext? ctx)
    {
        var command = new JsonCommand<BsonDocument>("{ dropDatabase: 1 }");
        
        if(ctx != null)
            ctx.GetDatabase()!.RunCommand(command);
    }
}
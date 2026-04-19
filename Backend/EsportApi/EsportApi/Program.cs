using Cassandra;
using EsportApi.Services;
using EsportApi.Services.Interfaces;
using EsportApi.Services.Workers;
using MongoDB.Driver;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                var isLocalHost = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                  uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                return isLocalHost && uri.Port >= 5173 && uri.Port <= 5190;
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSingleton<IMongoClient>(s => {
    
    var settings = MongoClientSettings.FromConnectionString("mongodb://127.0.0.1:27017/?replicaSet=rs0");
    settings.DirectConnection = false; 
    return new MongoClient(settings);
});
builder.Services.AddScoped<IShopService, EsportApi.Services.ShopService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(s => ConnectionMultiplexer.Connect("127.0.0.1:6379"));


builder.Services.AddSingleton<Cassandra.ISession>(s => {
    var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
    return cluster.Connect();
});
builder.Services.AddSingleton<CassandraSchemaInitializer>();
builder.Services.AddSingleton<DemoDataSeeder>();

builder.Services.AddScoped<IMatchmakingService, EsportApi.Services.MatchmakingService>();

builder.Services.AddSignalR();

builder.Services.AddHostedService<MatchmakingWorker>();

builder.Services.AddHostedService<LeaderboardSnapshotWorker>();

builder.Services.AddScoped<ITournamentService, TournamentService>();

builder.Services.AddScoped<ITeamService, TeamService>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddSingleton<ICassandraAuthService, CassandraAuthService>();
builder.Services.AddSingleton<IRedisRealtimePublisher, RedisRealtimePublisher>();

builder.Services.AddHostedService<TournamentWorker>();
builder.Services.AddHostedService<RedisRealtimeSubscriberWorker>();
var app = builder.Build();

await app.Services.GetRequiredService<CassandraSchemaInitializer>().InitializeAsync();
await app.Services.GetRequiredService<DemoDataSeeder>().InitializeAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("FrontendDev");

app.UseAuthorization();

app.MapControllers();

app.MapHub<EsportApi.Hubs.GameHub>("/gamehub");

app.Run();

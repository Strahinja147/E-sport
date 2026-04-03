using Cassandra;
using EsportApi.Services;
using EsportApi.Services.Interfaces;
using EsportApi.Services.Workers;
using MongoDB.Driver;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//Za turnir/mongo
builder.Services.AddScoped<ITournamentService, TournamentService>();

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

// Registracija tvojih servisa (Clan 2)
builder.Services.AddScoped<IMatchmakingService, EsportApi.Services.MatchmakingService>();

builder.Services.AddSignalR();

builder.Services.AddHostedService<MatchmakingWorker>();

builder.Services.AddHostedService<LeaderboardSnapshotWorker>();

builder.Services.AddHostedService<TournamentWorker>();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapHub<EsportApi.Hubs.GameHub>("/gamehub");

app.Run();

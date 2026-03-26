using Cassandra;
using MongoDB.Driver;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IMongoClient>(s => {
    
    var settings = MongoClientSettings.FromConnectionString("mongodb://127.0.0.1:27017/?replicaSet=rs0");
    settings.DirectConnection = false; 
    return new MongoClient(settings);
});
builder.Services.AddSingleton<IConnectionMultiplexer>(s => ConnectionMultiplexer.Connect("127.0.0.1:6379"));

builder.Services.AddSingleton<Cassandra.ISession>(s => {
    var cluster = Cluster.Builder().AddContactPoint("127.0.0.1").Build();
    return cluster.Connect();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

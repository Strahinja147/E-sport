using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using StackExchange.Redis;

[ApiController]
[Route("[controller]")]
public class DatabaseTestController : ControllerBase
{
    // Ovde "injektujemo" baze koje smo registrovali u Program.cs
    private readonly IMongoClient _mongoClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly Cassandra.ISession _cassandraSession;

    public DatabaseTestController(
        IMongoClient mongoClient,
        IConnectionMultiplexer redis,
        Cassandra.ISession cassandraSession)
    {
        _mongoClient = mongoClient;
        _redis = redis;
        _cassandraSession = cassandraSession;
    }
    [HttpGet("test-all")]
    public IActionResult TestAll()
    {
        try
        {
            var mongoOk = _mongoClient.ListDatabaseNames().Any();
            var redisPing = _redis.GetDatabase().Ping();
            var redisOk = redisPing > TimeSpan.Zero;
            var cassandraOk = _cassandraSession.Execute("SELECT now() FROM system.local") != null;
            if (mongoOk && redisOk && cassandraOk)
            {
                return Ok("Sve tri baze su uspešno povezane preko Dependency Injection-a!");
            }
            return StatusCode(500, "Neka od baza ne odgovara.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Greska pri povezivanju: {ex.Message}");
        }
    }
}
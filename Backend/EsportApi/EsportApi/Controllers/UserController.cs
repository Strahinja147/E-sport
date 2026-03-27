using EsportApi.Models;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EsportApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IMongoCollection<UserProfile> _usersCollection;

        public UserController(IMongoClient mongoClient)
        {
            var database = mongoClient.GetDatabase("EsportDb");
            _usersCollection = database.GetCollection<UserProfile>("Users");
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(string username)
        {
            var newUser = new UserProfile
            {
                // Generišemo novi MongoDB ID string ručno
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Username = username,
                EloRating = 1000,
                Coins = 500,
                Stats = new PlayerStatistics
                {
                    Wins = 0,
                    Losses = 0,
                    TotalGames = 0,
                    LastGameAt = DateTime.UtcNow
                }
            };

            await _usersCollection.InsertOneAsync(newUser);
            return Ok(newUser);
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            var users = await _usersCollection.Find(_ => true).ToListAsync();
            return Ok(users);
        }
    }
}
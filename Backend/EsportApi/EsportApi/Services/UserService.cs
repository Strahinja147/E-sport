using EsportApi.Models;
using EsportApi.Services.Interfaces;
using MongoDB.Driver;
using StackExchange.Redis;

namespace EsportApi.Services
{
    public class UserService : IUserService
    {
        private readonly IMongoCollection<UserProfile> _usersCollection;
        private readonly IDatabase _redisDb;

        public UserService(IMongoClient mongoClient, IConnectionMultiplexer redis)
        {
            var db = mongoClient.GetDatabase("EsportDb");
            _usersCollection = db.GetCollection<UserProfile>("Users");
            _redisDb = redis.GetDatabase();
        }

        public async Task<bool> SendFriendRequest(string senderId, string receiverId)
        {
            var sender = await _usersCollection.Find(u => u.Id == senderId).FirstOrDefaultAsync();
            var receiver = await _usersCollection.Find(u => u.Id == receiverId).FirstOrDefaultAsync();

            if (sender == null || receiver == null) throw new Exception("Jedan od korisnika ne postoji!");

            if (sender.Friends.Any(f => f.UserId == receiverId))
                throw new Exception("Zahtev je vec poslat ili ste vec prijatelji!");

            var friendForSender = new Friend
            {
                UserId = receiver.Id,
                Username = receiver.Username,
                Status = "Pending",
                RequestedByUserId = sender.Id
            };
            var updateSender = Builders<UserProfile>.Update.Push(u => u.Friends, friendForSender);
            await _usersCollection.UpdateOneAsync(u => u.Id == senderId, updateSender);

            var friendForReceiver = new Friend
            {
                UserId = sender.Id,
                Username = sender.Username,
                Status = "Pending",
                RequestedByUserId = sender.Id
            };
            var updateReceiver = Builders<UserProfile>.Update.Push(u => u.Friends, friendForReceiver);
            await _usersCollection.UpdateOneAsync(u => u.Id == receiverId, updateReceiver);

            return true;
        }

        public async Task<bool> SendFriendRequestByUsername(string senderId, string username)
        {
            var cleanedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(cleanedUsername))
            {
                throw new Exception("Korisnicko ime je obavezno.");
            }

            var sender = await _usersCollection.Find(u => u.Id == senderId).FirstOrDefaultAsync();
            if (sender == null)
            {
                throw new Exception("Posiljalac ne postoji.");
            }

            var receiver = await _usersCollection.Find(u => u.Username.ToLower() == cleanedUsername.ToLower()).FirstOrDefaultAsync();
            if (receiver == null)
            {
                throw new Exception("Korisnik sa tim korisnickim imenom ne postoji.");
            }

            if (receiver.Id == senderId)
            {
                throw new Exception("Ne mozes poslati zahtev samom sebi.");
            }

            return await SendFriendRequest(senderId, receiver.Id);
        }

        public async Task<bool> AcceptFriendRequest(string receiverId, string senderId)
        {
            var receiverFilter = Builders<UserProfile>.Filter.And(
                Builders<UserProfile>.Filter.Eq(u => u.Id, receiverId),
                Builders<UserProfile>.Filter.ElemMatch(u => u.Friends, f => f.UserId == senderId)
            );
            var receiverUpdate = Builders<UserProfile>.Update.Set("Friends.$.Status", "Accepted");
            await _usersCollection.UpdateOneAsync(receiverFilter, receiverUpdate);

            var senderFilter = Builders<UserProfile>.Filter.And(
                Builders<UserProfile>.Filter.Eq(u => u.Id, senderId),
                Builders<UserProfile>.Filter.ElemMatch(u => u.Friends, f => f.UserId == receiverId)
            );
            var senderUpdate = Builders<UserProfile>.Update.Set("Friends.$.Status", "Accepted");
            await _usersCollection.UpdateOneAsync(senderFilter, senderUpdate);

            return true;
        }

        public async Task<bool> RejectFriendRequest(string myUserId, string senderId)
        {
            return await RemoveFriendship(myUserId, senderId);
        }

        public async Task<bool> RemoveFriend(string myUserId, string friendId)
        {
            return await RemoveFriendship(myUserId, friendId);
        }

        public async Task<List<string>> GetOnlineFriendIds(string userId)
        {
            var user = await _usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            if (user == null)
            {
                return new List<string>();
            }

            var acceptedFriendIds = user.Friends
                .Where(friend => friend.Status == "Accepted")
                .Select(friend => friend.UserId)
                .Distinct()
                .ToList();

            var onlineFriendIds = new List<string>();
            foreach (var friendId in acceptedFriendIds)
            {
                if (await _redisDb.SetContainsAsync("online_players", friendId))
                {
                    onlineFriendIds.Add(friendId);
                }
            }

            return onlineFriendIds;
        }

        private async Task<bool> RemoveFriendship(string userId1, string userId2)
        {
            var update1 = Builders<UserProfile>.Update.PullFilter(u => u.Friends, f => f.UserId == userId2);
            await _usersCollection.UpdateOneAsync(u => u.Id == userId1, update1);

            var update2 = Builders<UserProfile>.Update.PullFilter(u => u.Friends, f => f.UserId == userId1);
            await _usersCollection.UpdateOneAsync(u => u.Id == userId2, update2);

            return true;
        }
    }
}

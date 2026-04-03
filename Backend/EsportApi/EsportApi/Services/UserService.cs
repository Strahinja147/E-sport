using EsportApi.Models;
using EsportApi.Services.Interfaces;
using MongoDB.Driver;

namespace EsportApi.Services
{
    public class UserService : IUserService
    {
        private readonly IMongoCollection<UserProfile> _usersCollection;

        public UserService(IMongoClient mongoClient)
        {
            var db = mongoClient.GetDatabase("EsportDb");
            _usersCollection = db.GetCollection<UserProfile>("Users");
        }

        public async Task<bool> SendFriendRequest(string senderId, string receiverId)
        {
            var sender = await _usersCollection.Find(u => u.Id == senderId).FirstOrDefaultAsync();
            var receiver = await _usersCollection.Find(u => u.Id == receiverId).FirstOrDefaultAsync();

            if (sender == null || receiver == null) throw new Exception("Jedan od korisnika ne postoji!");

            // Provera da li su već prijatelji
            if (sender.Friends.Any(f => f.UserId == receiverId))
                throw new Exception("Zahtev je već poslat ili ste već prijatelji!");

            // 1. Dodajemo receivera u listu kod sendera (kao Pending)
            var friendForSender = new Friend { UserId = receiver.Id, Username = receiver.Username, Status = "Pending" };
            var updateSender = Builders<UserProfile>.Update.Push(u => u.Friends, friendForSender);
            await _usersCollection.UpdateOneAsync(u => u.Id == senderId, updateSender);

            // 2. Dodajemo sendera u listu kod receivera (kao Pending)
            var friendForReceiver = new Friend { UserId = sender.Id, Username = sender.Username, Status = "Pending" };
            var updateReceiver = Builders<UserProfile>.Update.Push(u => u.Friends, friendForReceiver);
            await _usersCollection.UpdateOneAsync(u => u.Id == receiverId, updateReceiver);

            return true;
        }

        public async Task<bool> AcceptFriendRequest(string receiverId, string senderId)
        {
            // OVO JE PROFESORSKI KOD: Korišćenje "Positional Operator-a" ($)
            // Želimo da promenimo Status u "Accepted", ali samo za TAČNO ODREĐENOG prijatelja u nizu!

            // 1. Ažuriramo primaoca
            var receiverFilter = Builders<UserProfile>.Filter.And(
                Builders<UserProfile>.Filter.Eq(u => u.Id, receiverId),
                Builders<UserProfile>.Filter.ElemMatch(u => u.Friends, f => f.UserId == senderId) // Nalazimo onog u nizu
            );
            // $ znak u MongoDB-u znači "onaj element u nizu koji je filter pronašao"
            var receiverUpdate = Builders<UserProfile>.Update.Set("Friends.$.Status", "Accepted");
            await _usersCollection.UpdateOneAsync(receiverFilter, receiverUpdate);

            // 2. Ažuriramo pošiljaoca
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

        // PRIVATNA METODA: Radi isti posao za oba slučaja (Briše vezu kod oba igrača)
        private async Task<bool> RemoveFriendship(string userId1, string userId2)
        {
            // 1. Brišemo userId2 iz liste kod userId1
            // PullFilter traži element u nizu Friends gde je UserId jednak onome koga brišemo
            var update1 = Builders<UserProfile>.Update.PullFilter(u => u.Friends, f => f.UserId == userId2);
            await _usersCollection.UpdateOneAsync(u => u.Id == userId1, update1);

            // 2. Brišemo userId1 iz liste kod userId2
            var update2 = Builders<UserProfile>.Update.PullFilter(u => u.Friends, f => f.UserId == userId1);
            await _usersCollection.UpdateOneAsync(u => u.Id == userId2, update2);

            return true;
        }
    }
}
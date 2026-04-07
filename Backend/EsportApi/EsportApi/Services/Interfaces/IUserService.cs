namespace EsportApi.Services.Interfaces
{
    public interface IUserService
    {
        Task<bool> SendFriendRequest(string senderId, string receiverId);
        Task<bool> SendFriendRequestByUsername(string senderId, string username);
        Task<bool> AcceptFriendRequest(string receiverId, string senderId);
        Task<bool> RejectFriendRequest(string myUserId, string senderId);
        Task<bool> RemoveFriend(string myUserId, string friendId);
        Task<List<string>> GetOnlineFriendIds(string userId);
    }
}

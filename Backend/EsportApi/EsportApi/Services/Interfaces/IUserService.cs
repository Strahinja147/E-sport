namespace EsportApi.Services.Interfaces
{
    public interface IUserService
    {
        Task<bool> SendFriendRequest(string senderId, string receiverId);
        Task<bool> AcceptFriendRequest(string receiverId, string senderId);
        Task<bool> RejectFriendRequest(string myUserId, string senderId);
        Task<bool> RemoveFriend(string myUserId, string friendId);
    }
}

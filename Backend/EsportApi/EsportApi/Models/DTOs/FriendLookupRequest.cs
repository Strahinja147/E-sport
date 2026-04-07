namespace EsportApi.Models.DTOs
{
    public class FriendLookupRequest
    {
        public required string SenderId { get; set; }
        public required string Username { get; set; }
    }
}

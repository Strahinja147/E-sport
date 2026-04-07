namespace EsportApi.Services.Interfaces
{
    public interface ICassandraAuthService
    {
        string NormalizeEmail(string email);
        Task<bool> EmailExistsAsync(string email);
        Task RegisterAsync(string email, string userId, string username, string password);
        Task<CassandraAuthUser?> ValidateCredentialsAsync(string email, string password);
        Task DeleteAsync(string email);
    }

    public sealed class CassandraAuthUser
    {
        public required string Email { get; init; }
        public required string UserId { get; init; }
        public required string Username { get; init; }
    }
}

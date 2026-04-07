using Cassandra;
using EsportApi.Services.Interfaces;

namespace EsportApi.Services
{
    public sealed class CassandraAuthService : ICassandraAuthService
    {
        private readonly Cassandra.ISession _cassandra;
        private readonly IPasswordHasherService _passwordHasher;

        public CassandraAuthService(Cassandra.ISession cassandra, IPasswordHasherService passwordHasher)
        {
            _cassandra = cassandra;
            _passwordHasher = passwordHasher;
        }

        public string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        public async Task<bool> EmailExistsAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var prepared = await _cassandra.PrepareAsync(
                "SELECT email FROM esports.users_by_email WHERE email = ?");
            var rows = await _cassandra.ExecuteAsync(prepared.Bind(normalizedEmail));
            return rows.Any();
        }

        public async Task RegisterAsync(string email, string userId, string username, string password)
        {
            var normalizedEmail = NormalizeEmail(email);
            var passwordHash = _passwordHasher.HashPassword(password);
            var prepared = await _cassandra.PrepareAsync(@"
                INSERT INTO esports.users_by_email (email, user_id, username, password_hash, created_at)
                VALUES (?, ?, ?, ?, toTimestamp(now()))");

            await _cassandra.ExecuteAsync(prepared.Bind(normalizedEmail, userId, username, passwordHash));
        }

        public async Task<CassandraAuthUser?> ValidateCredentialsAsync(string email, string password)
        {
            var normalizedEmail = NormalizeEmail(email);
            var prepared = await _cassandra.PrepareAsync(
                "SELECT email, user_id, username, password_hash FROM esports.users_by_email WHERE email = ?");
            var row = (await _cassandra.ExecuteAsync(prepared.Bind(normalizedEmail))).FirstOrDefault();

            if (row == null)
            {
                return null;
            }

            var passwordHash = row.GetValue<string>("password_hash");
            if (!_passwordHasher.VerifyPassword(password, passwordHash))
            {
                return null;
            }

            return new CassandraAuthUser
            {
                Email = row.GetValue<string>("email"),
                UserId = row.GetValue<string>("user_id"),
                Username = row.GetValue<string>("username")
            };
        }

        public async Task DeleteAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);
            var prepared = await _cassandra.PrepareAsync(
                "DELETE FROM esports.users_by_email WHERE email = ?");
            await _cassandra.ExecuteAsync(prepared.Bind(normalizedEmail));
        }
    }
}

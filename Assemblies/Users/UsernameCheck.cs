using System;
using System.Threading.Tasks;
using Npgsql;

namespace Users
{
    public class UsernameCheckResult
    {
        public bool success { get; set; }
    }

    public static partial class UserQueries
    {
        public static async Task<UsernameCheckResult> DoesUsernameExistAsync(string connectionString, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return new UsernameCheckResult { success = false };
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "select exists(select 1 from users where lower(user_name) = lower(@username))",
                conn
            );
            cmd.Parameters.AddWithValue("username", username);

            var scalar = await cmd.ExecuteScalarAsync();
            var exists = scalar is bool b && b;
            return new UsernameCheckResult { success = exists };
        }
    }
}

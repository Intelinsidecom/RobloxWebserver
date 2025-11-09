using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Users
{
    public static partial class UserQueries
    {
        public static async Task<bool> UserExistsAsync(string connectionString, long userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (userId <= 0) return false;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("select 1 from users where user_id = @id", conn);
            cmd.Parameters.AddWithValue("id", userId);
            var obj = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return obj != null;
        }
    }
}

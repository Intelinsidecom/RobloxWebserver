using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Thumbnails
{
    public static class ThumbnailQueries
    {
        public static async Task<string?> GetUserHeadshotUrlAsync(string connectionString, long userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("select headshot_url from users where user_id = @id", conn);
            cmd.Parameters.AddWithValue("id", userId);
            var obj = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return obj is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
        }

        public static async Task<string?> GetUserThumbnailUrlAsync(string connectionString, long userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("select thumbnail_url from users where user_id = @id", conn);
            cmd.Parameters.AddWithValue("id", userId);
            var obj = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return obj is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
        }

        public static async Task<Dictionary<long, string?>> GetUserThumbnailUrlsAsync(string connectionString, long[] userIds, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            var result = new Dictionary<long, string?>();
            if (userIds == null || userIds.Length == 0)
                return result;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("select user_id, thumbnail_url from users where user_id = ANY(@ids)", conn);
            cmd.Parameters.AddWithValue("ids", userIds);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var url = reader.IsDBNull(1) ? null : reader.GetString(1);
                result[id] = url;
            }
            return result;
        }

        public static async Task<Dictionary<long, string?>> GetUserHeadshotUrlsAsync(string connectionString, long[] userIds, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            var result = new Dictionary<long, string?>();
            if (userIds == null || userIds.Length == 0)
                return result;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("select user_id, headshot_url from users where user_id = ANY(@ids)", conn);
            cmd.Parameters.AddWithValue("ids", userIds);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var url = reader.IsDBNull(1) ? null : reader.GetString(1);
                result[id] = url;
            }
            return result;
        }

        public static async Task SetUserHeadshotUrlAsync(string connectionString, long userId, string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("update users set headshot_url = @u where user_id = @id", conn);
            cmd.Parameters.AddWithValue("u", url ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task SetUserThumbnailUrlAsync(string connectionString, long userId, string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = new NpgsqlCommand("update users set thumbnail_url = @u where user_id = @id", conn);
            cmd.Parameters.AddWithValue("u", url ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

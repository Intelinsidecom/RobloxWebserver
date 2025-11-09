using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Users
{
    public sealed class BodyColorsRepository
    {
        public sealed class BodyColors
        {
            public int HeadColorId { get; set; }
            public int TorsoColorId { get; set; }
            public int RightArmColorId { get; set; }
            public int LeftArmColorId { get; set; }
            public int RightLegColorId { get; set; }
            public int LeftLegColorId { get; set; }
        }

        public async Task SetBodyColorsAsync(string connectionString, long userId, BodyColors colors, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId));
            if (colors == null)
                throw new ArgumentNullException(nameof(colors));

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"update bodycolors
set head_color = @head,
    torso_color = @torso,
    right_arm_color = @rarm,
    left_arm_color = @larm,
    right_leg_color = @rleg,
    left_leg_color = @lleg
where user_id = @uid";

            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("head", colors.HeadColorId);
                cmd.Parameters.AddWithValue("torso", colors.TorsoColorId);
                cmd.Parameters.AddWithValue("rarm", colors.RightArmColorId);
                cmd.Parameters.AddWithValue("larm", colors.LeftArmColorId);
                cmd.Parameters.AddWithValue("rleg", colors.RightLegColorId);
                cmd.Parameters.AddWithValue("lleg", colors.LeftLegColorId);
                cmd.Parameters.AddWithValue("uid", userId);
                var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                if (rows == 0)
                {
                    const string ins = @"insert into bodycolors(user_id, head_color, left_arm_color, left_leg_color, right_arm_color, right_leg_color, torso_color)
values(@uid, @head, @larm, @lleg, @rarm, @rleg, @torso)
on conflict (user_id) do update set
  head_color = excluded.head_color,
  left_arm_color = excluded.left_arm_color,
  left_leg_color = excluded.left_leg_color,
  right_arm_color = excluded.right_arm_color,
  right_leg_color = excluded.right_leg_color,
  torso_color = excluded.torso_color";
                    await using var up = new NpgsqlCommand(ins, conn);
                    up.Parameters.AddWithValue("uid", userId);
                    up.Parameters.AddWithValue("head", colors.HeadColorId);
                    up.Parameters.AddWithValue("larm", colors.LeftArmColorId);
                    up.Parameters.AddWithValue("lleg", colors.LeftLegColorId);
                    up.Parameters.AddWithValue("rarm", colors.RightArmColorId);
                    up.Parameters.AddWithValue("rleg", colors.RightLegColorId);
                    up.Parameters.AddWithValue("torso", colors.TorsoColorId);
                    await up.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}

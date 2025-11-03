using Microsoft.EntityFrameworkCore;

namespace RobloxWebserver.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}

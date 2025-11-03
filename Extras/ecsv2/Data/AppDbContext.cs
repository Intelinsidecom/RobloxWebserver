using Microsoft.EntityFrameworkCore;

namespace ecsv2.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}

using Microsoft.EntityFrameworkCore;

namespace fel_api.Data;

public sealed class FelApiDbContext(DbContextOptions<FelApiDbContext> options) : DbContext(options)
{
}
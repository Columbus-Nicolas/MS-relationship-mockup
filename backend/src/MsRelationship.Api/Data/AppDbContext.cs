using Microsoft.EntityFrameworkCore;

namespace MsRelationship.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}

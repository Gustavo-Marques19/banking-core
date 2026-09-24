using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Persistence;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options);

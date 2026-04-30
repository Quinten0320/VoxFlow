using AiCallAssistent.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AiCallAssistent.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();
    public DbSet<CompanyOpeningHour> CompanyOpeningHours => Set<CompanyOpeningHour>();
    public DbSet<CompanyOpeningTimeRange> CompanyOpeningTimeRanges => Set<CompanyOpeningTimeRange>();
    public DbSet<CompanyOpeningException> CompanyOpeningExceptions => Set<CompanyOpeningException>();
    public DbSet<CompanyOpeningExceptionTimeRange> CompanyOpeningExceptionTimeRanges => Set<CompanyOpeningExceptionTimeRange>();
    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<AssistantSettings> AssistantSettings => Set<AssistantSettings>();
    public DbSet<PhoneNumber> PhoneNumbers => Set<PhoneNumber>();
    public DbSet<CompanyOutlookToken> CompanyOutlookTokens => Set<CompanyOutlookToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.Property(a => a.Description).HasDefaultValue(string.Empty);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(c => c.IsActive).HasDefaultValue(false);
        });
    }
}

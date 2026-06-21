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
    public DbSet<CompanyDepartment> CompanyDepartments => Set<CompanyDepartment>();
    public DbSet<CallBlacklist> CallBlacklist => Set<CallBlacklist>();
    public DbSet<CallbackRequest> CallbackRequests => Set<CallbackRequest>();
    public DbSet<CompanyPackage> CompanyPackages => Set<CompanyPackage>();
    public DbSet<AssistantProfile> AssistantProfiles => Set<AssistantProfile>();
    public DbSet<BotActiveHour> BotActiveHours => Set<BotActiveHour>();
    public DbSet<KnowledgeSuggestion> KnowledgeSuggestions => Set<KnowledgeSuggestion>();
    public DbSet<CompanyRequest> CompanyRequests => Set<CompanyRequest>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<EmailJob> EmailJobs => Set<EmailJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.Property(a => a.Description).HasDefaultValue(string.Empty);
            // Prevents double-booking the same slot; also the last line of defence against the
            // race condition between the availability check and the insert.
            entity.HasIndex(a => new { a.EmployeeId, a.StartTime }).IsUnique();
            entity.HasIndex(a => new { a.CompanyId, a.StartTime });
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(c => c.IsActive).HasDefaultValue(false);
        });

        modelBuilder.Entity<CallSession>(entity =>
        {
            entity.HasIndex(s => new { s.CompanyId, s.CreatedAt });
        });

        modelBuilder.Entity<CallBlacklist>(entity =>
        {
            // Hot path on every inbound call; unique enforces one entry per number per company.
            entity.HasIndex(b => new { b.CompanyId, b.PhoneNumber }).IsUnique();
        });

        modelBuilder.Entity<CallbackRequest>(entity =>
        {
            entity.HasIndex(r => new { r.CompanyId, r.Status });
        });

        // Partial unique index: at most one active profile per company.
        modelBuilder.Entity<AssistantProfile>(entity =>
        {
            entity.HasIndex(p => p.CompanyId).HasFilter("is_active = TRUE").IsUnique()
                  .HasDatabaseName("uix_assistant_profiles_active");
        });

        modelBuilder.Entity<BotActiveHour>(entity =>
        {
            entity.HasIndex(h => new { h.CompanyId, h.DayOfWeek });
        });
    }
}

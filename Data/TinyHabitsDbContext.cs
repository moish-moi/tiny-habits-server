using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TinyHabits.Api.Models;

namespace TinyHabits.Api.Data;

public class TinyHabitsDbContext : DbContext
{
    public TinyHabitsDbContext(DbContextOptions<TinyHabitsDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<Checkin> Checkins => Set<Checkin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ========= Users =========
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);

            // Email ייחודי
            b.HasIndex(u => u.Email).IsUnique();

            // קצת הגדרות נעימות למסד
            b.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(320); // RFC-ish

            b.Property(u => u.PasswordHash)
                .IsRequired()
                .HasMaxLength(500);

            b.Property(u => u.CreatedAtUtc)
                .IsRequired();
        });

        // ========= Habits =========
        modelBuilder.Entity<Habit>(b =>
        {
            b.HasKey(h => h.Id);

            // FK ל־User (למרות שאין ניווטים במודל)
            b.HasOne<User>()
             .WithMany()
             .HasForeignKey(h => h.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            b.Property(h => h.Title)
                .IsRequired()
                .HasMaxLength(200);

            b.Property(h => h.Color)
                .HasMaxLength(20);

            b.Property(h => h.IsArchived)
                .IsRequired();

            b.Property(h => h.CreatedAtUtc)
                .IsRequired();

            // אינדקס שימושי לשליפות לפי משתמש + פעיל/כותרת
            b.HasIndex(h => new { h.UserId, h.IsArchived });
        });

        // ========= Checkins =========
        modelBuilder.Entity<Checkin>(b =>
        {
            b.HasKey(c => c.Id);

            // FK ל־Habit
            b.HasOne<Habit>()
             .WithMany()
             .HasForeignKey(c => c.HabitId)
             .OnDelete(DeleteBehavior.Cascade);

            // ממיר ל־DateOnly עבור SQLite: נשמור כ-TEXT yyyy-MM-dd
            var dateOnlyConverter = new ValueConverter<DateOnly, string>(
                toDb => toDb.ToString("yyyy-MM-dd"),
                fromDb => DateOnly.Parse(fromDb));

            b.Property(c => c.Date)
             .HasConversion(dateOnlyConverter)
             .HasColumnType("TEXT") // ל-SQLite; אם בעתיד תעבור ל-SQL Server אפשר לשנות ל-`date`
             .IsRequired();

            // מניעת כפילות צ'ק-אין לאותו יום לאותו הרגל
            b.HasIndex(c => new { c.HabitId, c.Date }).IsUnique();
        });
    }
}

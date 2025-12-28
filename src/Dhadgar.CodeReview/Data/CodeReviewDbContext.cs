using Dhadgar.CodeReview.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.CodeReview.Data;

/// <summary>
/// Entity Framework Core DbContext for the CodeReview service.
/// Uses SQLite for simple desktop deployment.
/// </summary>
public class CodeReviewDbContext : DbContext
{
    public CodeReviewDbContext(DbContextOptions<CodeReviewDbContext> options)
        : base(options)
    {
    }

    public DbSet<Entities.CodeReview> CodeReviews => Set<Entities.CodeReview>();
    public DbSet<ReviewComment> ReviewComments => Set<ReviewComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure CodeReview entity
        modelBuilder.Entity<Entities.CodeReview>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Repository).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ModelUsed).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();

            // Index for common queries
            entity.HasIndex(e => new { e.Repository, e.PullRequestNumber });
            entity.HasIndex(e => e.CreatedAt);

            // Configure relationship with ReviewComments
            entity.HasMany(e => e.Comments)
                .WithOne(c => c.CodeReview)
                .HasForeignKey(c => c.CodeReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure ReviewComment entity
        modelBuilder.Entity<ReviewComment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Body).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            // Index for queries
            entity.HasIndex(e => e.CodeReviewId);
        });
    }
}

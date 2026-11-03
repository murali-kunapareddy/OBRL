using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Obrl.Web.Domain;

namespace Obrl.Web.Data;

public class ObrlDbContext(DbContextOptions<ObrlDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCategory> BookCategories => Set<BookCategory>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<Disclaimer> Disclaimers => Set<Disclaimer>();
    public DbSet<TermsAcceptance> TermsAcceptances => Set<TermsAcceptance>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentReport> CommentReports => Set<CommentReport>();
    public DbSet<BoardPost> BoardPosts => Set<BoardPost>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.Theme).HasMaxLength(10);
            e.Property(x => x.CurrentSessionId).HasMaxLength(64);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Path).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => x.Path);
            e.HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Book>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Author).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.OriginalFileName).HasMaxLength(300);
            e.Property(x => x.OriginalFormat).HasMaxLength(10);
            e.Property(x => x.StatusMessage).HasMaxLength(1000);
            e.Property(x => x.TocJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.IsApproved);
            e.HasIndex(x => x.RatingAverage);
            e.Property(x => x.ApprovedById).HasMaxLength(450);
            // Books that already exist when this column arrives were live before approval existed.
            e.Property(x => x.IsApproved).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            // Full-text search over title, author and description (PostgreSQL only).
            if (Database.IsNpgsql())
            {
                e.HasGeneratedTsVectorColumn(x => x.SearchVector!, "simple", x => new { x.Title, x.Author, x.Description })
                    .HasIndex(x => x.SearchVector).HasMethod("GIN");
            }
            else
            {
                e.Ignore(x => x.SearchVector);
            }
            e.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<BookCategory>(e =>
        {
            e.HasKey(x => new { x.BookId, x.CategoryId });
            e.HasOne(x => x.Book).WithMany(x => x.Categories).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany(x => x.Books).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Subscription>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.BookId }).IsUnique().HasFilter("\"BookId\" IS NOT NULL");
            e.HasIndex(x => new { x.UserId, x.CategoryId }).IsUnique().HasFilter("\"CategoryId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Subscription_OneTarget",
                "(\"BookId\" IS NULL) <> (\"CategoryId\" IS NULL)"));
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReadingProgress>(e =>
        {
            e.HasKey(x => new { x.UserId, x.BookId });
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Bookmark>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.BookId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Disclaimer>(e =>
        {
            e.HasIndex(x => x.Version).IsUnique();
            e.Property(x => x.Text).HasMaxLength(20000).IsRequired();
        });

        b.Entity<TermsAcceptance>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.DisclaimerVersion });
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Rating>(e =>
        {
            e.HasKey(x => new { x.UserId, x.BookId });
            e.HasIndex(x => x.BookId);
            e.ToTable(t => t.HasCheckConstraint("CK_Rating_Stars", "\"Stars\" BETWEEN 1 AND 5"));
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Comment>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.HiddenReason).HasMaxLength(300);
            e.Property(x => x.HiddenById).HasMaxLength(450);
            e.HasIndex(x => new { x.BookId, x.CreatedAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent).WithMany(x => x.Replies).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CommentReport>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.CommentId, x.UserId }).IsUnique();
            e.HasIndex(x => x.ResolvedAt);
            e.HasOne(x => x.Comment).WithMany(x => x.Reports).HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SiteSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(1000);
        });

        b.Entity<BoardPost>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(8000).IsRequired();
            e.HasIndex(x => new { x.ParentId, x.IsPinned, x.LastActivityAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent).WithMany(x => x.Replies).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ActivityEvent>(e =>
        {
            // No foreign keys on purpose: history must survive user and book deletion.
            e.Property(x => x.Type).HasMaxLength(40).IsRequired();
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.UserId, x.At });
            e.HasIndex(x => new { x.Type, x.At });
        });
    }
}

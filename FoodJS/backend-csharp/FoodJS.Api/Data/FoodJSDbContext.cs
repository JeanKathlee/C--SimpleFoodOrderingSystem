using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FoodJS.Api.Data;

public class FoodJsDbContext : DbContext
{
    public FoodJsDbContext(DbContextOptions<FoodJsDbContext> options) : base(options)
    {
        // Delay EF's relationship delete processing until our save safeguard has
        // converted all Deleted entries into flag updates.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.Never;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<OrderStatus> OrderStatuses => Set<OrderStatus>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CheckoutSubmission> CheckoutSubmissions => Set<CheckoutSubmission>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Role>().HasQueryFilter(r => !r.IsDeleted);
        modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted && !u.Role.IsDeleted);
        modelBuilder.Entity<Address>().HasQueryFilter(a => !a.IsDeleted);
        modelBuilder.Entity<Category>().HasQueryFilter(c => !c.IsDeleted);
        modelBuilder.Entity<MenuItem>().HasQueryFilter(i => !i.IsDeleted && !i.Category.IsDeleted);
        modelBuilder.Entity<ShoppingCart>().HasQueryFilter(c => !c.IsDeleted && !c.User.IsDeleted);
        modelBuilder.Entity<CartItem>().HasQueryFilter(i => !i.IsDeleted
            && !i.ShoppingCart.IsDeleted && !i.ShoppingCart.User.IsDeleted
            && !i.MenuItem.IsDeleted && !i.MenuItem.Category.IsDeleted);
        // Do not hide an administrator's historical orders when their owner,
        // saved address, menu item, or coupon has subsequently been deleted.
        modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);
        modelBuilder.Entity<OrderStatus>().HasQueryFilter(s => !s.IsDeleted);
        modelBuilder.Entity<OrderItem>().HasQueryFilter(i => !i.IsDeleted && !i.Order.IsDeleted);
        modelBuilder.Entity<Payment>().HasQueryFilter(p => !p.IsDeleted && !p.Order.IsDeleted);
        modelBuilder.Entity<OrderStatusHistory>().HasQueryFilter(h => !h.IsDeleted && !h.Order.IsDeleted);
        modelBuilder.Entity<Coupon>().HasQueryFilter(c => !c.IsDeleted);
        modelBuilder.Entity<Notification>().HasQueryFilter(n => !n.IsDeleted && !n.User.IsDeleted);
        modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(t => !t.IsDeleted && !t.User.IsDeleted);
        modelBuilder.Entity<EmailVerificationToken>().HasQueryFilter(t => !t.IsDeleted && !t.User.IsDeleted && !t.User.Role.IsDeleted);
        modelBuilder.Entity<EmailVerificationToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<CheckoutSubmission>().HasQueryFilter(s => !s.IsDeleted);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entity.ClrType))
                modelBuilder.Entity(entity.ClrType).Property(nameof(ISoftDeletable.IsDeleted)).HasDefaultValue(false);

            // Physical parent deletion must never cascade through retained records.
            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ConvertDeletesToTombstones();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ConvertDeletesToTombstones();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConvertDeletesToTombstones()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Deleted).ToList())
        {
            if (entry.Entity is not ISoftDeletable)
                throw new InvalidOperationException("Application records must support soft deletion.");
            // Even an accidental EF Remove call becomes a single flag UPDATE.
            // Retain the row and every relationship; do not persist other pending edits.
            entry.State = EntityState.Unchanged;
            entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
            entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified = true;
        }
    }
}

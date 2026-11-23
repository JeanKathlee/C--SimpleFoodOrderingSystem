using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Data;

public class FoodJsDbContext(DbContextOptions<FoodJsDbContext> options)
    : DbContext(options)
{
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MenuItem>().Property(item => item.IsDeleted).HasDefaultValue(false);
        modelBuilder.Entity<Order>().Property(order => order.IsDeleted).HasDefaultValue(false);
        modelBuilder.Entity<MenuItem>().HasQueryFilter(item => !item.IsDeleted);
        modelBuilder.Entity<Order>().HasQueryFilter(order => !order.IsDeleted);
        modelBuilder.Entity<CartItem>().HasQueryFilter(item => !item.MenuItem.IsDeleted);
        // These filters follow order deletion, not menu deletion: snapshots remain
        // readable for an active order even after its live menu item is deleted.
        modelBuilder.Entity<OrderItem>().HasQueryFilter(item => !item.Order.IsDeleted);
        modelBuilder.Entity<Payment>().HasQueryFilter(payment => !payment.Order.IsDeleted);
        modelBuilder.Entity<OrderStatusHistory>().HasQueryFilter(entry => !entry.Order.IsDeleted);

        // Retained records must not be physically erased by another feature's
        // parent delete, including category/user deletes and their cascades.
        modelBuilder.Entity<MenuItem>().HasOne(item => item.Category).WithMany().HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>().HasOne(order => order.User).WithMany().HasForeignKey(order => order.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>().HasOne(order => order.OrderStatus).WithMany().HasForeignKey(order => order.OrderStatusId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItem>().HasOne(item => item.MenuItem).WithMany().HasForeignKey(item => item.MenuItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItem>().HasOne(item => item.Order).WithMany().HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Payment>().HasOne(payment => payment.Order).WithMany().HasForeignKey(payment => payment.OrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderStatusHistory>().HasOne(entry => entry.Order).WithMany().HasForeignKey(entry => entry.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

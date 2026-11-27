using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class SoftDeletePersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private FoodJsDbContext db = null!;
    private List<object> records = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new FoodJsDbContext(new DbContextOptionsBuilder<FoodJsDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        var role = new Role { Name = "customer" };
        var user = new User { FirstName = "Retained", LastName = "Customer", Email = "retained@qa.invalid", PasswordHash = "unused", Role = role };
        var category = new Category { Name = "Retained category" };
        var menu = new MenuItem { Name = "Retained meal", Price = 100, PrepTime = 5, Category = category };
        var address = new Address { User = user, AddressLine1 = "Retained address", City = "Cebu", IsDefault = true };
        var cart = new ShoppingCart { User = user };
        var line = new CartItem { ShoppingCart = cart, MenuItem = menu, Quantity = 2, UnitPrice = 100 };
        var status = new OrderStatus { Name = "Pending" };
        var order = new Order { User = user, Address = address, OrderStatus = status, CustomerName = "Retained Customer", TotalAmount = 100 };
        var item = new OrderItem { Order = order, MenuItem = menu, ItemName = "Retained meal", Quantity = 1, SnapshotPrice = 100, LineTotal = 100 };
        var payment = new Payment { Order = order, PaymentMethod = "cash", Amount = 100 };
        var history = new OrderStatusHistory { Order = order, FromStatus = "", ToStatus = "Pending", ChangedByUser = user };
        var notification = new Notification { User = user, Order = order, Title = "Retained notification", Message = "Retained message" };
        var coupon = new Coupon { Code = "RETAIN", Discount = 10, DiscountType = "percentage", ExpiryDate = new DateOnly(2026, 12, 31) };
        var reset = new PasswordResetToken { User = user, TokenHash = "retained-hash", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        records = [role, user, category, menu, address, cart, line, status, order, item, payment, history, notification, coupon, reset];
        db.AddRange(records);
        await db.SaveChangesAsync();
        var submission = new CheckoutSubmission { UserId = user.Id, Key = Guid.NewGuid().ToString(), OrderId = order.Id, RequestHash = "retained-request" };
        db.CheckoutSubmissions.Add(submission);
        await db.SaveChangesAsync();
        records.Add(submission);
    }

    [Fact]
    public void Every_application_entity_has_a_false_default_flag_and_restrictive_relationships()
    {
        Assert.Equal(records.Count, db.Model.GetEntityTypes().Count());
        foreach (var entity in db.Model.GetEntityTypes())
        {
            Assert.True(typeof(ISoftDeletable).IsAssignableFrom(entity.ClrType));
            Assert.Equal(false, entity.FindProperty(nameof(ISoftDeletable.IsDeleted))!.GetDefaultValue());
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accidental_EF_deletes_only_update_flags_and_never_remove_any_record(bool asynchronous)
    {
        // Include parents and their related records: no cascade may erase children.
        foreach (var record in records)
        {
            db.Remove(record);
            if (asynchronous) await db.SaveChangesAsync(); else db.SaveChanges();
            Assert.True(((ISoftDeletable)record).IsDeleted);
            var count = await StoredCountAsync(record);
            var deleted = await StoredCountAsync(record, true);
            Assert.Equal(1L, count);
            Assert.Equal(1L, deleted);
        }
    }

    [Fact]
    public async Task Migration_preserves_legacy_records_and_defaults_all_new_flags_to_false()
    {
        db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261125120613_ProtectRetainedRelationships");
        var oldColumns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Users')").ToListAsync();
        Assert.DoesNotContain("IsDeleted", oldColumns);
        await migrator.MigrateAsync();
        foreach (var record in records)
        {
            Assert.Equal(1L, await StoredCountAsync(record, false));
        }
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Soft_deleted_rows_can_be_explicitly_restored_without_recreating_them()
    {
        var coupon = Assert.Single(await db.Coupons.ToListAsync());
        coupon.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.Empty(await db.Coupons.ToListAsync());
        var retained = await db.Coupons.IgnoreQueryFilters().SingleAsync();
        retained.IsDeleted = false;
        await db.SaveChangesAsync();
        Assert.Equal(coupon.Id, (await db.Coupons.SingleAsync()).Id);
        Assert.Equal(1, await db.Coupons.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void Repositories_expose_no_permanent_delete_operations_or_SQL()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "FoodJS.Api", "Repositories"))) folder = folder.Parent;
        Assert.NotNull(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(folder!.FullName, "FoodJS.Api", "Repositories"), "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(".Remove(", source);
            Assert.DoesNotContain(".RemoveRange(", source);
            Assert.DoesNotContain("ExecuteDelete", source);
            Assert.DoesNotMatch(@"(?i)\bDELETE\s+FROM\b", source);
        }
    }

    private async Task<long> StoredCountAsync(object record, bool? deleted = null)
    {
        var table = db.Model.FindEntityType(record.GetType())!.GetTableName()!;
        // Table identifiers come only from EF metadata and are validated before
        // quoting. Values are parameters; no request input enters this query.
        Assert.Matches(@"^[A-Za-z][A-Za-z0-9_]*$", table);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"" + table + "\"";
        if (deleted is bool flag)
        {
            command.CommandText += " WHERE IsDeleted = $flag";
            command.Parameters.AddWithValue("$flag", flag ? 1 : 0);
        }
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }
}

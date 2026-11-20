using Xunit;
using FoodJS.Api.Common;
using FoodJS.Api.Features.Checkout;
using FoodJS.Api.Features.Orders;
using FoodJS.Api.Models;

namespace FoodJS.Api.Tests;

public class OrderStatusRulesTests
{
    [Theory]
    [InlineData("Pending", "Preparing", true)]
    [InlineData("Pending", "Cancelled", true)]
    [InlineData("Pending", "Ready", false)]
    [InlineData("Preparing", "Ready", true)]
    [InlineData("Preparing", "Delivered", false)]
    [InlineData("Ready", "Delivered", true)]
    [InlineData("Ready", "Cancelled", false)]
    [InlineData("Delivered", "Cancelled", false)]
    [InlineData("Cancelled", "Pending", false)]
    public void Transitions_follow_the_kitchen_flow(string from, string to, bool allowed) =>
        Assert.Equal(allowed, OrderStatusRules.CanTransition(from, to));

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Preparing", true)]
    [InlineData("Ready", false)]
    [InlineData("Delivered", false)]
    [InlineData("Cancelled", false)]
    public void Customer_can_cancel_only_before_the_order_is_ready(string status, bool allowed) =>
        Assert.Equal(allowed, OrderStatusRules.CanCustomerCancel(status));

    [Fact]
    public void Unknown_status_is_rejected() => Assert.False(OrderStatusRules.IsKnown("Shipped"));
}

public class CouponRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    private static Coupon Make(string type, decimal discount, decimal min = 0, int? max = null, int used = 0, int expiresInDays = 30) => new()
    {
        Code = "PALDO10",
        DiscountType = type,
        Discount = discount,
        MinimumOrderAmount = min,
        MaximumUses = max,
        UsedCount = used,
        ExpiryDate = Today.AddDays(expiresInDays)
    };

    [Fact]
    public void Percentage_coupon_discounts_the_order()
    {
        var result = CouponRules.Evaluate("PALDO10", Make("percentage", 10, min: 100), 447m, Today);
        Assert.True(result.Valid);
        Assert.Equal(44.70m, result.DiscountAmount);
        Assert.Equal(402.30m, result.FinalAmount);
    }

    [Fact]
    public void Fixed_coupon_is_capped_at_the_order_amount()
    {
        var result = CouponRules.Evaluate("PALDO10", Make("fixed", 500), 100m, Today);
        Assert.Equal(100m, result.DiscountAmount);
        Assert.Equal(0m, result.FinalAmount);
    }

    [Theory]
    [InlineData("paldo10")]
    [InlineData("AB")]
    [InlineData("PAL DO10")]
    [InlineData("")]
    public void Code_must_be_4_to_12_uppercase_letters_or_digits(string code) =>
        Assert.Equal(CouponRules.FormatError, CouponRules.Evaluate(code, Make("fixed", 5), 100m, Today).Error);

    [Fact]
    public void Missing_or_inactive_coupon_is_not_found()
    {
        Assert.Equal("Coupon not found", CouponRules.Evaluate("PALDO10", null, 100m, Today).Error);
        var inactive = Make("fixed", 5);
        inactive.IsActive = false;
        Assert.Equal("Coupon not found", CouponRules.Evaluate("PALDO10", inactive, 100m, Today).Error);
    }

    [Fact]
    public void Expired_coupon_is_rejected_but_expiry_day_is_still_valid()
    {
        Assert.Equal("Coupon expired", CouponRules.Evaluate("PALDO10", Make("fixed", 5, expiresInDays: -1), 100m, Today).Error);
        Assert.True(CouponRules.Evaluate("PALDO10", Make("fixed", 5, expiresInDays: 0), 100m, Today).Valid);
    }

    [Fact]
    public void Usage_limit_and_minimum_order_are_enforced()
    {
        Assert.Equal("Coupon usage limit reached", CouponRules.Evaluate("PALDO10", Make("fixed", 5, max: 1, used: 1), 100m, Today).Error);
        Assert.Equal("Minimum order amount 200 required", CouponRules.Evaluate("PALDO10", Make("fixed", 5, min: 200), 150m, Today).Error);
    }
}

public class PublicIdTests
{
    [Theory]
    [InlineData("M-12", 12)]
    [InlineData("m-12", 12)]
    [InlineData("12", 12)]
    public void Menu_ids_parse_leniently(string text, int expected)
    {
        Assert.True(PublicId.TryParseMenu(text, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("M-0")]
    [InlineData("M-abc")]
    [InlineData("-5")]
    public void Bad_ids_are_rejected(string text) => Assert.False(PublicId.TryParseMenu(text, out _));

    [Fact]
    public void Formatting_round_trips()
    {
        Assert.Equal("O-7", PublicId.Order(7));
        Assert.True(PublicId.TryParseOrder(PublicId.Order(7), out var id) && id == 7);
        Assert.Equal("cat-3", PublicId.Category(3));
    }
}

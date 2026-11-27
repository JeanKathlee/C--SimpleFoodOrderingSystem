using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using Xunit;
using FoodJS.Api.Common;
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

    [Fact]
    public void Minus_one_max_uses_means_unlimited()
    {
        Assert.True(CouponRules.Evaluate("PALDO10", Make("fixed", 5, max: -1, used: 50), 100m, Today).Valid);
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

public class ImageRulesTests
{
    private static string DataUrl(string type, params byte[] bytes) => $"data:image/{type};base64,{Convert.ToBase64String(bytes)}";

    [Theory]
    [InlineData(2 * 1024 * 1024 + 1, true)]
    [InlineData(5 * 1024 * 1024, true)]
    [InlineData(5 * 1024 * 1024 + 1, false)]
    public void Food_images_allow_five_mebibytes_but_profile_limit_is_unchanged(int size, bool allowed)
    {
        Assert.Equal(5 * 1024 * 1024, ImageRules.MenuMaxBytes);
        var bytes = new byte[size];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        var image = DataUrl("png", bytes);
        Assert.Equal(allowed ? null : "Image must be 5 MB or smaller.", ImageRules.CheckMenuImage(image));
        Assert.Equal(ImageRules.SizeError, ImageRules.Check(image));
    }

    [Fact]
    public void Real_images_links_and_empty_are_allowed()
    {
        Assert.Null(ImageRules.Check(null));
        Assert.Null(ImageRules.Check(""));
        Assert.Null(ImageRules.Check("https://images.unsplash.com/photo.jpg"));
        Assert.Null(ImageRules.Check(DataUrl("png", 0x89, 0x50, 0x4E, 0x47, 1, 2)));
        Assert.Null(ImageRules.Check(DataUrl("jpeg", 0xFF, 0xD8, 0xFF, 0xE0)));
        Assert.Null(ImageRules.Check(DataUrl("webp", 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50)));
    }

    [Fact]
    public void Wrong_types_fakes_and_big_files_are_refused()
    {
        Assert.Equal(ImageRules.TypeError, ImageRules.Check("data:image/svg+xml;base64,PHN2Zz4="));
        Assert.Equal(ImageRules.TypeError, ImageRules.Check("data:application/pdf;base64,JVBERg=="));
        Assert.Equal(ImageRules.TypeError, ImageRules.Check("javascript:alert(1)"));
        Assert.Equal(ImageRules.TypeError, ImageRules.Check("file:///C:/secret.png"));
        Assert.Equal(ImageRules.TypeError, ImageRules.Check(DataUrl("png", 0x25, 0x50, 0x44, 0x46)));
        Assert.Equal(ImageRules.TypeError, ImageRules.Check("data:image/png;base64,not-base64!!"));
        var big = new byte[ImageRules.MaxBytes + 1];
        big[0] = 0x89; big[1] = 0x50; big[2] = 0x4E; big[3] = 0x47;
        Assert.Equal(ImageRules.SizeError, ImageRules.Check(DataUrl("png", big)));
    }
}

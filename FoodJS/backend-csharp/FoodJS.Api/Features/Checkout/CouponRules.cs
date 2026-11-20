using System.Globalization;
using System.Text.RegularExpressions;
using FoodJS.Api.Models;

namespace FoodJS.Api.Features.Checkout;

public sealed record CouponCheck(bool Valid, string? Error, decimal DiscountAmount, decimal FinalAmount)
{
    public static CouponCheck Fail(string error, decimal orderAmount) => new(false, error, 0m, orderAmount);
}

// The rules for validating and applying coupons. The rules are enforced on the server, not the client.
public static partial class CouponRules
{
    public const string FormatError = "Coupon codes are case-sensitive. Use uppercase letters and numbers only.";

    [GeneratedRegex("^[A-Z0-9]{4,12}$")]
    private static partial Regex CodeFormat();

    public static bool IsValidFormat(string? code) => code is not null && CodeFormat().IsMatch(code.Trim());

    public static CouponCheck Evaluate(string? code, Coupon? coupon, decimal orderAmount, DateOnly today)
    {
        if (!IsValidFormat(code))
        {
            return CouponCheck.Fail(FormatError, orderAmount);
        }

        // An inactive coupon is treated the same as one that does not exist.
        if (coupon is null || !coupon.IsActive)
        {
            return CouponCheck.Fail("Coupon not found", orderAmount);
        }

        // Valid through the whole expiry day.
        if (today > coupon.ExpiryDate)
        {
            return CouponCheck.Fail("Coupon expired", orderAmount);
        }

        if (coupon.MaximumUses is not null && coupon.UsedCount >= coupon.MaximumUses)
        {
            return CouponCheck.Fail("Coupon usage limit reached", orderAmount);
        }

        if (orderAmount < coupon.MinimumOrderAmount)
        {
            var minimum = coupon.MinimumOrderAmount.ToString("0.##", CultureInfo.InvariantCulture);
            return CouponCheck.Fail($"Minimum order amount {minimum} required", orderAmount);
        }

        var discount = CalculateDiscount(coupon, orderAmount);
        return new CouponCheck(true, null, discount, Math.Max(0m, orderAmount - discount));
    }

    public static decimal CalculateDiscount(Coupon coupon, decimal orderAmount)
    {
        var raw = coupon.DiscountType.Trim().ToLowerInvariant() switch
        {
            "percentage" => orderAmount * coupon.Discount / 100m,
            "fixed" => coupon.Discount,
            _ => 0m
        };

        // Never discount more than the order is worth.
        return Math.Round(Math.Min(raw, orderAmount), 2, MidpointRounding.AwayFromZero);
    }
}

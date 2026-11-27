using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace FoodJS.Api.Services;

// Email clients use an HTML link styled as a button, with plain-text fallback.
public static class VerificationEmailTemplate
{
    public static MailMessage CreateMessage(string sender, string recipient, Uri verificationLink)
    {
        if (!verificationLink.IsAbsoluteUri || verificationLink.Scheme is not ("https" or "http"))
            throw new ArgumentException("A web verification URL is required.", nameof(verificationLink));

        var url = verificationLink.AbsoluteUri;
        var safeUrl = WebUtility.HtmlEncode(url);
        var localNotice = verificationLink.IsLoopback
            ? "Local testing: open this email on the computer running FoodJS. This localhost link will not work on a different phone or computer."
            : "";
        var text = $"Confirm your FoodJS email by opening this link, then choosing Verify my email:\n\n{url}\n\nThis link expires in 30 minutes and can only be used once.\nIf you did not request this account or email change, do not verify it.\n\n{localNotice}";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0;padding:24px 12px;background:#f5f3f0;font-family:Arial,Helvetica,sans-serif;color:#2d241c;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background:#ffffff;border:1px solid #f0dfcc;border-radius:16px;">
                  <tr><td style="padding:28px 24px 12px;text-align:center;color:#ed7300;font-size:22px;font-weight:bold;">FOODJS</td></tr>
                  <tr><td style="padding:8px 24px 28px;text-align:center;">
                    <h1 style="margin:0 0 16px;font-size:24px;line-height:1.3;">Confirm your email</h1>
                    <p style="font-size:15px;line-height:1.6;margin:0 0 24px;">Thanks for choosing FoodJS. Confirm your email address to finish setting up your account.</p>
                    <table role="presentation" cellpadding="0" cellspacing="0" align="center"><tr><td align="center" bgcolor="#ed7300" style="border-radius:8px;">
                      <a href="{{safeUrl}}" style="display:inline-block;padding:16px 30px;color:#ffffff;background:#ed7300;border:1px solid #ed7300;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold;">Confirm email</a>
                    </td></tr></table>
                    <p style="margin:24px 0 12px;font-size:13px;line-height:1.6;color:#66584b;">This button opens a confirmation page. The link expires in 30 minutes and can only be used once.</p>
                    <p style="margin:0;font-size:12px;line-height:1.6;color:#77695d;">If you did not request this account or email change, do not confirm it.</p>
                    {{(localNotice.Length > 0 ? $"<p style=\"margin:20px 0 0;padding:12px;background:#fff4e6;font-size:12px;line-height:1.6;color:#72512d;\">{localNotice}</p>" : "")}}
                  </td></tr>
                </table>
              </td></tr></table>
            </body>
            </html>
            """;

        var message = new MailMessage
        {
            From = new MailAddress(sender, "FoodJS"),
            Subject = "Confirm your FoodJS email",
            SubjectEncoding = Encoding.UTF8,
            Body = text,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));
        return message;
    }
}

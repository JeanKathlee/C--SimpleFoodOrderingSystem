using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace FoodJS.Api.Services;

public static class PasswordResetEmailTemplate
{
    public static MailMessage CreateMessage(string sender, string recipient, Uri resetLink)
    {
        if (!resetLink.IsAbsoluteUri || resetLink.Scheme is not ("https" or "http"))
            throw new ArgumentException("A web password-reset URL is required.", nameof(resetLink));
        var url = resetLink.AbsoluteUri;
        var localNotice = resetLink.IsLoopback
            ? "Local testing: open this email on the computer running FoodJS. This localhost link will not work on a different phone or computer."
            : "";
        var text = $"Reset your FoodJS password by opening this link:\n\n{url}\n\nThis link expires in 30 minutes and can only be used once.\nIf you did not request a password reset, ignore this email. Your password has not changed.\n\n{localNotice}";
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0;padding:24px 12px;background:#f5f3f0;font-family:Arial,Helvetica,sans-serif;color:#2d241c;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background:#fff;border:1px solid #f0dfcc;border-radius:16px;">
                  <tr><td style="padding:28px 24px 12px;text-align:center;color:#ed7300;font-size:22px;font-weight:bold;">FOODJS</td></tr>
                  <tr><td style="padding:8px 24px 28px;text-align:center;">
                    <h1 style="font-size:24px;line-height:1.3;">Reset your password</h1>
                    <p style="font-size:15px;line-height:1.6;">We received a request to reset your FoodJS password. Choose a new password using the button below.</p>
                    <table role="presentation" cellpadding="0" cellspacing="0" align="center"><tr><td align="center" bgcolor="#ed7300" style="border-radius:8px;">
                      <a href="{{WebUtility.HtmlEncode(url)}}" style="display:inline-block;padding:16px 30px;color:#fff;background:#ed7300;border:1px solid #ed7300;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold;">Reset password</a>
                    </td></tr></table>
                    <p style="margin:24px 0 12px;font-size:13px;line-height:1.6;">This link expires in 30 minutes and can only be used once.</p>
                    <p style="font-size:12px;line-height:1.6;">If you did not request this reset, ignore this email. Your password has not changed.</p>
                    {{(localNotice.Length > 0 ? $"<p style=\"font-size:12px;line-height:1.6;\">{localNotice}</p>" : "")}}
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
        var message = Create(sender, recipient, "Reset your FoodJS password", text);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));
        return message;
    }

    public static MailMessage CreateChangedMessage(string sender, string recipient) => Create(sender, recipient,
        "Your FoodJS password was changed",
        "Your FoodJS password was changed successfully. All existing sign-in sessions have been revoked.\n\nIf you did not make this change, contact the FoodJS administrator immediately. Never share your password or reset links.");

    private static MailMessage Create(string sender, string recipient, string subject, string body)
    {
        var message = new MailMessage
        {
            From = new MailAddress(sender, "FoodJS"), Subject = subject, SubjectEncoding = Encoding.UTF8,
            Body = body, BodyEncoding = Encoding.UTF8, IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));
        return message;
    }
}

namespace BlueberryMart.Api.Services;

/// <summary>
/// Branded HTML for the transactional emails (verification + password reset). Kept in one place so
/// the look stays consistent with the wwwroot pages (🫐, dark green #14532d, green button #16a34a).
/// </summary>
public static class EmailTemplates
{
    public static string Verification(string link) => Shell(
        heading: "Confirm your email",
        intro: "Thanks for signing up for Blueberry Mart! Tap the button below to confirm your email " +
               "address — then head back to the app and log in.",
        buttonText: "Verify email",
        link: link,
        footer: "If you didn't create a Blueberry Mart account, you can safely ignore this email. " +
                "This link expires in 24 hours.");

    public static string PasswordReset(string link) => Shell(
        heading: "Reset your password",
        intro: "We received a request to reset your Blueberry Mart password. Tap the button below to " +
               "choose a new one. This link expires in 1 hour.",
        buttonText: "Reset password",
        link: link,
        footer: "If you didn't request a password reset, you can safely ignore this email — your " +
                "password won't change.");

    private static string Shell(string heading, string intro, string buttonText, string link, string footer) => $$"""
        <!doctype html>
        <html lang="en">
        <body style="margin:0;padding:0;background:#f3f8f3;font-family:-apple-system,system-ui,Segoe UI,sans-serif;">
          <div style="max-width:480px;margin:0 auto;padding:32px 16px;">
            <div style="background:#fff;border-radius:16px;padding:36px;box-shadow:0 6px 24px rgba(0,0,0,.08);">
              <div style="font-size:44px;text-align:center;line-height:1;">🫐</div>
              <h1 style="font-size:22px;color:#14532d;text-align:center;margin:12px 0 4px;">{{heading}}</h1>
              <p style="font-size:14px;line-height:1.7;color:#374151;margin:20px 0;">{{intro}}</p>
              <div style="text-align:center;margin:28px 0;">
                <a href="{{link}}" style="display:inline-block;background:#16a34a;color:#fff;text-decoration:none;
                   font-size:15px;font-weight:600;padding:13px 28px;border-radius:10px;">{{buttonText}}</a>
              </div>
              <p style="font-size:12px;line-height:1.6;color:#9ca3af;margin:20px 0 0;">{{footer}}</p>
              <p style="font-size:12px;line-height:1.6;color:#9ca3af;margin:12px 0 0;word-break:break-all;">
                Or paste this link into your browser:<br><a href="{{link}}" style="color:#16a34a;">{{link}}</a></p>
            </div>
          </div>
        </body>
        </html>
        """;
}

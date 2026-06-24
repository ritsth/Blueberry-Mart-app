namespace BlueberryMart.Api.Services;

/// <summary>
/// Branded HTML shown in the browser after a user taps an email-verification link (the GET handler
/// needs a dynamic success/expired outcome, so it can't be a static wwwroot file). Matches the
/// styling of the wwwroot pages and the emails.
/// </summary>
public static class AuthResultPages
{
    public static string VerifySuccess() => Page(
        icon: "✅",
        title: "Email verified",
        message: "Your email is confirmed. Head back to the Blueberry Mart app and log in.");

    public static string VerifyFailed() => Page(
        icon: "⚠️",
        title: "Link expired or invalid",
        message: "This verification link is no longer valid. Open the app, try to log in, and tap " +
                 "\"Resend verification email\" to get a fresh link.");

    private static string Page(string icon, string title, string message) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{title}} — Blueberry Mart</title>
          <style>
            body { font-family:-apple-system,system-ui,sans-serif; background:#f3f8f3; color:#14532d;
                   display:flex; min-height:100vh; align-items:center; justify-content:center; margin:0; padding:16px; box-sizing:border-box; }
            .card { background:#fff; padding:40px; border-radius:16px; box-shadow:0 6px 24px rgba(0,0,0,.08); max-width:420px; width:100%; text-align:center; }
            .icon { font-size:48px; line-height:1; margin-bottom:12px; }
            h1 { margin:0 0 10px; font-size:22px; }
            p { color:#374151; font-size:14px; line-height:1.6; margin:0; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">{{icon}}</div>
            <h1>{{title}}</h1>
            <p>{{message}}</p>
          </div>
        </body>
        </html>
        """;
}

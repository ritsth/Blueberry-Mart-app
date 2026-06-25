using System.Text;
using System.Threading.RateLimiting;
using BlueberryMart.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);

builder.Services.AddDbContext<BlueberryMartDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// eSewa payment integration
builder.Services.Configure<BlueberryMart.Api.Configuration.EsewaOptions>(
    builder.Configuration.GetSection("Esewa"));
builder.Services.AddHttpClient<BlueberryMart.Api.Services.Interfaces.IEsewaPaymentService,
    BlueberryMart.Api.Services.EsewaPaymentService>(c => c.Timeout = TimeSpan.FromSeconds(10));

// Image storage (review photos, item photos): GCS when a bucket is configured, else local.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Gcs:BucketName"]))
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IImageStorage,
        BlueberryMart.Api.Services.GcsImageStorage>();
else
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IImageStorage,
        BlueberryMart.Api.Services.LocalImageStorage>();

// Inventory event stream: real Kafka producer when a broker is configured,
// otherwise a no-op so the app runs without Kafka (production today, and tests).
builder.Services.Configure<BlueberryMart.Api.Configuration.KafkaOptions>(
    builder.Configuration.GetSection("Kafka"));
var kafkaEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["Kafka:BootstrapServers"]);
// Consumers run in-process locally (local Redpanda, no API key) and on the dedicated
// Cloud Run worker (Kafka:RunConsumers=true). The prod API service only *produces* —
// RunConsumers defaults false once a Confluent API key is configured.
var kafkaHasApiKey = !string.IsNullOrWhiteSpace(builder.Configuration["Kafka:ApiKey"]);
var runConsumers = kafkaEnabled && builder.Configuration.GetValue("Kafka:RunConsumers", !kafkaHasApiKey);

if (kafkaEnabled)
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IStockEventProducer,
        BlueberryMart.Api.Services.KafkaStockEventProducer>();
else
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IStockEventProducer,
        BlueberryMart.Api.Services.NoOpStockEventProducer>();

// Stages sales domain events into the transactional outbox (scoped: shares the request's
// DbContext so the outbox row commits atomically with the change that produced it).
builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.ISalesEventOutbox,
    BlueberryMart.Api.Services.SalesEventOutbox>();

// Shared order-cancel logic (restock + events) used by both the manager and customer cancel paths.
builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.IOrderCancellationService,
    BlueberryMart.Api.Services.OrderCancellationService>();

// Consumer turns stock-changed events into back-in-stock notifications.
if (runConsumers)
    builder.Services.AddHostedService<BlueberryMart.Api.Services.StockEventConsumer>();

// Publishes transactional-outbox rows (sales events) to Kafka — worker-only, single instance.
if (runConsumers)
    builder.Services.AddHostedService<BlueberryMart.Api.Services.OutboxDispatcher>();

// Unpaid-order expiry sweeper — worker-only (a single always-on instance), so it never
// races multiple API instances or vanishes when the API scales to zero.
if (runConsumers)
    builder.Services.AddHostedService<BlueberryMart.Api.Services.OrderExpirySweeper>();

// BigQuery analytics warehouse: opt-in via BigQuery:ProjectId. The sink (Kafka ->
// BigQuery) runs only when both Kafka and BigQuery are configured.
builder.Services.Configure<BlueberryMart.Api.Configuration.BigQueryOptions>(
    builder.Configuration.GetSection("BigQuery"));
var bigQueryConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["BigQuery:ProjectId"]);
if (bigQueryConfigured)
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IInventoryAnalytics,
        BlueberryMart.Api.Services.BigQueryInventoryAnalytics>();
else
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IInventoryAnalytics,
        BlueberryMart.Api.Services.DisabledInventoryAnalytics>();
if (bigQueryConfigured && runConsumers)
    builder.Services.AddHostedService<BlueberryMart.Api.Services.BigQueryStockSink>();
// Streams sales events into the append-only raw tables that back the sales_fact view.
if (bigQueryConfigured && runConsumers)
    builder.Services.AddHostedService<BlueberryMart.Api.Services.BigQuerySalesSink>();

// Self-service "Explore" analytics over the sales_fact warehouse: opt-in via the same
// BigQuery:ProjectId. A disabled (no-op) implementation is used when BigQuery is off.
if (bigQueryConfigured)
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IAnalyticsQueryService,
        BlueberryMart.Api.Services.BigQueryAnalyticsQueryService>();
else
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IAnalyticsQueryService,
        BlueberryMart.Api.Services.DisabledAnalyticsQueryService>();

// Customer support assistant: opt-in via Chat:ApiKey (Anthropic). A disabled (no-op)
// implementation is used when no key is configured (e.g. production today).
builder.Services.Configure<BlueberryMart.Api.Configuration.ChatOptions>(
    builder.Configuration.GetSection("Chat"));
if (!string.IsNullOrWhiteSpace(builder.Configuration["Chat:ApiKey"]))
    builder.Services.AddHttpClient<BlueberryMart.Api.Services.Interfaces.IChatService,
        BlueberryMart.Api.Services.LlmChatService>(c => c.Timeout = TimeSpan.FromSeconds(30));
else
    builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.IChatService,
        BlueberryMart.Api.Services.DisabledChatService>();

// Admin-editable global settings (delivery fee, membership fee, maintenance mode…),
// cached in memory and read on every checkout.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.ISettingsService,
    BlueberryMart.Api.Services.SettingsService>();

// Releases stock reserved by unpaid orders after a hold window. The periodic sweeper
// (OrderExpirySweeper) runs only on the worker; this service holds the logic so it's testable.
builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.IOrderExpiryService,
    BlueberryMart.Api.Services.OrderExpiryService>();

// Verifies Google ID tokens for "Continue with Google" sign-in.
builder.Services.AddScoped<BlueberryMart.Api.Security.IGoogleTokenValidator,
    BlueberryMart.Api.Security.GoogleTokenValidator>();

// Transactional email (verification + password-reset links): Resend when an API key is configured,
// otherwise a logging sender that just writes the email (incl. the link) to logs — local dev + tests.
builder.Services.Configure<BlueberryMart.Api.Configuration.EmailOptions>(
    builder.Configuration.GetSection("Email"));
if (!string.IsNullOrWhiteSpace(builder.Configuration["Email:ApiKey"]))
    builder.Services.AddHttpClient<BlueberryMart.Api.Services.Interfaces.IEmailSender,
        BlueberryMart.Api.Services.ResendEmailSender>(c => c.Timeout = TimeSpan.FromSeconds(10));
else
    builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.IEmailSender,
        BlueberryMart.Api.Services.LoggingEmailSender>();

// Issues + validates email-verification and password-reset link tokens.
builder.Services.AddScoped<BlueberryMart.Api.Services.Interfaces.IAuthCodeService,
    BlueberryMart.Api.Services.AuthCodeService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };

        // Per-request ban enforcement: even a still-valid token is rejected the
        // moment a user is banned. One indexed lookup per authenticated request.
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var sub = ctx.Principal?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(sub, out var userId)) { ctx.Fail("Invalid subject."); return; }

                var db = ctx.HttpContext.RequestServices
                    .GetRequiredService<BlueberryMartDbContext>();
                var status = await db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.IsBanned, u.DeletedAt, u.PasswordChangedAt })
                    .FirstOrDefaultAsync();

                if (status is null) { ctx.Fail("Account no longer exists."); return; }
                if (status.DeletedAt is not null) { ctx.Fail("Account has been deleted."); return; }
                if (status.IsBanned) { ctx.Fail("Account is banned."); return; }

                // A password reset stamps PasswordChangedAt; reject any token issued before it so
                // stolen/older sessions stop working immediately.
                if (status.PasswordChangedAt is { } changedAt)
                {
                    var iat = ctx.Principal?.FindFirst(
                        System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat)?.Value;
                    var changedUnix = new DateTimeOffset(
                        DateTime.SpecifyKind(changedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
                    if (long.TryParse(iat, out var iatUnix) && iatUnix < changedUnix)
                        ctx.Fail("Session expired, please sign in again.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

// Brute-force protection for the public auth endpoints (login/register/google), a per-user cap on
// the cost-bearing LLM chat endpoint, and a generous global backstop so every endpoint has *some*
// limit. Limits are config-driven so the test suite (which hammers endpoints from one loopback IP)
// can raise them.
const string AuthRateLimit = "auth";
const string ChatRateLimit = "chat";
builder.Services.AddRateLimiter(options =>
{
    // Real client IP. On Cloud Run RemoteIpAddress is the proxy, so prefer the left-most
    // X-Forwarded-For hop. NOTE: X-Forwarded-For is client-spoofable — this is a speed bump,
    // not a hard identity boundary.
    static string ClientIp(HttpContext ctx)
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(fwd)
            ? fwd.Split(',')[0].Trim()
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Global backstop applied to every request (partitioned by client IP).
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permit = cfg.GetValue("RateLimiting:Global:PermitLimit", 300);
        var window = cfg.GetValue("RateLimiting:Global:WindowSeconds", 60);
        return RateLimitPartition.GetFixedWindowLimiter(ClientIp(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromSeconds(window),
            QueueLimit = 0
        });
    });

    options.AddPolicy(AuthRateLimit, httpContext =>
    {
        // Read limits from the *resolved* configuration (not builder.Configuration, which doesn't
        // yet see test/factory-injected overrides before Build()).
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permit = cfg.GetValue("RateLimiting:Auth:PermitLimit", 5);
        var window = cfg.GetValue("RateLimiting:Auth:WindowSeconds", 60);
        return RateLimitPartition.GetFixedWindowLimiter(ClientIp(httpContext), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromSeconds(window),
            QueueLimit = 0
        });
    });

    // LLM calls cost real money — throttle per authenticated user (fall back to IP if unauthenticated).
    options.AddPolicy(ChatRateLimit, httpContext =>
    {
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permit = cfg.GetValue("RateLimiting:Chat:PermitLimit", 10);
        var window = cfg.GetValue("RateLimiting:Chat:WindowSeconds", 60);
        var key = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? ClientIp(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromSeconds(window),
            QueueLimit = 0
        });
    });
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            ctx.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many attempts. Please wait a moment and try again." }, token);
    };
});

// Global multipart cap — individual upload endpoints enforce 5 MB via ValidateImageAsync, but
// this stops an oversized request from reaching any controller at all.
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);

// CORS for the separate admin web portal (static SPA on a different origin).
const string PortalCors = "AdminPortal";
var portalOrigins = builder.Configuration.GetSection("Cors:PortalOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddPolicy(PortalCors, p => p
    .WithOrigins(portalOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Validate after Build() so factory-injected config is visible in tests
if (string.IsNullOrWhiteSpace(app.Configuration["Jwt:Secret"]))
    throw new InvalidOperationException(
        "Jwt:Secret is not configured. " +
        "Set it in appsettings.Development.json locally, " +
        "or via the JWT__SECRET environment variable in production.");

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
    DbInitializer.Initialize(context, app.Configuration);
}

// One-off data seeding: `dotnet run --project BlueberryMart.Api -- seed [--orders N …]`
// (or `… -- seed clear`). Runs against the configured connection, then exits without
// starting the web host. Never triggered by a normal container start (no `seed` arg).
if (args.Contains("seed"))
{
    using var seedScope = app.Services.CreateScope();
    var seedCtx = seedScope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
    await DataSeeder.RunAsync(seedCtx, args);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "BlueberryMart API", status = "running", version = "1.0.0" }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors(PortalCors);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();

public partial class Program { }

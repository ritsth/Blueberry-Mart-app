using System.Text;
using BlueberryMart.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    BlueberryMart.Api.Services.EsewaPaymentService>();

// Review image storage: GCS when a bucket is configured, otherwise local filesystem
if (!string.IsNullOrWhiteSpace(builder.Configuration["Gcs:BucketName"]))
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IReviewImageStorage,
        BlueberryMart.Api.Services.GcsReviewImageStorage>();
else
    builder.Services.AddSingleton<BlueberryMart.Api.Services.Interfaces.IReviewImageStorage,
        BlueberryMart.Api.Services.LocalReviewImageStorage>();

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
    });

builder.Services.AddAuthorization();

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
    DbInitializer.Initialize(context);
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }

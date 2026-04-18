using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Lovecraft.Backend.Hubs;
using Lovecraft.Common.Models;

var builder = WebApplication.CreateBuilder(args);

// Table prefix — must be set before any Azure service is constructed
Lovecraft.Backend.Storage.TableNames.Prefix =
    builder.Configuration["AZURE_TABLE_PREFIX"] ?? string.Empty;

// JWT Settings
var jwtSettings = new JwtSettings
{
    SecretKey = builder.Configuration["JWT_SECRET_KEY"]
        ?? throw new InvalidOperationException("JWT_SECRET_KEY environment variable is required"),
    Issuer = "AloeVeraAPI",
    Audience = "AloeVeraClients",
    AccessTokenLifetimeMinutes = 15,
    RefreshTokenLifetimeDays = 7
};

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as camelCase strings so the frontend receives
        // "concert", "male", etc. instead of integer values
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.CamelCase));
    });
builder.Services.AddSignalR();

// Allow up to 20 MB multipart bodies (profile image upload limit)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AloeVera Harmony Meet API", Version = "v1", Description = "Authentication required - use /api/v1/auth endpoints to login" });
});

// Add JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
    
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Trust X-Forwarded-For from nginx/Cloudflare so rate limiter sees the real client IP
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clear default restrictions — all proxies trusted (nginx sits in front)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate limiting — sliding window, 5 requests / 15 min per IP, applied to auth endpoints.
// One shared bucket per IP across all three rate-limited endpoints (login + register +
// forgot-password). A client mixing requests across them exhausts the limit faster than
// per-endpoint limits would — this is intentional (any auth probing counts against the budget).
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("AuthRateLimit", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";

        // SlidingWindowRateLimiter does not populate RetryAfter metadata, so fall back
        // to the known window duration (900 s = 15 min) when metadata is absent.
        var retryAfterSeconds = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)retryAfter.TotalSeconds
            : 900;
        ctx.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

        await ctx.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.ErrorResponse(
                "TOO_MANY_REQUESTS",
                "Too many requests. Please try again later."),
            ct);
    };
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:8080", "http://localhost:5173", "http://localhost:3000",
                           "https://aloeve.club", "https://www.aloeve.club")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Register services
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

var sendGridKey = builder.Configuration["SENDGRID_API_KEY"];
if (!string.IsNullOrEmpty(sendGridKey))
    builder.Services.AddSingleton<IEmailService, SendGridEmailService>();
else
    builder.Services.AddSingleton<IEmailService, NullEmailService>();

var useAzure = builder.Configuration.GetValue<bool>("USE_AZURE_STORAGE");
if (useAzure)
{
    var connectionString = builder.Configuration["AZURE_STORAGE_CONNECTION_STRING"]
        ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING not set");
    builder.Services.AddSingleton(new TableServiceClient(connectionString));
    builder.Services.AddSingleton(new BlobServiceClient(connectionString));
    builder.Services.AddSingleton<IAppConfigService, AzureAppConfigService>();
    builder.Services.AddSingleton<IImageService, AzureImageService>();
    builder.Services.AddSingleton<IAuthService, AzureAuthService>();
    builder.Services.AddSingleton<IUserService, AzureUserService>();
    builder.Services.AddSingleton<IMatchingService>(sp => new AzureMatchingService(
        sp.GetRequiredService<TableServiceClient>(),
        sp.GetRequiredService<IChatService>(),
        sp.GetRequiredService<IUserService>(),
        sp.GetRequiredService<ILogger<AzureMatchingService>>()));
    builder.Services.AddSingleton<IEventService>(sp => new CachingEventService(
        new AzureEventService(
            sp.GetRequiredService<TableServiceClient>(),
            sp.GetRequiredService<IUserService>(),
            sp.GetRequiredService<ILogger<AzureEventService>>()),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<IStoreService>(sp => new CachingStoreService(
        new AzureStoreService(
            sp.GetRequiredService<TableServiceClient>(),
            sp.GetRequiredService<ILogger<AzureStoreService>>()),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<IBlogService>(sp => new CachingBlogService(
        new AzureBlogService(
            sp.GetRequiredService<TableServiceClient>(),
            sp.GetRequiredService<ILogger<AzureBlogService>>()),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<IForumService>(sp => new CachingForumService(
        new AzureForumService(
            sp.GetRequiredService<TableServiceClient>(),
            sp.GetRequiredService<IUserService>(),
            sp.GetRequiredService<ILogger<AzureForumService>>()),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<IChatService, AzureChatService>();
}
else
{
    builder.Services.AddSingleton<IAppConfigService, MockAppConfigService>();
    builder.Services.AddSingleton<IAuthService, MockAuthService>();
    builder.Services.AddSingleton<IUserService>(sp => new MockUserService(
        sp.GetRequiredService<IAppConfigService>()));
    builder.Services.AddSingleton<IEventService>(sp =>
        new MockEventService(sp.GetRequiredService<IUserService>()));
    builder.Services.AddSingleton<IMatchingService>(sp => new MockMatchingService(
        sp.GetRequiredService<IChatService>(),
        sp.GetRequiredService<IUserService>()));
    builder.Services.AddSingleton<IStoreService, MockStoreService>();
    builder.Services.AddSingleton<IBlogService, MockBlogService>();
    builder.Services.AddSingleton<IForumService>(sp =>
        new MockForumService(sp.GetRequiredService<IUserService>()));
    builder.Services.AddSingleton<IChatService, MockChatService>();
    builder.Services.AddSingleton<IImageService, MockImageService>();
}

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AloeVera API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseCors("AllowFrontend");
app.UseHttpsRedirection();

// Authentication & Authorization middleware (order matters!)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Health check endpoint (public)
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0",
    authentication = "Enabled"
}));

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }

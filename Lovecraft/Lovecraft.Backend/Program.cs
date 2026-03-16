using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Azure.Data.Tables;
using Lovecraft.Backend.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Table prefix — must be set before any Azure service is constructed
Lovecraft.Backend.Storage.TableNames.Prefix =
    builder.Configuration["AZURE_TABLE_PREFIX"] ?? string.Empty;

// JWT Settings
var jwtSettings = new JwtSettings
{
    SecretKey = builder.Configuration["JWT_SECRET_KEY"] ?? "your-super-secret-key-min-32-chars!!",
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

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:8080", "http://localhost:5173", "http://localhost:3000")
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

var useAzure = builder.Configuration.GetValue<bool>("USE_AZURE_STORAGE");
if (useAzure)
{
    var connectionString = builder.Configuration["AZURE_STORAGE_CONNECTION_STRING"]
        ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING not set");
    builder.Services.AddSingleton(new TableServiceClient(connectionString));
    builder.Services.AddSingleton<IAuthService, AzureAuthService>();
    builder.Services.AddSingleton<IUserService, AzureUserService>();
    builder.Services.AddSingleton<IMatchingService, AzureMatchingService>();
    builder.Services.AddSingleton<IEventService>(sp => new CachingEventService(
        new AzureEventService(
            sp.GetRequiredService<TableServiceClient>(),
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
            sp.GetRequiredService<ILogger<AzureForumService>>()),
        sp.GetRequiredService<IMemoryCache>()));
    builder.Services.AddSingleton<IChatService, AzureChatService>();
}
else
{
    builder.Services.AddSingleton<IAuthService, MockAuthService>();
    builder.Services.AddSingleton<IUserService, MockUserService>();
    builder.Services.AddSingleton<IEventService, MockEventService>();
    builder.Services.AddSingleton<IMatchingService, MockMatchingService>();
    builder.Services.AddSingleton<IStoreService, MockStoreService>();
    builder.Services.AddSingleton<IBlogService, MockBlogService>();
    builder.Services.AddSingleton<IForumService, MockForumService>();
    builder.Services.AddSingleton<IChatService, MockChatService>();
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

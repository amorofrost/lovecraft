using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/api/auth/login";
        options.LogoutPath = "/api/auth/logout";
        options.Cookie.Name = "aloe_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax; // for SPA + same origin
        options.Cookie.SecurePolicy = CookieSecurePolicy.None; // allow over http in dev; set Always in prod
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Lovecraft BFF", Version = "v1" });
});

// CORS for local Vite dev server (8080)
builder.Services.AddCors(options =>
{
    options.AddPolicy("spa", p => p
        .WithOrigins("http://localhost:8080", "http://127.0.0.1:8080")
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("spa");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Lovecraft.BFF" }));

app.Run();
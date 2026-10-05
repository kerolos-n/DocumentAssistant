using System.Security.Claims;
using System.Text;
using System.Reflection;
using DocumentAssistant.Common.Auth;
using DocumentAssistant.Extensions;
using DocumentAssistant.Features.Auth;
using DocumentAssistant.Data;
using DocumentAssistant.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RealTimeChatAPI.Common.Messaging;

namespace DocumentAssistant;

public static class DependencyInjection
{
    public static void AddAllServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default must be configured for PostgreSQL.");
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key must be configured with a secret of at least 32 bytes.");
        var jwtIssuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer must be configured.");
        var jwtAudience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience must be configured.");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default must be configured for PostgreSQL.");
        }
        if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be a secret of at least 32 bytes.");
        }
        if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must not be empty.");
        }
        var jwtExpirationMinutes = configuration.GetValue<int>("Jwt:ExpirationMinutes", 60);
        var jwtSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddIdentityCore<IdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            }).AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<ICommandHandler<Login.Command, TokenResponse?>, Login.Handler>();
        services.AddScoped<ICommandHandler<Register.Command, TokenResponse?>, Register.Handler>();
        services.AddScoped<ICommandHandler<GetUser.Command, GetUser.UserResponse?>, GetUser.Handler>();
        services.AddEndpoints(Assembly.GetExecutingAssembly());

        services.AddSingleton(new JwtTokenService(jwtKey, jwtIssuer, jwtAudience, jwtExpirationMinutes));
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwtSigningKey,
                    ValidateLifetime = true,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        services.AddAuthorization();

        // The Angular dev server runs on a different origin, so the browser enforces CORS.
        var clientOrigin = configuration["Client:Origin"] ?? "http://localhost:4200";
        services.AddCors(options => options.AddPolicy("client", policy => policy
            .WithOrigins(clientOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()));

        services.AddEndpoints(Assembly.GetExecutingAssembly());
    }
}
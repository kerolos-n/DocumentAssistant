using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Common.Auth;
using DocumentAssistant.Services;
using Microsoft.AspNetCore.Identity;
using RealTimeChatAPI.Common.Messaging;

namespace DocumentAssistant.Features.Auth;

internal static class Login
{
    internal sealed record Command(string? Email, string? Password) : ICommand<TokenResponse?>;

    internal sealed class Handler(
        UserManager<IdentityUser> userManager,
        JwtTokenService tokenService) : ICommandHandler<Command, TokenResponse?>
    {
        public async Task<TokenResponse?> Handle(Command command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrEmpty(command.Password))
            {
                return null;
            }

            var user = await userManager.FindByEmailAsync(command.Email);
            if (user is null || !await userManager.CheckPasswordAsync(user, command.Password))
            {
                return null;
            }

            return tokenService.CreateToken(user);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/login", async (
                Command command,
                ICommandHandler<Command, TokenResponse?> handler,
                CancellationToken cancellationToken) =>
            {
                var token = await handler.Handle(command, cancellationToken);
                return token is null ? Results.Unauthorized() : Results.Ok(token);
            });
        }
    }
}
using System.ComponentModel.DataAnnotations;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Services;
using Microsoft.AspNetCore.Identity;
using RealTimeChatAPI.Common.Messaging;
using DocumentAssistant.Common.Auth;

namespace DocumentAssistant.Features.Auth;

internal static class Register
{
    internal sealed record Command(string? Email, string? Password) : ICommand<TokenResponse?>;

    internal sealed class Handler(
        UserManager<IdentityUser> userManager,
        JwtTokenService tokenService) : ICommandHandler<Command, TokenResponse?>
    {
        public async Task<TokenResponse?> Handle(Command command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command.Email) ||
                !new EmailAddressAttribute().IsValid(command.Email))
            {
                return null;
            }

            if (string.IsNullOrEmpty(command.Password))
            {
                return null;
            }

            var user = new IdentityUser
            {
                UserName = command.Email,
                Email = command.Email,
            };
            var result = await userManager.CreateAsync(user, command.Password);
            if (!result.Succeeded)
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
            app.MapPost("/api/auth/register", async (
                Command command,
                ICommandHandler<Command, TokenResponse?> handler,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(command.Email) ||
                    !new EmailAddressAttribute().IsValid(command.Email))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["email"] = ["Enter a valid email address."],
                    });
                }

                if (string.IsNullOrEmpty(command.Password))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["password"] = ["Password is required."],
                    });
                }

                var token = await handler.Handle(command, cancellationToken);
                return token is null
                    ? Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["registration"] = ["The account could not be created. Check the email and password requirements."],
                    })
                    : Results.Ok(token);
            });
        }
    }
}

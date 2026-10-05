using DocumentAssistant.Common.Auth;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Services;
using FluentValidation;
using Microsoft.AspNetCore.Identity;

namespace DocumentAssistant.Features.Auth;

internal static class Login
{
    internal sealed record Command(string? Email, string? Password) : ICommand<TokenResponse?>;

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            // Only malformed input is rejected here; unknown emails and wrong passwords stay a 401.
            RuleFor(command => command.Email)
                .NotEmpty().WithMessage("Enter your email address.");

            RuleFor(command => command.Password)
                .NotEmpty().WithMessage("Password is required.");
        }
    }

    internal sealed class Handler(
        UserManager<IdentityUser> userManager,
        JwtTokenService tokenService) : ICommandHandler<Command, TokenResponse?>
    {
        public async Task<TokenResponse?> Handle(Command command, CancellationToken cancellationToken)
        {
            var user = await userManager.FindByEmailAsync(command.Email!);
            if (user is null || !await userManager.CheckPasswordAsync(user, command.Password!))
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

using DocumentAssistant.Common.Auth;
using DocumentAssistant.Common.CQRS;
using DocumentAssistant.Common.Endpoints;
using DocumentAssistant.Services;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace DocumentAssistant.Features.Auth;

internal static class Register
{
    internal sealed record Command(string? Email, string? Password) : ICommand<TokenResponse?>;

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Email)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Enter your email address.")
                .EmailAddress().WithMessage("Enter a valid email address.");

            RuleFor(command => command.Password)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.");
        }
    }

    internal sealed class Handler(
        UserManager<IdentityUser> userManager,
        JwtTokenService tokenService) : ICommandHandler<Command, TokenResponse?>
    {
        public async Task<TokenResponse?> Handle(Command command, CancellationToken cancellationToken)
        {
            var user = new IdentityUser
            {
                UserName = command.Email,
                Email = command.Email,
            };
            var result = await userManager.CreateAsync(user, command.Password!);
            if (!result.Succeeded)
            {
                // Identity rules (duplicate email, weak password, ...) surface as 400s too.
                throw new ValidationException(result.Errors.Select(error =>
                    new ValidationFailure("registration", error.Description)));
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
                var token = await handler.Handle(command, cancellationToken);
                return Results.Ok(token);
            });
        }
    }
}

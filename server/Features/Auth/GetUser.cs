using System.Security.Claims;
using DocumentAssistant.Common.Endpoints;
using Microsoft.AspNetCore.Identity;
using RealTimeChatAPI.Common.Messaging;

namespace DocumentAssistant.Features.Auth;

internal static class GetUser
{
    internal sealed record Command(string? UserId) : ICommand<UserResponse?>;

    internal sealed record UserResponse(string UserId, string? Email);

    internal sealed class Handler(UserManager<IdentityUser> userManager)
        : ICommandHandler<Command, UserResponse?>
    {
        public async Task<UserResponse?> Handle(Command command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command.UserId))
            {
                return null;
            }

            var user = await userManager.FindByIdAsync(command.UserId);
            return user is null ? null : new UserResponse(user.Id, user.Email);
        }
    }

    public sealed class Endpoint : IEndpoint
    {
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/me", async (
                ClaimsPrincipal principal,
                ICommandHandler<Command, UserResponse?> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new Command(principal.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await handler.Handle(command, cancellationToken);
                return user is null ? Results.Unauthorized() : Results.Ok(user);
            }).RequireAuthorization();
        }
    }
}

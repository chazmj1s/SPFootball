using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using SaturdayPulse.Contracts;
using SaturdayPulse.Extensions;
using SaturdayPulse.Interfaces;

namespace SaturdayPulse.Filters
{
    /// <summary>
    /// Requires the caller to resolve (via HttpContext.GetUserId() — the same
    /// dual-mode JWT-sub/X-User-Id resolution UserController already uses) to
    /// an existing UserProfile with IsAdmin == true. Runs as an authorization
    /// filter, before model binding and the action itself, so a non-admin
    /// caller never reaches the controller's business logic.
    ///
    /// Requires [Authorize] (or an equivalent auth requirement) already in
    /// effect on the controller/action — this filter only adds the IsAdmin
    /// check on top of "who is this," it does not itself require
    /// authentication. Missing/unresolvable identity -> 401; resolved but
    /// not admin -> 403, matching UserController.SetDevEntitlement's existing
    /// UnauthorizedAccessException -> Forbid() convention.
    /// </summary>
    public class AdminOnlyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var userId = context.HttpContext.GetUserId();
            if (userId == null)
            {
                context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
                return;
            }

            var uow = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
            var profile = await uow.UserProfiles.GetByUserIdAsync(userId, context.HttpContext.RequestAborted);

            if (profile == null || !profile.IsAdmin)
            {
                context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
                return;
            }
        }
    }
}
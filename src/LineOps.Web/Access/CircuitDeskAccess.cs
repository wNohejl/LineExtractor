using LineOps.Desk;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace LineOps.Web.Access;

/// <summary>
/// Who is at this desk, read from the circuit's signed-in user.
///
/// <para>
/// Open mode answers "operator" without asking, which is how the laptop has always run. Gated
/// mode asks the authentication state each time — it is fixed for the life of a circuit,
/// because signing in or out is a page load that starts a new one — and fails closed: a state
/// not yet known, or one that throws, reads as a visitor.
/// </para>
/// </summary>
public sealed class CircuitDeskAccess(AuthenticationStateProvider authentication, IOptions<AccessOptions> options)
    : IDeskAccess
{
    public bool CanOperate => options.Value.Mode == AccessMode.Open || SignedInAsOperator();

    private bool SignedInAsOperator()
    {
        try
        {
            var state = authentication.GetAuthenticationStateAsync();
            return state.IsCompletedSuccessfully && state.Result.User.IsInRole(AccessRoles.Operator);
        }
        catch (InvalidOperationException)
        {
            // Asked before the circuit was given its user.
            return false;
        }
    }
}

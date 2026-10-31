using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace LineOps.Desk.Hosting;

/// <summary>
/// Who is at this desk, read from the circuit's signed-in user.
///
/// <para>
/// Open mode answers "operator" without asking. Gated mode asks the authentication state each
/// time — it is fixed for the life of a circuit, because signing in or out is a page load that
/// starts a new one — and fails closed: a state not yet known, or one that throws, reads as a
/// visitor.
/// </para>
///
/// <para>
/// It also tells the desk where the gate is (<see cref="DeskAccessOptions.SignInPath"/> and
/// <see cref="DeskAccessOptions.SignOutPath"/>), so the Window manager — which knows no gate of
/// its own — can link to sign-in and sign-out.
/// </para>
/// </summary>
public sealed class DeskCircuitAccess(AuthenticationStateProvider authentication, IOptions<DeskAccessOptions> options)
    : IDeskAccess
{
    public bool CanOperate => options.Value.Mode == DeskAccessMode.Open || SignedInAsOperator();

    public bool IsGated => options.Value.Mode == DeskAccessMode.Gated;

    public string? SignInPath => options.Value.SignInPath;

    public string? SignOutPath => options.Value.SignOutPath;

    private bool SignedInAsOperator()
    {
        try
        {
            var state = authentication.GetAuthenticationStateAsync();
            return state.IsCompletedSuccessfully && state.Result.User.IsInRole(DeskAccessRoles.Operator);
        }
        catch (InvalidOperationException)
        {
            // Asked before the circuit was given its user.
            return false;
        }
    }
}

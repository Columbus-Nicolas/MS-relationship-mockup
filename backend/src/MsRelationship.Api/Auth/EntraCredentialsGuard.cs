namespace MsRelationship.Api.Auth;

/// <summary>
/// Fails startup loudly outside Development when <c>AzureAd:ClientId</c> is still the
/// non-functional placeholder from <c>appsettings.json</c> (see <c>Program.cs</c> for why the
/// placeholder exists at all). Without this, a deployment with a placeholder tenant/client id
/// boots clean and <c>/health</c> reports <c>200 ok</c> — it is <c>[AllowAnonymous]</c> and never
/// touches the auth handler — while every real, token-bearing request 403s or 500s. A
/// health-probe-gated rollout would see that as a healthy deployment. Development is exempt so
/// local runs and the test suite, which never configure a real tenant, stay bootable.
/// </summary>
public static class EntraCredentialsGuard
{
    public const string PlaceholderClientId = "00000000-0000-0000-0000-000000000000";

    public static void Validate(IConfiguration config, IWebHostEnvironment env)
    {
        if (env.IsDevelopment()) return;

        var clientId = config["AzureAd:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId) || clientId == PlaceholderClientId)
        {
            throw new InvalidOperationException(
                "AzureAd:ClientId is empty or still the placeholder GUID. Refusing to start " +
                "outside Development: the app registration must be configured (AZUREAD__CLIENTID " +
                "and friends) before this environment can serve real traffic.");
        }
    }
}

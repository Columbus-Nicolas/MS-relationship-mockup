namespace MsRelationship.Api.Auth;

/// <summary>
/// Fails startup loudly when <c>DEV_MODE</c> is enabled outside Development, mirroring
/// <see cref="EntraCredentialsGuard"/> — same shape, opposite direction. That guard refuses to
/// start when real credentials are *missing*; this one refuses to start when a local development
/// shortcut is *present* somewhere it must never be.
///
/// Dev mode replaces Entra authentication with a header — anyone who can reach the port becomes
/// whoever they claim to be. That is exactly right on a laptop and catastrophic anywhere else, so
/// the failure mode has to be a refusal to boot rather than a warning in a log nobody reads. An
/// environment variable set once in the wrong place (a copied <c>.env</c>, an inherited compose
/// file, a CI job that exports the developer's local settings) is a realistic accident, and
/// without this it would produce a *working* deployment with no authentication at all — the
/// worst possible outcome, because nothing about it looks broken.
///
/// <b>The flag is read twice, at two different moments, and that is deliberate.</b>
/// <see cref="IsEnabled"/> runs against the *builder's* configuration, because whether to
/// register the dev authentication scheme has to be decided before the service container is
/// built and cannot be revisited afterwards. <see cref="Validate"/> then runs against the *built
/// app's* configuration, which is the final, authoritative view — it is the one that decides
/// whether the process is allowed to keep running. Splitting them this way means the safety check
/// sees every configuration source, including any added late, while the registration decision
/// stays conservative: a flag that appears only after the container was built cannot switch
/// authentication off, because by then the real scheme is already registered.
///
/// That split has one confusing failure mode, which is why <see cref="Validate"/> takes
/// <c>registeredDevMode</c>: configuration supplied late (a test's <c>ConfigureAppConfiguration</c>,
/// say) sets the flag where the safety check can see it but the registration decision cannot, so
/// dev mode would silently not happen while every setting insists it should. Rather than let that
/// present as a baffling 401, it is an explicit error naming the cause.
/// </summary>
public static class DevModeGuard
{
    public const string ConfigKey = "DEV_MODE";

    private static readonly string[] True = ["true", "1", "yes", "on"];
    private static readonly string[] False = ["false", "0", "no", "off"];

    /// <summary>
    /// Reads the flag without <c>GetValue&lt;bool&gt;</c>, which throws on an empty string. That
    /// matters because .env's own convention is to leave unset values blank — POSTGRES_PASSWORD,
    /// AZUREAD__TENANTID and friends all ship that way — so <c>DEV_MODE=</c> is a natural thing to
    /// write and must mean "off", not a startup crash with a configuration-binding stack trace.
    ///
    /// Unrecognised values are an error rather than a quiet false. Someone who writes
    /// <c>DEV_MODE=enabled</c> has stated an intention, and silently ignoring it would leave them
    /// staring at 401s with a configuration file that plainly says it should be working.
    /// </summary>
    private static bool Requested(IConfiguration config)
    {
        var raw = config[ConfigKey];
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim().ToLowerInvariant();
        if (True.Contains(value)) return true;
        if (False.Contains(value)) return false;

        throw new InvalidOperationException(
            $"{ConfigKey} is set to '{raw}', which is not a recognised boolean. " +
            $"Use one of {string.Join(", ", True)} or {string.Join(", ", False)}, or leave it empty.");
    }

    /// <summary>
    /// True only when the flag is set <em>and</em> the environment is Development. Both halves are
    /// required: <see cref="Validate"/> already makes "flag on, not Development" unreachable, so
    /// the environment check here is redundant by construction — which is the point. If the guard
    /// were ever removed or weakened, every dev-mode branch in <c>Program.cs</c> would still be
    /// closed, rather than the whole auth bypass hinging on one call at startup.
    /// </summary>
    public static bool IsEnabled(IConfiguration config, IWebHostEnvironment env) =>
        Requested(config) && env.IsDevelopment();

    /// <summary>
    /// The authoritative check, run against the built app's configuration.
    /// <paramref name="registeredDevMode"/> is what <see cref="IsEnabled"/> returned earlier,
    /// against the builder.
    /// </summary>
    public static void Validate(IConfiguration config, IWebHostEnvironment env, bool registeredDevMode)
    {
        var requested = Requested(config);
        if (!requested)
        {
            // Nothing asked for dev mode now — but if it was registered, the flag was visible
            // earlier and has since disappeared. The bypass is live and the configuration no
            // longer admits it, which is not a state worth guessing about.
            if (registeredDevMode)
                throw new InvalidOperationException(
                    $"{ConfigKey} was set when services were configured but is no longer set. " +
                    "The dev authentication scheme is already registered and cannot be withdrawn.");
            return;
        }

        if (!env.IsDevelopment())
            throw new InvalidOperationException(
                $"{ConfigKey} is enabled but the environment is '{env.EnvironmentName}', not Development. " +
                "Dev mode replaces Entra authentication with a plain request header and seeds a " +
                "privileged developer account, so it must never run outside a local machine. " +
                $"Unset {ConfigKey} (or remove it from .env) for this environment.");

        if (!registeredDevMode)
            throw new InvalidOperationException(
                $"{ConfigKey} is set, but it was not visible when the application registered its " +
                "services, so dev mode is NOT active and every request will be refused. The flag is " +
                "read before the service container is built, so it has to come from a source that " +
                "exists by then: the process environment (which is where .env is loaded to) or " +
                "appsettings. Configuration added after the host builder was created — a test's " +
                "ConfigureAppConfiguration, for instance — arrives too late to take effect.");
    }
}

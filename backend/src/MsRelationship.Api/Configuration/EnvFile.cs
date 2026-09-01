namespace MsRelationship.Api.Configuration;

/// <summary>
/// Loads the repository-root <c>.env</c> into the process environment before the host builder
/// snapshots it.
///
/// This exists because <c>.env</c> is not a thing ASP.NET Core knows about. Docker Compose reads
/// it automatically, which makes it look like shared configuration, but
/// <c>WebApplication.CreateBuilder</c> only ever reads appsettings, user secrets and *process*
/// environment variables — so every <c>AZUREAD__*</c> / <c>ConnectionStrings__Default</c> value
/// in that file reached the API only if a human remembered to export it first. Worse, the
/// obvious way to do that by hand (<c>set -a; source .env</c>) silently corrupts the one value
/// that matters most: the connection string contains semicolons, the shell treats them as
/// command separators, and the variable ends up as <c>Host=localhost</c> with the database, user
/// and password quietly dropped. Parsing the file ourselves removes the shell from the loop
/// entirely, so the file works exactly as written, quoted or not.
///
/// Two deliberate restrictions:
///
///  - <b>Development only</b>, decided by the <c>ASPNETCORE_ENVIRONMENT</c> *process* variable
///    rather than <c>IWebHostEnvironment</c>, because this has to run before the builder exists.
///    <c>dotnet run</c> sets that variable from <c>launchSettings.json</c>; a deployed host sets
///    it explicitly; <c>WebApplicationFactory</c> sets its environment on the builder and leaves
///    the process variable alone. That last case is the important one — it means the test suite
///    never picks up whatever a developer happens to have in their untracked local <c>.env</c>,
///    so test behaviour cannot depend on a file that is not in the repository.
///  - <b>No clobber</b>: a variable already present in the environment always wins over the file.
///    That keeps the normal precedence people expect (explicit export &gt; file) and means CI,
///    which injects real environment variables, is unaffected even if a <c>.env</c> is present.
/// </summary>
public static class EnvFile
{
    public const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// Loads the nearest <c>.env</c> at or above <paramref name="startDirectory"/>, if the
    /// environment is Development and such a file exists. Returns the path that was loaded, or
    /// <c>null</c> when nothing was loaded — callers use that to log which file is in effect.
    /// </summary>
    public static string? Load(string startDirectory, string? environmentName)
    {
        if (!string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = Find(startDirectory);
        if (path is null) return null;

        DotNetEnv.Env.Load(path, new DotNetEnv.LoadOptions(
            setEnvVars: true, clobberExistingVars: false, onlyExactPath: true));
        return path;
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a <c>.env</c>. The API runs
    /// out of <c>backend/src/MsRelationship.Api/bin/&lt;config&gt;/net9.0</c>, so the repository
    /// root that actually holds the file is five levels up — searching upwards rather than
    /// hard-coding that depth keeps this working under <c>dotnet run</c>, a published output and
    /// an IDE launch alike.
    /// </summary>
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

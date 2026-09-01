using MsRelationship.Api.Configuration;
using Xunit;

namespace MsRelationship.Api.Tests;

/// <summary>
/// <c>.env</c> is not something ASP.NET Core reads on its own, so the API parses it directly.
/// The two properties worth pinning down are the ones that would cause quiet damage if they
/// regressed: it must not load outside Development (or the test suite would start depending on
/// an untracked file on whichever machine it runs on), and it must not overwrite variables that
/// are already set (or a real deployment's environment could be silently replaced by a stray
/// checked-out file).
/// </summary>
public class EnvFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"envfile-{Guid.NewGuid():N}");

    private string Nested()
    {
        // Mimics the real layout: the file sits at the repository root, the app runs several
        // directories below it in bin/<config>/<tfm>.
        var nested = Path.Combine(_root, "backend", "src", "Api", "bin", "Debug", "net9.0");
        Directory.CreateDirectory(nested);
        return nested;
    }

    private void WriteEnv(string contents) => File.WriteAllText(Path.Combine(_root, ".env"), contents);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Find_walks_up_to_the_repository_root()
    {
        var nested = Nested();
        WriteEnv("A=1\n");

        Assert.Equal(Path.Combine(_root, ".env"), EnvFile.Find(nested));
    }

    [Fact]
    public void Find_returns_null_when_there_is_no_env_file()
    {
        Assert.Null(EnvFile.Find(Nested()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Does_not_load_outside_development(string? environmentName)
    {
        var nested = Nested();
        var key = $"ENVFILE_TEST_{Guid.NewGuid():N}";
        WriteEnv($"{key}=from-file\n");

        Assert.Null(EnvFile.Load(nested, environmentName));
        Assert.Null(Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Loads_in_development_and_returns_the_path_it_used()
    {
        var nested = Nested();
        var key = $"ENVFILE_TEST_{Guid.NewGuid():N}";
        WriteEnv($"{key}=from-file\n");

        try
        {
            Assert.Equal(Path.Combine(_root, ".env"), EnvFile.Load(nested, "Development"));
            Assert.Equal("from-file", Environment.GetEnvironmentVariable(key));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    /// <summary>
    /// The connection string in this repository's own .env contains semicolons and is written
    /// unquoted. Sourcing that file in a shell truncates it at the first semicolon — the whole
    /// reason parsing happens here instead — so the parser must carry the entire value through.
    /// </summary>
    [Fact]
    public void Reads_an_unquoted_value_containing_semicolons_intact()
    {
        var nested = Nested();
        var key = $"ENVFILE_TEST_{Guid.NewGuid():N}";
        const string expected = "Host=localhost;Port=5433;Database=rmap;Username=rmap;Password=secret";
        WriteEnv($"{key}={expected}\n");

        try
        {
            EnvFile.Load(nested, "Development");
            Assert.Equal(expected, Environment.GetEnvironmentVariable(key));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Fact]
    public void An_existing_environment_variable_wins_over_the_file()
    {
        var nested = Nested();
        var key = $"ENVFILE_TEST_{Guid.NewGuid():N}";
        WriteEnv($"{key}=from-file\n");

        try
        {
            Environment.SetEnvironmentVariable(key, "already-set");
            EnvFile.Load(nested, "Development");
            Assert.Equal("already-set", Environment.GetEnvironmentVariable(key));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }
}

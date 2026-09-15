using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MatchTests
{
    private readonly PostgresFixture _pg;
    public MatchTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task An_exact_key_returns_the_existing_person()
    {
        await using var db = _pg.NewContext();
        var existing = MsProfile.Create("Liang Ye", "liang.ye@microsoft.com", null);
        db.MsProfiles.Add(existing);
        await db.SaveChangesAsync();

        var result = await new MsProfileMatcher(db).MatchAsync("Liang Ye", "Liang.Ye@microsoft.com", null);

        Assert.NotNull(result.Exact);
        Assert.Equal(existing.Id, result.Exact!.Id);
    }

    [Fact]
    public async Task The_same_name_without_an_email_is_suspected_not_assumed()
    {
        await using var db = _pg.NewContext();
        db.MsProfiles.Add(MsProfile.Create("Henrik Ditlevsen", "henrik.d@microsoft.com", null));
        await db.SaveChangesAsync();

        var result = await new MsProfileMatcher(db).MatchAsync("Henrik Ditlevsen", null, null);

        Assert.Null(result.Exact);
        Assert.Contains(result.Suspected, p => p.Name == "Henrik Ditlevsen");
    }

    [Fact]
    public async Task Doubled_internal_whitespace_does_not_hide_a_suspected_match()
    {
        await using var db = _pg.NewContext();
        db.MsProfiles.Add(MsProfile.Create("Anne  Berg", "anne.berg@microsoft.com", null));
        await db.SaveChangesAsync();

        var result = await new MsProfileMatcher(db).MatchAsync("Anne Berg", null, null);

        Assert.Null(result.Exact);
        Assert.Contains(result.Suspected, p => p.Name == "Anne Berg");
    }
}

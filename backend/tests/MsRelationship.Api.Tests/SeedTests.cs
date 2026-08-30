using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SeedTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeding_loads_the_approved_dataset()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        Assert.Equal(23, await db.MsProfiles.CountAsync());
        Assert.Equal(9, await db.MsDomains.CountAsync());       // eight agreed domains plus Unmarked
        Assert.True(await db.MsDomains.AnyAsync(d => d.IsSystem));
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);
        var before = await db.MsProfiles.CountAsync();
        await Seeder.SeedAsync(db);

        Assert.Equal(before, await db.MsProfiles.CountAsync());
    }

    [Fact]
    public async Task Seeds_the_full_mockup_dataset_including_columbus_users_and_relations()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        Assert.Equal(14, await db.ColumbusUsers.CountAsync());
        Assert.Equal(31, await db.Relations.CountAsync());
    }

    [Fact]
    public async Task Seeding_twice_leaves_columbus_users_and_relations_unchanged()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);
        var usersBefore = await db.ColumbusUsers.CountAsync();
        var relationsBefore = await db.Relations.CountAsync();

        await Seeder.SeedAsync(db);

        Assert.Equal(usersBefore, await db.ColumbusUsers.CountAsync());
        Assert.Equal(relationsBefore, await db.Relations.CountAsync());
    }

    [Fact]
    public async Task Seeded_relations_land_without_relation_history_rows()
    {
        // Seed data is bootstrap state, not an application mutation — see Seeder.cs for the
        // reasoning. Confirms the deliberate choice to write relations directly rather than
        // through RelationWriter.
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        Assert.Equal(0, await db.RelationHistory.CountAsync());
    }

    [Fact]
    public async Task Seeded_relation_scores_are_all_within_the_allowed_range()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        var scores = await db.Relations.Select(r => r.Score).ToListAsync();
        Assert.All(scores, s => Assert.InRange(s, -3, 3));
    }

    [Fact]
    public async Task A_known_mockup_relation_maps_to_the_right_pair_and_score()
    {
        // r31: c5 (Rikke Dalsgaard) -> m23 (Lars-Bo Heidemann), score -3.
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        var rikke = await db.ColumbusUsers.SingleAsync(u => u.Name == "Rikke Dalsgaard");
        var larsBo = await db.MsProfiles.SingleAsync(p => p.Name == "Lars-Bo Heidemann");
        var relation = await db.Relations.SingleAsync(r =>
            r.ColumbusUserId == rikke.Id && r.MsProfileId == larsBo.Id);

        Assert.Equal(-3, relation.Score);
        Assert.Equal("Repeated attempts at contact unanswered.", relation.Note);
    }

    [Fact]
    public async Task Domains_and_groups_and_sources_and_departments_are_seeded_as_rows()
    {
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        Assert.Equal(4, await db.MsGroups.CountAsync());
        Assert.Equal(5, await db.MsSources.CountAsync());
        Assert.Equal(9, await db.CbDepartments.CountAsync());
    }

    [Fact]
    public async Task A_tentative_ms_profile_and_a_superadmin_columbus_user_round_trip()
    {
        // m14 (Bo Larsen) is marked tentative in the mockup; c2 (Jesper Winther) is superadmin.
        await using var db = fixture.NewContext();
        await Seeder.SeedAsync(db);

        var boLarsen = await db.MsProfiles.SingleAsync(p => p.Name == "Bo Larsen");
        Assert.True(boLarsen.IsTentative);

        var jesper = await db.ColumbusUsers.SingleAsync(u => u.Name == "Jesper Winther");
        Assert.Equal(UserRole.SuperAdmin, jesper.Role);
    }
}

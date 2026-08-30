using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.MsProfiles;
using MsRelationship.Api.Features.Relations;
using MsRelationship.Api.Features.Submissions;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MergeTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------- happy path

    [Fact]
    public async Task Relations_move_to_the_survivor()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, duplicate.Id, 2, "knows them", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        Assert.True(await db.Relations.AnyAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == user.Id));
        Assert.False(await db.Relations.AnyAsync(r => r.MsProfileId == duplicate.Id));
    }

    [Fact]
    public async Task A_moved_relation_is_recorded_on_both_profiles_as_MergeMoved()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, duplicate.Id, 2, "knows them", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        // Arrival on the survivor: nothing there before, the duplicate's value now.
        Assert.True(await db.RelationHistory.AnyAsync(h =>
            h.MsProfileId == survivor.Id && h.ColumbusUserId == user.Id &&
            h.ChangeType == RelationChangeType.MergeMoved &&
            h.OldScore == null && h.NewScore == 2 && h.NewNote == "knows them"));
        // Departure from the duplicate: the value that left, and nothing after it.
        Assert.True(await db.RelationHistory.AnyAsync(h =>
            h.MsProfileId == duplicate.Id && h.ColumbusUserId == user.Id &&
            h.ChangeType == RelationChangeType.MergeMoved &&
            h.OldScore == 2 && h.NewScore == null));
    }

    [Fact]
    public async Task The_duplicate_is_tombstoned_not_deleted()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        var stored = await db.MsProfiles.SingleAsync(p => p.Id == duplicate.Id);
        Assert.Equal(survivor.Id, stored.MergedIntoId);
    }

    [Fact]
    public async Task Domains_are_unioned()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var domainA = new MsDomain { Id = Guid.NewGuid(), Name = $"A-{Guid.NewGuid():N}" };
        var domainB = new MsDomain { Id = Guid.NewGuid(), Name = $"B-{Guid.NewGuid():N}" };
        db.MsDomains.AddRange(domainA, domainB);
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = survivor.Id, DomainId = domainA.Id });
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = duplicate.Id, DomainId = domainB.Id });
        await db.SaveChangesAsync();

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        var domains = await db.MsProfileDomains.Where(x => x.MsProfileId == survivor.Id).ToListAsync();
        Assert.Equal(2, domains.Count);
    }

    [Fact]
    public async Task A_domain_both_profiles_already_share_is_not_duplicated()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var shared = new MsDomain { Id = Guid.NewGuid(), Name = $"S-{Guid.NewGuid():N}" };
        db.MsDomains.Add(shared);
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = survivor.Id, DomainId = shared.Id });
        db.MsProfileDomains.Add(new MsProfileDomain { MsProfileId = duplicate.Id, DomainId = shared.Id });
        await db.SaveChangesAsync();

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        Assert.Single(await db.MsProfileDomains.Where(x => x.MsProfileId == survivor.Id).ToListAsync());
    }

    [Fact]
    public async Task Blanks_on_the_survivor_are_filled_from_the_duplicate()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        survivor.Title = "";
        duplicate.Title = "Partner Manager";
        duplicate.Email = $"dup-{Guid.NewGuid():N}@microsoft.com";
        duplicate.Notes = "met at Ignite";
        await db.SaveChangesAsync();

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        await using var verify = fixture.NewContext();
        var stored = await verify.MsProfiles.SingleAsync(p => p.Id == survivor.Id);
        Assert.Equal("Partner Manager", stored.Title);
        Assert.Equal(duplicate.Email, stored.Email);
        Assert.Equal("met at Ignite", stored.Notes);
        // Adopting the duplicate's email rewrites the survivor's generated identity_key. That
        // only survives the filtered unique index because the duplicate is tombstoned first.
        Assert.Equal(duplicate.Email, stored.IdentityKey);
    }

    [Fact]
    public async Task A_value_the_survivor_already_has_is_never_overwritten()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        survivor.Title = "Cloud Solution Architect";
        duplicate.Title = "Partner Manager";
        await db.SaveChangesAsync();

        await new MsProfileMerger(db, new RelationWriter(db)).MergeAsync(survivor.Id, [duplicate.Id], actor.Id);

        await using var verify = fixture.NewContext();
        Assert.Equal("Cloud Solution Architect",
            (await verify.MsProfiles.SingleAsync(p => p.Id == survivor.Id)).Title);
    }

    // ------------------------------------------------- the unique-index collision (spec §6)

    [Fact]
    public async Task A_collision_keeps_the_higher_score_and_records_the_loser()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, survivor.Id, 1, "weak", user.Id);
        await writer.UpsertAsync(user.Id, duplicate.Id, 3, "strong", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == user.Id);
        Assert.Equal(3, kept.Score);
        Assert.True(await db.RelationHistory.AnyAsync(h =>
            h.ChangeType == RelationChangeType.MergeDiscarded && h.OldScore == 1));
    }

    [Fact]
    public async Task A_collision_the_survivor_wins_keeps_its_value_and_records_the_duplicates()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, survivor.Id, 3, "strong", user.Id);
        await writer.UpsertAsync(user.Id, duplicate.Id, 1, "weak", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == user.Id);
        Assert.Equal(3, kept.Score);
        Assert.Equal("strong", kept.Note);
        Assert.False(await db.Relations.AnyAsync(r => r.MsProfileId == duplicate.Id));
        // The value that lost is preserved on the row it was discarded from.
        Assert.True(await db.RelationHistory.AnyAsync(h =>
            h.MsProfileId == duplicate.Id && h.ColumbusUserId == user.Id &&
            h.ChangeType == RelationChangeType.MergeDiscarded &&
            h.OldScore == 1 && h.OldNote == "weak" && h.NewScore == null));
    }

    [Fact]
    public async Task A_tied_collision_leaves_the_survivors_own_value_standing()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, survivor.Id, 2, "survivor note", user.Id);
        await writer.UpsertAsync(user.Id, duplicate.Id, 2, "duplicate note", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        var kept = await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id && r.ColumbusUserId == user.Id);
        Assert.Equal(2, kept.Score);
        Assert.Equal("survivor note", kept.Note);
        Assert.True(await db.RelationHistory.AnyAsync(h =>
            h.MsProfileId == duplicate.Id && h.ChangeType == RelationChangeType.MergeDiscarded &&
            h.OldNote == "duplicate note"));
    }

    [Fact]
    public async Task A_collision_never_leaves_two_relations_for_the_same_user()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, survivor.Id, 1, "weak", user.Id);
        await writer.UpsertAsync(user.Id, duplicate.Id, 3, "strong", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        await using var verify = fixture.NewContext();
        Assert.Single(await verify.Relations.Where(r => r.ColumbusUserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task Relations_of_other_users_move_untouched_alongside_a_collision()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var colliding = await Seed.UserAsync(db);
        var bystander = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(colliding.Id, survivor.Id, 1, "weak", colliding.Id);
        await writer.UpsertAsync(colliding.Id, duplicate.Id, 3, "strong", colliding.Id);
        await writer.UpsertAsync(bystander.Id, duplicate.Id, -2, "avoids", bystander.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], colliding.Id);

        await using var verify = fixture.NewContext();
        var onSurvivor = await verify.Relations.Where(r => r.MsProfileId == survivor.Id).ToListAsync();
        Assert.Equal(2, onSurvivor.Count);
        Assert.Equal(3, onSurvivor.Single(r => r.ColumbusUserId == colliding.Id).Score);
        Assert.Equal(-2, onSurvivor.Single(r => r.ColumbusUserId == bystander.Id).Score);
    }

    // ---------------------------------------------------------------- tombstone graph

    [Fact]
    public async Task Merging_a_profile_into_itself_is_refused()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var merger = new MsProfileMerger(db, new RelationWriter(db));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAsync(survivor.Id, [survivor.Id], actor.Id));

        Assert.Contains("itself", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await db.MsProfiles.SingleAsync(p => p.Id == survivor.Id)).MergedIntoId);
    }

    [Fact]
    public async Task Merging_into_an_already_tombstoned_survivor_is_refused()
    {
        await using var db = fixture.NewContext();
        var real = await Seed.ProfileAsync(db);
        var tombstoned = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var merger = new MsProfileMerger(db, new RelationWriter(db));
        await merger.MergeAsync(real.Id, [tombstoned.Id], actor.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAsync(tombstoned.Id, [duplicate.Id], actor.Id));

        Assert.Contains("already merged", ex.Message, StringComparison.OrdinalIgnoreCase);
        await using var verify = fixture.NewContext();
        Assert.Null((await verify.MsProfiles.SingleAsync(p => p.Id == duplicate.Id)).MergedIntoId);
    }

    [Fact]
    public async Task Merging_a_profile_that_was_already_merged_elsewhere_is_refused()
    {
        await using var db = fixture.NewContext();
        var firstSurvivor = await Seed.ProfileAsync(db);
        var secondSurvivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var merger = new MsProfileMerger(db, new RelationWriter(db));
        await merger.MergeAsync(firstSurvivor.Id, [duplicate.Id], actor.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAsync(secondSurvivor.Id, [duplicate.Id], actor.Id));

        Assert.Contains("already merged", ex.Message, StringComparison.OrdinalIgnoreCase);
        await using var verify = fixture.NewContext();
        Assert.Equal(firstSurvivor.Id, (await verify.MsProfiles.SingleAsync(p => p.Id == duplicate.Id)).MergedIntoId);
    }

    [Fact]
    public async Task Re_merging_the_same_duplicate_changes_nothing()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, duplicate.Id, 2, "knows them", user.Id);
        var merger = new MsProfileMerger(db, writer);
        await merger.MergeAsync(survivor.Id, [duplicate.Id], user.Id);
        var historyCount = await db.RelationHistory.CountAsync();

        await merger.MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        Assert.Equal(historyCount, await db.RelationHistory.CountAsync());
        Assert.Equal(2, (await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id)).Score);
    }

    [Fact]
    public async Task Tombstones_pointing_at_a_merged_away_profile_are_repointed()
    {
        // D -> S, then S -> T. The graph must stay one hop deep: D -> T, never D -> S -> T.
        await using var db = fixture.NewContext();
        var t = await Seed.ProfileAsync(db);
        var s = await Seed.ProfileAsync(db);
        var d = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var merger = new MsProfileMerger(db, new RelationWriter(db));
        await merger.MergeAsync(s.Id, [d.Id], actor.Id);

        await merger.MergeAsync(t.Id, [s.Id], actor.Id);

        await using var verify = fixture.NewContext();
        Assert.Equal(t.Id, (await verify.MsProfiles.SingleAsync(p => p.Id == d.Id)).MergedIntoId);
        Assert.Equal(t.Id, (await verify.MsProfiles.SingleAsync(p => p.Id == s.Id)).MergedIntoId);
    }

    [Fact]
    public async Task An_empty_merge_list_changes_nothing()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, survivor.Id, 2, "unchanged", user.Id);
        var before = await db.RelationHistory.CountAsync();

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [], user.Id);

        Assert.Equal(before, await db.RelationHistory.CountAsync());
        Assert.Equal(2, (await db.Relations.SingleAsync(r => r.MsProfileId == survivor.Id)).Score);
    }

    [Fact]
    public async Task An_unknown_survivor_is_refused()
    {
        await using var db = fixture.NewContext();
        var duplicate = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var merger = new MsProfileMerger(db, new RelationWriter(db));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAsync(Guid.NewGuid(), [duplicate.Id], actor.Id));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_duplicate_is_refused()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var actor = await Seed.UserAsync(db);
        var merger = new MsProfileMerger(db, new RelationWriter(db));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAsync(survivor.Id, [Guid.NewGuid()], actor.Id));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- atomicity

    [Fact]
    public async Task A_merge_that_fails_part_way_rolls_everything_back()
    {
        await using var db = fixture.NewContext();
        var otherSurvivor = await Seed.ProfileAsync(db);
        var survivor = await Seed.ProfileAsync(db);
        var good = await Seed.ProfileAsync(db);
        var poisoned = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, good.Id, 2, "knows them", user.Id);
        var merger = new MsProfileMerger(db, writer);
        // `poisoned` is already tombstoned elsewhere, so it will be refused — but only after
        // `good` has been fully processed inside the same transaction.
        await merger.MergeAsync(otherSurvivor.Id, [poisoned.Id], user.Id);
        var historyBefore = await db.RelationHistory.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => merger.MergeAsync(survivor.Id, [good.Id, poisoned.Id], user.Id));

        // A fresh context: the failed merger's own change tracker still holds the rolled-back
        // in-memory edits, so only the database can answer this.
        await using var verify = fixture.NewContext();
        Assert.Null((await verify.MsProfiles.SingleAsync(p => p.Id == good.Id)).MergedIntoId);
        Assert.True(await verify.Relations.AnyAsync(r => r.MsProfileId == good.Id && r.Score == 2));
        Assert.False(await verify.Relations.AnyAsync(r => r.MsProfileId == survivor.Id));
        Assert.Equal(historyBefore, await verify.RelationHistory.CountAsync());
    }

    // ---------------------------------------------------------------- neighbours

    [Fact]
    public async Task Pending_submission_items_follow_the_survivor()
    {
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        var submissions = new SubmissionService(db, writer);
        await submissions.SubmitAsync(user.Id, [new SubmissionDraft(duplicate.Id, SubmissionAction.Upsert, 3, "new")]);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        await using var verify = fixture.NewContext();
        Assert.True(await verify.SubmissionItems.AnyAsync(i => i.MsProfileId == survivor.Id));
        Assert.False(await verify.SubmissionItems.AnyAsync(i => i.MsProfileId == duplicate.Id));
    }

    // ---------------------------------------------------------------- concurrency (Fix 1)

    /// <summary>
    /// Reproduces the tombstone-write race from the final review deterministically, with no
    /// threads or sleeps — mirroring
    /// <see cref="SuperAdminTransferTests.Two_concurrent_transfers_from_the_same_holder_do_not_both_succeed"/>.
    /// <see cref="MsProfile"/> carries no concurrency token, so before the fix the "already
    /// merged elsewhere" guard was a plain read (the duplicate's stale, still-tracked
    /// <c>MergedIntoId == null</c>) followed by an unconditional tracked write: two contexts
    /// both load the duplicate while it is still live, one context fully merges it into
    /// <c>survivorB</c> and commits, and the other's later write — issued from its now-stale
    /// belief that the duplicate is still unmerged — must fail instead of silently overwriting
    /// the tombstone onto <c>survivorA</c> while the duplicate's relation already committed onto
    /// <c>survivorB</c>.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_merges_of_the_same_duplicate_into_different_survivors_do_not_both_succeed()
    {
        await using var seedDb = fixture.NewContext();
        var survivorA = await Seed.ProfileAsync(seedDb);
        var survivorB = await Seed.ProfileAsync(seedDb);
        var duplicate = await Seed.ProfileAsync(seedDb);
        var user = await Seed.UserAsync(seedDb);
        var actor = await Seed.UserAsync(seedDb);
        await new RelationWriter(seedDb).UpsertAsync(user.Id, duplicate.Id, 2, "knows them", actor.Id);

        await using var dbA = fixture.NewContext();
        await using var dbB = fixture.NewContext();

        // Both contexts load (and thus track) the duplicate while it is still live, before
        // either merge commits.
        await dbA.MsProfiles.SingleAsync(p => p.Id == duplicate.Id);
        await dbB.MsProfiles.SingleAsync(p => p.Id == duplicate.Id);

        var mergerA = new MsProfileMerger(dbA, new RelationWriter(dbA));
        var mergerB = new MsProfileMerger(dbB, new RelationWriter(dbB));

        // Context B completes and commits a full merge into survivorB first.
        await mergerB.MergeAsync(survivorB.Id, [duplicate.Id], actor.Id);

        // Context A's merge, still working from its stale "duplicate is live" snapshot, must
        // fail rather than succeed and re-tombstone the duplicate onto survivorA.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mergerA.MergeAsync(survivorA.Id, [duplicate.Id], actor.Id));

        await using var verifyDb = fixture.NewContext();
        Assert.Equal(survivorB.Id, (await verifyDb.MsProfiles.SingleAsync(p => p.Id == duplicate.Id)).MergedIntoId);
        Assert.True(await verifyDb.Relations.AnyAsync(
            r => r.MsProfileId == survivorB.Id && r.ColumbusUserId == user.Id));
        Assert.False(await verifyDb.Relations.AnyAsync(r => r.MsProfileId == survivorA.Id));
    }

    [Fact]
    public async Task History_already_recorded_against_the_duplicate_is_left_where_it_is()
    {
        // relation_history is append-only (FR-09): merging must not rewrite the past, so the
        // duplicate's pre-merge rows stay on the duplicate's id, reachable through the tombstone.
        await using var db = fixture.NewContext();
        var survivor = await Seed.ProfileAsync(db);
        var duplicate = await Seed.ProfileAsync(db);
        var user = await Seed.UserAsync(db);
        var writer = new RelationWriter(db);
        await writer.UpsertAsync(user.Id, duplicate.Id, 2, "knows them", user.Id);

        await new MsProfileMerger(db, writer).MergeAsync(survivor.Id, [duplicate.Id], user.Id);

        await using var verify = fixture.NewContext();
        Assert.True(await verify.RelationHistory.AnyAsync(h =>
            h.MsProfileId == duplicate.Id && h.ChangeType == RelationChangeType.Created && h.NewScore == 2));
    }
}

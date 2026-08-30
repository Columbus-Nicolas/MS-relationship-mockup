using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;
using MsRelationship.Api.Features.Relations;
using MsRelationship.Api.Features.Submissions;
using Xunit;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class SubmissionTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Submitting_does_not_touch_approved_relations()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));

        await service.SubmitAsync(user.Id, [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 3, "new")]);

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id));
        Assert.True(await db.Submissions.AnyAsync(s => s.Status == SubmissionStatus.Pending));
    }

    [Fact]
    public async Task Approving_writes_the_relation_and_links_the_history()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "positive")]);

        await service.ApproveAsync(submission.Id, admin.Id);

        var relation = await db.Relations.SingleAsync(r => r.ColumbusUserId == user.Id);
        Assert.Equal(2, relation.Score);
        Assert.True(await db.RelationHistory.AnyAsync(h => h.SubmissionId == submission.Id));
        Assert.Equal(SubmissionStatus.Approved,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    [Fact]
    public async Task Rejecting_leaves_the_approved_data_alone()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        await new RelationWriter(db).UpsertAsync(user.Id, profile.Id, 1, "as approved", user.Id);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, -3, "wrong")]);

        await service.RejectAsync(submission.Id, admin.Id);

        Assert.Equal(1, (await db.Relations.SingleAsync(r => r.ColumbusUserId == user.Id)).Score);
        Assert.Equal(SubmissionStatus.Rejected,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    [Fact]
    public async Task Approving_twice_throws_and_does_not_double_apply()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "positive")]);

        await service.ApproveAsync(submission.Id, admin.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(submission.Id, admin.Id));

        var historyCount = await db.RelationHistory.CountAsync(h => h.SubmissionId == submission.Id);
        Assert.Equal(1, historyCount);
    }

    [Fact]
    public async Task Approving_after_reject_throws()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "positive")]);

        await service.RejectAsync(submission.Id, admin.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(submission.Id, admin.Id));
        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id));
    }

    [Fact]
    public async Task A_failing_item_rolls_back_the_whole_approval()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var otherProfile = await Seed.ProfileAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        // Second item has an out-of-range score (violates ck_relations_score) so the
        // whole approval must roll back, including the first item's otherwise-valid write.
        var submission = await service.SubmitAsync(user.Id,
        [
            new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "ok"),
            new SubmissionDraft(otherProfile.Id, SubmissionAction.Upsert, 99, "bad")
        ]);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ApproveAsync(submission.Id, admin.Id));

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id));
        Assert.False(await db.RelationHistory.AnyAsync(h => h.SubmissionId == submission.Id));
        Assert.Equal(SubmissionStatus.Pending,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    [Fact]
    public async Task Approving_a_remove_item_deletes_the_relation_and_links_the_history()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        await new RelationWriter(db).UpsertAsync(user.Id, profile.Id, -1, "strained", user.Id);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Remove, null, null)]);

        await service.ApproveAsync(submission.Id, admin.Id);

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id));
        var history = await db.RelationHistory.SingleAsync(h => h.SubmissionId == submission.Id);
        Assert.Equal(RelationChangeType.Removed, history.ChangeType);
        Assert.Equal(SubmissionStatus.Approved,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    [Fact]
    public async Task Approving_a_remove_item_for_an_already_absent_relation_is_a_no_op_that_still_approves()
    {
        // No relation exists for this pair (e.g. it was removed independently, or another
        // pending submission already removed it) — RelationWriter.RemoveAsync is a silent
        // no-op in that case. This pins that as deliberate: no throw, no history row, and
        // the submission still reaches Approved (see the comment in
        // SubmissionService.ApproveAsync's Remove branch for why).
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Remove, null, null)]);

        await service.ApproveAsync(submission.Id, admin.Id);

        Assert.False(await db.Relations.AnyAsync(r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id));
        Assert.False(await db.RelationHistory.AnyAsync(h => h.SubmissionId == submission.Id));
        Assert.Equal(SubmissionStatus.Approved,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }

    /// <summary>
    /// Reproduces the approve-vs-reject race from the final review deterministically, with no
    /// threads or sleeps — mirroring
    /// <see cref="SuperAdminTransferTests.Two_concurrent_transfers_from_the_same_holder_do_not_both_succeed"/>.
    /// <see cref="Submission"/> carries no concurrency token, so before the fix
    /// <c>RejectAsync</c>'s guard was a plain read of the still-tracked, stale
    /// <c>Status == Pending</c> followed by an unconditional write: two admins both load the
    /// submission while it is still Pending, one approves and commits (relations written,
    /// history stamped with the submission id), and the other's reject — issued from its
    /// now-stale belief that the submission is still Pending — must fail instead of silently
    /// flipping an already-approved submission to Rejected while its changes stay live in
    /// <c>relations</c>.
    /// </summary>
    [Fact]
    public async Task Approve_then_reject_do_not_both_succeed()
    {
        await using var seedDb = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(seedDb);
        var adminA = await Seed.UserAsync(seedDb);
        var adminB = await Seed.UserAsync(seedDb);
        var submission = await new SubmissionService(seedDb, new RelationWriter(seedDb))
            .SubmitAsync(user.Id, [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "positive")]);

        await using var dbA = fixture.NewContext();
        await using var dbB = fixture.NewContext();

        // Both contexts load (and thus track) the submission at Status == Pending before
        // either one commits a decision.
        await dbA.Submissions.SingleAsync(s => s.Id == submission.Id);
        await dbB.Submissions.SingleAsync(s => s.Id == submission.Id);

        var serviceA = new SubmissionService(dbA, new RelationWriter(dbA));
        var serviceB = new SubmissionService(dbB, new RelationWriter(dbB));

        // Admin A approves and commits first.
        await serviceA.ApproveAsync(submission.Id, adminA.Id);

        // Admin B's reject, still working from its stale "still Pending" snapshot, must fail
        // rather than silently flip an already-approved submission to Rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serviceB.RejectAsync(submission.Id, adminB.Id));

        await using var verifyDb = fixture.NewContext();
        var stored = await verifyDb.Submissions.SingleAsync(s => s.Id == submission.Id);
        Assert.Equal(SubmissionStatus.Approved, stored.Status);
        Assert.Equal(adminA.Id, stored.DecidedByUserId);
        Assert.True(await verifyDb.Relations.AnyAsync(
            r => r.ColumbusUserId == user.Id && r.MsProfileId == profile.Id));
    }

    [Fact]
    public async Task Rejecting_after_approve_throws()
    {
        await using var db = fixture.NewContext();
        var (user, profile) = await Seed.PairAsync(db);
        var admin = await Seed.UserAsync(db);
        var service = new SubmissionService(db, new RelationWriter(db));
        var submission = await service.SubmitAsync(user.Id,
            [new SubmissionDraft(profile.Id, SubmissionAction.Upsert, 2, "positive")]);

        await service.ApproveAsync(submission.Id, admin.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RejectAsync(submission.Id, admin.Id));
        Assert.Equal(SubmissionStatus.Approved,
            (await db.Submissions.SingleAsync(s => s.Id == submission.Id)).Status);
    }
}

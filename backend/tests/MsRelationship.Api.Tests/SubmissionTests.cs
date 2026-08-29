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

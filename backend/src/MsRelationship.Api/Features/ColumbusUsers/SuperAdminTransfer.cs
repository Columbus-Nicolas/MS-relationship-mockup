using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

/// <summary>
/// Transfers the Super Admin role (R-01). There is exactly one Super Admin at all times.
///
/// <see cref="ColumbusUser"/> carries no concurrency token (unlike <see cref="Relation.UpdatedAt"/>
/// — see Task 7b, finding 2 — adding one here would be a schema change, which this task may not
/// make). Instead, both the demote and the promote are <em>conditional</em> updates whose WHERE
/// clause repeats the guard that must still hold at write time — `role = 'SuperAdmin'` for the
/// demote, `status = 'Active'` for the promote — and the affected-row count is checked
/// afterwards. Postgres re-evaluates an UPDATE's WHERE predicate against the row's latest
/// committed state once a blocked writer's lock is released, so if another transfer already
/// changed the row first, our predicate no longer matches, the update affects zero rows, and we
/// throw instead of silently succeeding alongside it. This closes the same race a concurrency
/// token closes for <see cref="Relation"/> — a business predicate standing in for a version
/// column — and is proven deterministically (no threads/sleeps) by
/// <see cref="SuperAdminTransferTests.Two_concurrent_transfers_from_the_same_holder_do_not_both_succeed"/>,
/// which mirrors <c>RelationWriterTests.Concurrent_updates_to_the_same_relation_are_caught</c>.
///
/// Both conditional updates run inside one transaction, and here that transaction is
/// load-bearing rather than defensive: <c>ExecuteUpdateAsync</c> bypasses the change tracker and
/// executes immediately as its own statement instead of waiting for a single <c>SaveChanges</c>
/// flush, so with two separate writes an explicit transaction is what makes them atomic. If the
/// promote's predicate fails to match after the demote has already gone through, the transaction
/// is disposed without a commit and the demote is rolled back with it — proven by
/// <see cref="SuperAdminTransferTests.A_failed_promote_rolls_back_the_demote"/>, which observes
/// the holder still holding the role after a rejected transfer.
///
/// Decisions made here (the brief was silent on all three):
///  - The outgoing Super Admin becomes <see cref="UserRole.Admin"/>, not <see cref="UserRole.Standard"/>.
///    They just held the most privileged role in the system; demoting them all the way down would
///    strip abilities (e.g. approving submissions) that an outgoing Super Admin plausibly still
///    needs, and "Admin" is the natural one-step-down landing spot.
///  - Transferring to an archived user is rejected: that would hand the most privileged role to
///    someone who has left the company. This is exactly the scenario <see cref="UserArchiver"/>'s
///    guard is written against — the two must stay consistent, since an archived user can never
///    be the Super Admin in the first place.
///  - Transferring to oneself (fromUserId == toUserId) is a no-op, not an error, checked only once
///    the caller is confirmed to genuinely be the current Super Admin (a non-holder self-calling
///    still throws, not silently succeeds). Because exactly one Super Admin can exist, "the target
///    already holds the role" is only reachable when the target *is* the current holder, so
///    self-transfer is the only case that needs handling.
/// </summary>
public class SuperAdminTransfer(AppDbContext db)
{
    public async Task TransferAsync(Guid fromUserId, Guid toUserId)
    {
        var holder = await db.ColumbusUsers.SingleAsync(u => u.Id == fromUserId);
        if (holder.Role != UserRole.SuperAdmin)
            throw new InvalidOperationException("Only the current Super Admin can transfer the role.");

        // Checked only after confirming holder is actually the Super Admin — an impostor
        // calling TransferAsync(self, self) must still throw, not return a silent success.
        if (fromUserId == toUserId) return;

        await using var tx = await db.Database.BeginTransactionAsync();

        // Conditional demote: the WHERE clause re-asserts "still Super Admin" at write time.
        var demoted = await db.ColumbusUsers
            .Where(u => u.Id == fromUserId && u.Role == UserRole.SuperAdmin)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Admin));
        Detach(fromUserId);
        if (demoted != 1)
            throw new InvalidOperationException("Only the current Super Admin can transfer the role.");

        // Conditional promote: the WHERE clause re-asserts "still active" at write time. This is
        // also the sole check rejecting an archived target — there is no separate pre-check to
        // go stale between reading it and writing it.
        var promoted = await db.ColumbusUsers
            .Where(u => u.Id == toUserId && u.Status == UserStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.SuperAdmin));
        Detach(toUserId);
        if (promoted != 1)
            throw new InvalidOperationException("An archived user cannot become Super Admin.");

        await tx.CommitAsync();
    }

    /// <summary>
    /// <c>ExecuteUpdateAsync</c> writes straight to the database and bypasses the change
    /// tracker, so any instance of this row already tracked by <c>db</c> — e.g. because a caller
    /// loaded it earlier in the same scope — would otherwise keep showing pre-transfer values
    /// for the rest of that scope. Detaching forces the next query for this id back to the
    /// database.
    /// </summary>
    private void Detach(Guid id)
    {
        var entry = db.ChangeTracker.Entries<ColumbusUser>().FirstOrDefault(e => e.Entity.Id == id);
        if (entry is not null) entry.State = EntityState.Detached;
    }
}

using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Features.ColumbusUsers;

/// <summary>
/// Transfers the Super Admin role (R-01). There is exactly one Super Admin at all times, so
/// demoting the outgoing holder and promoting the incoming one happen inside a single
/// transaction — a failure between the two writes must never leave zero or two Super Admins.
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
///  - Transferring to oneself (fromUserId == toUserId) is a no-op, not an error. Because exactly
///    one Super Admin can exist, "the target already holds the role" is only reachable when the
///    target *is* the current holder, so self-transfer is the only case that needs handling.
/// </summary>
public class SuperAdminTransfer(AppDbContext db)
{
    public async Task TransferAsync(Guid fromUserId, Guid toUserId)
    {
        if (fromUserId == toUserId) return;

        var holder = await db.ColumbusUsers.SingleAsync(u => u.Id == fromUserId);
        if (holder.Role != UserRole.SuperAdmin)
            throw new InvalidOperationException("Only the current Super Admin can transfer the role.");

        var target = await db.ColumbusUsers.SingleAsync(u => u.Id == toUserId);
        if (target.Status != UserStatus.Active)
            throw new InvalidOperationException("An archived user cannot become Super Admin.");

        await using var tx = await db.Database.BeginTransactionAsync();
        holder.Role = UserRole.Admin;
        target.Role = UserRole.SuperAdmin;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}

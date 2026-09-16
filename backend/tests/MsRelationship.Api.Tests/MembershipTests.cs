using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data.Entities;

namespace MsRelationship.Api.Tests;

[Collection("postgres")]
public class MembershipTests
{
    private readonly PostgresFixture _pg;
    public MembershipTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task A_person_in_domains_from_two_dashboards_appears_on_both()
    {
        await using var db = _pg.NewContext();
        var a = new Dashboard { Slug = "data-ai", Label = "Data & AI" };
        var bd = new Dashboard { Slug = "dynamics", Label = "Dynamics" };
        var da = new Domain { DashboardId = a.Id, Name = "Partner Leadership" };
        var dbn = new Domain { DashboardId = bd.Id, Name = "Partner & Alliance" };
        var nina = MsProfile.Create("Nina Due", "nina.due@ms.example", null);
        db.AddRange(a, bd, da, dbn, nina);
        db.MsProfileDomains.AddRange(
            new MsProfileDomain { MsProfileId = nina.Id, DomainId = da.Id },
            new MsProfileDomain { MsProfileId = nina.Id, DomainId = dbn.Id });
        await db.SaveChangesAsync();

        var boards = await db.MsProfileDomains
            .Where(x => x.MsProfileId == nina.Id)
            .Join(db.Domains, x => x.DomainId, d => d.Id, (x, d) => d.DashboardId)
            .Distinct().ToListAsync();

        Assert.Equal(2, boards.Count);
    }

    /// The "Unmarked" system domain (Task 14's seed data has exactly one) is
    /// dashboard-less by design: DashboardId is null. MsProfilesController's
    /// list projection joins domain to dashboard the same way this test does
    /// and must filter that null out rather than serialise it.
    [Fact]
    public async Task A_person_in_an_unmarked_domain_does_not_report_a_null_dashboard()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = _pg.NewContext();

        var board = new Dashboard { Slug = $"board-{tag}", Label = "Board" };
        var real = new Domain { DashboardId = board.Id, Name = $"Real {tag}" };
        var unmarked = new Domain { DashboardId = null, Name = $"Unmarked {tag}" };
        var person = MsProfile.Create($"Person {tag}", $"person-{tag}@ms.example", null);
        db.AddRange(board, real, unmarked, person);
        db.MsProfileDomains.AddRange(
            new MsProfileDomain { MsProfileId = person.Id, DomainId = real.Id },
            new MsProfileDomain { MsProfileId = person.Id, DomainId = unmarked.Id });
        await db.SaveChangesAsync();

        var dashboards = await db.MsProfileDomains
            .Where(x => x.MsProfileId == person.Id)
            .Join(db.Domains, x => x.DomainId, d => d.Id, (x, d) => d.DashboardId)
            .Where(d => d != null)
            .Distinct().ToListAsync();

        // One assertion does both jobs: no null survived the filter, and the
        // real membership is not also a casualty of it. A filter that dropped
        // everything would satisfy "contains no null" too, so checking only
        // that would let the test pass for the wrong reason.
        Assert.Equal(new List<Guid?> { board.Id }, dashboards);
    }

    [Fact]
    public async Task A_customer_is_a_record_so_it_can_be_queried_backwards()
    {
        await using var db = _pg.NewContext();
        var novo = new Customer { Name = "NOVO NORDISK" };
        var seller = MsProfile.Create("Jesper Pedersen", "jesper.p@ms.example", null);
        db.AddRange(novo, seller);
        db.MsProfileCustomers.Add(new MsProfileCustomer { MsProfileId = seller.Id, CustomerId = novo.Id });
        await db.SaveChangesAsync();

        var whoTouchesNovo = await db.MsProfileCustomers
            .Where(x => x.CustomerId == novo.Id)
            .Join(db.MsProfiles, x => x.MsProfileId, p => p.Id, (x, p) => p.Name)
            .ToListAsync();

        Assert.Equal(["Jesper Pedersen"], whoTouchesNovo);
    }

    [Fact]
    public async Task A_customer_type_defaults_to_unknown()
    {
        await using var db = _pg.NewContext();
        var c = new Customer { Name = "Carlsberg Group" };
        db.Customers.Add(c);
        await db.SaveChangesAsync();

        await using var verify = _pg.NewContext();
        Assert.Equal(CustomerType.Unknown, (await verify.Customers.FindAsync(c.Id))!.Type);
    }
}

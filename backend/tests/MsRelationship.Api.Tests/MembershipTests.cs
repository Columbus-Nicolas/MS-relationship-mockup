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

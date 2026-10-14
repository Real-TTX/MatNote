using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Tests;

/// <summary>The rules of the tables: audit columns, soft delete.</summary>
public class DataTests
{
    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task A_new_record_gets_its_create_and_update_dates()
    {
        using var db = NewDb();
        var before = DateTime.UtcNow.AddSeconds(-1);
        var tag = new Tag { Name = "x", OwnerId = 1 };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        Assert.True(tag.CreateDate >= before);
        Assert.Equal(tag.CreateDate, tag.UpdateDate);
        Assert.Equal(SoftEntity.Created, tag.UpdateState);
    }

    [Fact]
    public async Task A_change_moves_the_update_date_and_state_but_not_the_create_date()
    {
        using var db = NewDb();
        var tag = new Tag { Name = "x", OwnerId = 1 };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        var created = tag.CreateDate;
        await Task.Delay(20);
        tag.Name = "y";
        await db.SaveChangesAsync();
        Assert.Equal(created, tag.CreateDate);
        Assert.True(tag.UpdateDate > created);
        Assert.Equal(SoftEntity.Updated, tag.UpdateState);
    }

    [Fact]
    public async Task Remove_is_a_soft_delete_and_the_record_leaves_the_queries()
    {
        using var db = NewDb();
        var note = new Note { Title = "n", OwnerId = 1 };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        Assert.Empty(await db.Notes.ToListAsync());
        var kept = await db.Notes.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(SoftEntity.Deleted, kept.UpdateState);
    }

    [Fact]
    public async Task A_deleted_note_can_be_brought_back()
    {
        using var db = NewDb();
        var note = new Note { Title = "n", OwnerId = 1 };
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        db.Notes.Remove(note);
        await db.SaveChangesAsync();
        var gone = await db.Notes.IgnoreQueryFilters().SingleAsync();
        gone.UpdateState = SoftEntity.Updated;
        await db.SaveChangesAsync();
        Assert.Single(await db.Notes.ToListAsync());
    }

    [Fact]
    public async Task A_session_is_not_soft_deleted()
    {
        using var db = NewDb();
        var session = new UserSession { UserId = 1, ExpiresDate = DateTime.UtcNow.AddDays(1) };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        db.Sessions.Remove(session);
        await db.SaveChangesAsync();
        Assert.Empty(await db.Sessions.ToListAsync());
    }

    [Fact]
    public void Tables_are_pascal_case_and_the_key_is_id()
    {
        using var db = NewDb();
        var names = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToList();
        Assert.All(names, n => Assert.Matches("^[A-Z][A-Za-z]+$", n!));
        Assert.All(db.Model.GetEntityTypes(), e => Assert.Equal("Id", e.FindPrimaryKey()!.Properties.Single().Name));
        Assert.All(db.Model.GetEntityTypes(), e => Assert.Equal(typeof(long), e.FindPrimaryKey()!.Properties.Single().ClrType));
    }

    [Fact]
    public void Every_table_has_the_audit_columns()
    {
        using var db = NewDb();
        foreach (var e in db.Model.GetEntityTypes())
        {
            foreach (var column in new[] { "CreateDate", "CreateUserId", "UpdateDate", "UpdateUserId" })
            {
                Assert.True(e.FindProperty(column) is not null, $"{e.GetTableName()} has no {column}");
            }
        }
    }

    [Fact]
    public void Tables_that_are_soft_deleted_have_update_state_and_a_filter()
    {
        using var db = NewDb();
        foreach (var e in db.Model.GetEntityTypes().Where(e => e.FindProperty("UpdateState") is not null))
        {
            Assert.True(e.GetDeclaredQueryFilters().Any(), $"{e.GetTableName()} has no query filter");
        }
    }

    [Fact]
    public void A_table_with_a_secret_in_its_address_has_a_token()
    {
        using var db = NewDb();
        foreach (var table in new[] { "UserSession", "Share" })
        {
            Assert.NotNull(db.Model.GetEntityTypes().Single(e => e.GetTableName() == table).FindProperty("Token"));
        }
    }
}

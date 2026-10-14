using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Tests;

/// <summary>The privacy rules: who may see which note. Run on an in-memory database.</summary>
public class AccessTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }

    private sealed class Scene
    {
        public required AppDbContext Db;
        public User Alex = null!, Bea = null!, Admin = null!;
        public Folder Work = null!, Prompts = null!, Private = null!;
        public Note InPrompts = null!, InWork = null!, InPrivate = null!, Loose = null!;
    }

    private static async Task<Scene> SceneAsync()
    {
        var db = NewDb();
        var s = new Scene { Db = db };
        s.Alex = new User { Username = "alex", DisplayName = "Alex" };
        s.Bea = new User { Username = "bea", DisplayName = "Bea" };
        s.Admin = new User { Username = "root", DisplayName = "Root", Role = UserRole.Admin };
        db.Users.AddRange(s.Alex, s.Bea, s.Admin);
        await db.SaveChangesAsync();
        s.Work = new Folder { OwnerId = s.Alex.Id, Name = "Work" };
        s.Private = new Folder { OwnerId = s.Alex.Id, Name = "Private" };
        db.Folders.AddRange(s.Work, s.Private);
        await db.SaveChangesAsync();
        s.Prompts = new Folder { OwnerId = s.Alex.Id, Name = "Prompts", ParentId = s.Work.Id };
        db.Folders.Add(s.Prompts);
        await db.SaveChangesAsync();
        s.InPrompts = new Note { OwnerId = s.Alex.Id, FolderId = s.Prompts.Id, Title = "In prompts" };
        s.InWork = new Note { OwnerId = s.Alex.Id, FolderId = s.Work.Id, Title = "In work" };
        s.InPrivate = new Note { OwnerId = s.Alex.Id, FolderId = s.Private.Id, Title = "In private" };
        s.Loose = new Note { OwnerId = s.Alex.Id, Title = "Loose" };
        db.Notes.AddRange(s.InPrompts, s.InWork, s.InPrivate, s.Loose);
        await db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Nothing_is_shared_nothing_is_seen()
    {
        var s = await SceneAsync();
        var scope = await AccessService.LoadScopeAsync(s.Db, s.Bea.Id);
        Assert.Equal(Access.None, scope.For(s.InWork));
        Assert.Equal(Access.None, scope.For(s.Work));
    }

    [Fact]
    public async Task A_folder_share_covers_everything_below_it_with_the_given_permission()
    {
        var s = await SceneAsync();
        s.Db.Shares.Add(new Share { Kind = ShareKind.User, OwnerId = s.Alex.Id, FolderId = s.Work.Id, SharedWithUserId = s.Bea.Id, Permission = SharePermission.Edit });
        await s.Db.SaveChangesAsync();
        var scope = await AccessService.LoadScopeAsync(s.Db, s.Bea.Id);
        Assert.Equal(Access.Edit, scope.For(s.InWork));
        Assert.Equal(Access.Edit, scope.For(s.InPrompts));
        Assert.Equal(Access.Edit, scope.For(s.Prompts));
        Assert.Equal(Access.None, scope.For(s.InPrivate));
        Assert.Equal(Access.None, scope.For(s.Loose));
        Assert.Equal(Access.None, scope.For(s.Private));
    }

    [Fact]
    public async Task The_highest_permission_of_several_shares_wins()
    {
        var s = await SceneAsync();
        s.Db.Shares.Add(new Share { Kind = ShareKind.User, OwnerId = s.Alex.Id, FolderId = s.Work.Id, SharedWithUserId = s.Bea.Id, Permission = SharePermission.Read });
        s.Db.Shares.Add(new Share { Kind = ShareKind.User, OwnerId = s.Alex.Id, NoteId = s.InPrompts.Id, SharedWithUserId = s.Bea.Id, Permission = SharePermission.Edit });
        await s.Db.SaveChangesAsync();
        var scope = await AccessService.LoadScopeAsync(s.Db, s.Bea.Id);
        Assert.Equal(Access.Edit, scope.For(s.InPrompts));
        Assert.Equal(Access.Read, scope.For(s.InWork));
    }

    [Fact]
    public async Task A_single_note_share_covers_only_that_note()
    {
        var s = await SceneAsync();
        s.Db.Shares.Add(new Share { Kind = ShareKind.User, OwnerId = s.Alex.Id, NoteId = s.Loose.Id, SharedWithUserId = s.Bea.Id });
        await s.Db.SaveChangesAsync();
        var scope = await AccessService.LoadScopeAsync(s.Db, s.Bea.Id);
        Assert.Equal(Access.Read, scope.For(s.Loose));
        Assert.Equal(Access.None, scope.For(s.InWork));
    }

    [Fact]
    public async Task An_expired_share_gives_nothing()
    {
        var s = await SceneAsync();
        s.Db.Shares.Add(new Share { Kind = ShareKind.User, OwnerId = s.Alex.Id, NoteId = s.Loose.Id, SharedWithUserId = s.Bea.Id, ExpiresDate = DateTime.UtcNow.AddMinutes(-1) });
        await s.Db.SaveChangesAsync();
        Assert.Equal(Access.None, (await AccessService.LoadScopeAsync(s.Db, s.Bea.Id)).For(s.Loose));
    }

    [Fact]
    public async Task A_stopped_share_gives_nothing()
    {
        var s = await SceneAsync();
        var share = new Share { Kind = ShareKind.User, OwnerId = s.Alex.Id, NoteId = s.Loose.Id, SharedWithUserId = s.Bea.Id };
        s.Db.Shares.Add(share);
        await s.Db.SaveChangesAsync();
        s.Db.Shares.Remove(share);
        await s.Db.SaveChangesAsync();
        Assert.Equal(Access.None, (await AccessService.LoadScopeAsync(s.Db, s.Bea.Id)).For(s.Loose));
    }

    [Fact]
    public async Task An_administrator_has_no_access_to_the_notes_of_others()
    {
        var s = await SceneAsync();
        var scope = await AccessService.LoadScopeAsync(s.Db, s.Admin.Id);
        Assert.Equal(Access.None, scope.For(s.InWork));
        Assert.Equal(Access.None, scope.For(s.Loose));
    }

    [Fact]
    public async Task A_link_covers_its_note_or_its_folder_tree_and_nothing_else()
    {
        var s = await SceneAsync();
        var folderLink = new Share { Kind = ShareKind.Link, OwnerId = s.Alex.Id, FolderId = s.Work.Id };
        var noteLink = new Share { Kind = ShareKind.Link, OwnerId = s.Alex.Id, NoteId = s.Loose.Id };
        Assert.True(await AccessService.LinkCoversAsync(s.Db, folderLink, s.InWork));
        Assert.True(await AccessService.LinkCoversAsync(s.Db, folderLink, s.InPrompts));
        Assert.False(await AccessService.LinkCoversAsync(s.Db, folderLink, s.InPrivate));
        Assert.False(await AccessService.LinkCoversAsync(s.Db, folderLink, s.Loose));
        Assert.True(await AccessService.LinkCoversAsync(s.Db, noteLink, s.Loose));
        Assert.False(await AccessService.LinkCoversAsync(s.Db, noteLink, s.InWork));
    }

    [Fact]
    public void Folder_tree_helpers_walk_down_and_up()
    {
        FolderRef[] all = [new(1, null), new(2, 1), new(3, 2), new(4, 1), new(5, null)];
        Assert.Equal(new long[] { 1, 2, 3, 4 }, FolderTree.SelfAndDescendants(all, 1).Order());
        Assert.Equal(new long[] { 5 }, FolderTree.SelfAndDescendants(all, 5).Order());
        Assert.Equal(new long[] { 1, 2, 3 }, FolderTree.PathTo(all, 3));
    }

    [Fact]
    public void Folder_tree_survives_a_loop()
    {
        FolderRef[] all = [new(1, 2), new(2, 1)];
        Assert.Equal(2, FolderTree.SelfAndDescendants(all, 1).Count);
        Assert.True(FolderTree.PathTo(all, 1).Count <= 64);
    }
}

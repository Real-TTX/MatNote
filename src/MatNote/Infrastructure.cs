using System.Text.RegularExpressions;
using MatNote.Data;
using MatNote.Services;
using Microsoft.EntityFrameworkCore;

namespace MatNote;

/// <summary>The connection string of the database (the backup needs its parts for <c>pg_dump</c>).</summary>
public sealed record DatabaseInfo(string ConnectionString);

/// <summary>Docker <c>HEALTHCHECK</c> without curl: the app asks its own <c>/health</c> and answers with an exit code.</summary>
public static partial class HealthCheck
{
    [GeneratedRegex(@":(\d+)")]
    private static partial Regex PortRegex();

    public static async Task<int> RunAsync()
    {
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        var match = PortRegex().Match(urls ?? string.Empty);
        var port = match.Success ? match.Groups[1].Value : "2301";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync($"http://localhost:{port}/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}

public static class Database
{
    /// <summary>Creates and updates the tables. The database container may still be starting: try again for a while before giving up.</summary>
    public static async Task MigrateWithRetryAsync(WebApplication app)
    {
        const int attempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                app.Logger.LogInformation("The database is up to date.");
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                app.Logger.LogWarning("The database is not ready yet (attempt {Attempt}/{Attempts}): {Message}", attempt, attempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}

/// <summary>Whether the app has a user yet. Without one, every page leads to the setup assistant.</summary>
public sealed class SetupState
{
    private volatile int _state; // 0 unknown, 1 needs setup, 2 done

    public async Task<bool> NeedsSetupAsync(AppDbContext db)
    {
        if (_state == 2)
        {
            return false;
        }

        var any = await db.Users.IgnoreQueryFilters().AnyAsync();
        _state = any ? 2 : 1;
        return !any;
    }

    public void Done() => _state = 2;
}

/// <summary>On a fresh database the first visit goes to the setup assistant, which creates the first administrator.</summary>
public sealed class SetupRedirect(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SetupState state, AppDbContext db)
    {
        var path = context.Request.Path;
        var open = path.StartsWithSegments("/Account/Setup") || path.StartsWithSegments("/health") || path.StartsWithSegments("/Error")
            || path.StartsWithSegments("/matui") || path.StartsWithSegments("/css") || path.StartsWithSegments("/js") || path.StartsWithSegments("/img")
            || path.Value?.EndsWith(".ico", StringComparison.Ordinal) == true;
        if (!open && await state.NeedsSetupAsync(db))
        {
            context.Response.Redirect("/Account/Setup");
            return;
        }

        await next(context);
    }
}

/// <summary>Headers every response carries: no inline script or style (the toolkit needs none), nothing from another site, no guessing of types.</summary>
public sealed class SecurityHeaders(RequestDelegate next)
{
    private const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; " +
        "frame-src 'self'; object-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = Policy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return next(context);
    }
}

/// <summary>The housekeeping that runs by itself: old sessions go, and what has been in the trash longer than the setting is removed for good.</summary>
public sealed class MaintenanceService(IServiceScopeFactory scopes, ILogger<MaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The housekeeping failed; it tries again later.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<ConfigStore<AppConfig>>();
        var now = DateTime.UtcNow;

        var sessions = await db.Sessions.Where(s => s.IsRevoked || s.ExpiresDate < now.AddDays(-1)).ExecuteDeleteAsync(ct);
        var limit = now.AddDays(-Math.Max(1, config.Value.TrashDays));
        var notes = await db.Notes.IgnoreQueryFilters().Where(n => n.UpdateState == SoftEntity.Deleted && n.UpdateDate < limit).ExecuteDeleteAsync(ct);
        var shares = await db.Shares.IgnoreQueryFilters().Where(s => s.UpdateState == SoftEntity.Deleted && s.UpdateDate < limit).ExecuteDeleteAsync(ct);
        await db.NoteLinks.Where(l => !db.Notes.IgnoreQueryFilters().Any(n => n.Id == l.SourceNoteId) || !db.Notes.IgnoreQueryFilters().Any(n => n.Id == l.TargetNoteId)).ExecuteDeleteAsync(ct);

        // what was deleted with its owner or its folder goes too, once nothing refers to it any more (folders from the bottom up)
        var folders = 0;
        for (var pass = 0; pass < FolderService.MaxDepth + 2; pass++)
        {
            var removed = await db.Folders.IgnoreQueryFilters()
                .Where(f => f.UpdateState == SoftEntity.Deleted && f.UpdateDate < limit
                            && !db.Folders.IgnoreQueryFilters().Any(c => c.ParentId == f.Id) && !db.Notes.IgnoreQueryFilters().Any(n => n.FolderId == f.Id))
                .ExecuteDeleteAsync(ct);
            folders += removed;
            if (removed == 0)
            {
                break;
            }
        }

        var tags = await db.Tags.IgnoreQueryFilters().Where(t => t.UpdateState == SoftEntity.Deleted && t.UpdateDate < limit).ExecuteDeleteAsync(ct);
        var users = await db.Users.IgnoreQueryFilters()
            .Where(u => u.UpdateState == SoftEntity.Deleted && u.UpdateDate < limit
                        && !db.Notes.IgnoreQueryFilters().Any(n => n.OwnerId == u.Id) && !db.Folders.IgnoreQueryFilters().Any(f => f.OwnerId == u.Id))
            .ExecuteDeleteAsync(ct);
        logger.LogInformation("Housekeeping: {Sessions} old sessions, {Notes} notes, {Folders} folders, {Tags} tags, {Shares} shares and {Users} accounts removed.", sessions, notes, folders, tags, shares, users);
    }
}

/// <summary>
/// A text that is posted empty is an empty string, not <c>null</c> (the default of ASP.NET Core): the properties of the page models are not nullable, so the code never meets a null in a text.
/// Where "nothing" matters, the code asks <c>string.IsNullOrWhiteSpace</c>.
/// </summary>
public sealed class EmptyStringBinderProvider : Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinderProvider
{
    public Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder? GetBinder(Microsoft.AspNetCore.Mvc.ModelBinding.ModelBinderProviderContext context)
        => context.Metadata.ModelType == typeof(string) ? new EmptyStringBinder() : null;

    private sealed class EmptyStringBinder : Microsoft.AspNetCore.Mvc.ModelBinding.IModelBinder
    {
        public Task BindModelAsync(Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingContext bindingContext)
        {
            var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (result == Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult.None)
            {
                return Task.CompletedTask;
            }

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
            bindingContext.Result = Microsoft.AspNetCore.Mvc.ModelBinding.ModelBindingResult.Success(result.FirstValue ?? string.Empty);
            return Task.CompletedTask;
        }
    }
}

/// <summary>A signed-in person whose password was set by an administrator has to choose their own before anything else.</summary>
public sealed class ForcePasswordChange(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUser current)
    {
        var path = context.Request.Path;
        if (current.Snapshot is { MustChangePassword: true }
            && !path.StartsWithSegments("/Account") && !path.StartsWithSegments("/health")
            && !path.StartsWithSegments("/matui") && !path.StartsWithSegments("/css") && !path.StartsWithSegments("/js") && !path.StartsWithSegments("/img"))
        {
            context.Response.Redirect("/Account");
            return;
        }

        await next(context);
    }
}

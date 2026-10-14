using System.Reflection;
using MatNote;
using MatNote.Data;
using MatNote.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--healthcheck"))
{
    return await HealthCheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

// ---- files: one data directory (the volume), JSON for the configuration ----
var paths = new AppPaths(AppPaths.ResolveDataDir());
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(new ConfigStore<AppConfig>(paths, "app.json"));
builder.Services.AddSingleton<Clock>();

// ---- the database (PostgreSQL) ----
var connection = Environment.GetEnvironmentVariable("MATNOTE_DB")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "Host=postgres;Port=5432;Database=matnote;Username=matnote;Password=matnote";
builder.Services.AddSingleton(new DatabaseInfo(connection));
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));

// The keys that protect the cookie and the secrets are in the volume: a session survives a restart of the container.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(paths.Keys))
    .SetApplicationName("MatNote");

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<TotpService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddSingleton<SetupState>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.AddMatNoteFeatures();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "matnote.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.IsEssential = true;
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = SessionService.ValidateAsync;
        o.Events.OnRedirectToLogin = context =>
        {
            // a script that asks for JSON gets a status, not the sign-in page
            if (context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }

            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Verify");
    options.Conventions.AllowAnonymousToPage("/Account/Setup");
    options.Conventions.AllowAnonymousToPage("/Account/Register");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToFolder("/Public");
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
}).AddMvcOptions(o =>
{
    o.ModelBinderProviders.Insert(0, new EmptyStringBinderProvider());
    // the page models have non-nullable texts that a form may leave empty: what is required is checked by the code, with a message that says what to do
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

// files up to the limit of the setting are accepted; the limit itself is checked where a file is read
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 600L * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 600L * 1024 * 1024);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // behind a reverse proxy (Caddy, nginx, Traefik) the address and the scheme of the visitor are in the headers
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

await Database.MigrateWithRetryAsync(app);

app.UseForwardedHeaders();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
}

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseMiddleware<SecurityHeaders>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // files are versioned with ?v= (asp-append-version): they can be kept for a long time
        if (ctx.Context.Request.Query.ContainsKey("v"))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
    },
});
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<SetupRedirect>();
app.UseMiddleware<ForcePasswordChange>();
app.UseAuthorization();

app.MapGet("/health", async (AppDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok("ok") : Results.StatusCode(503)).AllowAnonymous();
app.MapRazorPages();

app.Run();
return 0;

/// <summary>The build: <c>0.1.42-20261014</c> (release), <c>nightly-42-20261014</c> (development) or <c>local-20261014</c>.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "local";
}

public partial class Program;

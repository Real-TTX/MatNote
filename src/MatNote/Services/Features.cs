using MatNote.Services.Export;

namespace MatNote.Services;

/// <summary>Registers what the features of the app need (notes, sharing, export, backup). Each feature adds one line here.</summary>
public static class FeatureRegistration
{
    public static IServiceCollection AddMatNoteFeatures(this IServiceCollection services)
    {
        services.AddSingleton<Assets>();
        services.AddScoped<MenuService>();
        services.AddScoped<UserService>();
        services.AddScoped<FolderService>();
        services.AddScoped<NoteService>();
        services.AddScoped<ShareService>();
        services.AddScoped<PublicShareAccess>();
        services.AddExport();
        services.AddBackup();
        return services;
    }
}

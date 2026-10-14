using MatNote.Services.Backup;

namespace MatNote.Services;

/// <summary>Everything the backup subsystem needs: its configuration file, the targets, the service, the scheduler and the restore.</summary>
public static class BackupRegistration
{
    public static IServiceCollection AddBackup(this IServiceCollection services)
    {
        services.AddSingleton(sp => new ConfigStore<BackupConfig>(sp.GetRequiredService<AppPaths>(), "backup.json"));
        services.AddSingleton<BackupTargetFactory>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<RestoreService>();
        services.AddScoped<BackupStatusService>();
        services.AddHostedService<BackupScheduler>();
        return services;
    }
}

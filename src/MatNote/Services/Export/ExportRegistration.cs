namespace MatNote.Services.Export;

public static class ExportRegistration
{
    /// <summary>Registers the exporter, the preview and what they read from the database.</summary>
    public static IServiceCollection AddExport(this IServiceCollection services)
    {
        services.AddScoped<INoteFileStore, DbNoteFileStore>();
        services.AddScoped<INoteExporter, NoteExporter>();
        services.AddScoped<INotePreview, NotePreview>();
        return services;
    }
}

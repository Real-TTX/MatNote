using MatNote.Services;
using MatNote.Services.Backup;
using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Admin.Backups.Targets;

/// <summary>
/// Create or change a target. "Test connection" tries what is in the form without saving it. The password of an SMB target is never sent back to the page: an empty field keeps
/// the stored one. Delete is here, not in the list, and is refused while a schedule still uses the target.
/// </summary>
public sealed class EditModel(ConfigStore<BackupConfig> config, BackupTargetFactory targets, SecretProtector secrets) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public string Id { get; set; } = string.Empty;

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public BackupTargetType Type { get; set; } = BackupTargetType.Folder;

    [BindProperty] public string Path { get; set; } = "/backups";

    [BindProperty] public string Host { get; set; } = string.Empty;

    [BindProperty] public int Port { get; set; } = 445;

    [BindProperty] public string Share { get; set; } = string.Empty;

    [BindProperty] public string SubFolder { get; set; } = string.Empty;

    [BindProperty] public string Username { get; set; } = string.Empty;

    [BindProperty] public string Domain { get; set; } = string.Empty;

    /// <summary>Only what was typed into the field: the stored password is never put into it.</summary>
    [BindProperty] public string Password { get; set; } = string.Empty;

    public bool IsNew => Id.Length == 0;

    public bool HasStoredPassword { get; private set; }

    public int ScheduleCount { get; private set; }

    /// <summary>The answer of "Test connection": <c>null</c> when it was not asked for.</summary>
    public bool? TestOk { get; private set; }

    public string TestMessage { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        if (!IsNew)
        {
            var target = Find();
            if (target is null)
            {
                return NotFound();
            }

            Name = target.Name;
            Type = target.Type;
            Path = target.Path;
            Host = target.Host;
            Port = target.Port;
            Share = target.Share;
            SubFolder = target.SubFolder;
            Username = target.Username;
            Domain = target.Domain;
        }

        Prepare();
        return Page();
    }

    public IActionResult OnPost()
    {
        var existing = IsNew ? null : Find();
        if (!IsNew && existing is null)
        {
            return NotFound();
        }

        Name = Name.Trim();
        if (Name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), "Please enter a name.");
        }
        else if (Name.Length > 120)
        {
            ModelState.AddModelError(nameof(Name), "The name is too long (120 characters at most).");
        }
        else if (config.Value.Targets.Any(t => t.Id != Id && string.Equals(t.Name, Name, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(Name), "There is a target with this name already.");
        }

        ValidateConnection();
        if (!ModelState.IsValid)
        {
            Prepare(existing);
            return Page();
        }

        var savedId = Id;
        config.Update(c =>
        {
            var target = IsNew ? new BackupTarget() : c.Targets.First(t => t.Id == Id);
            if (IsNew)
            {
                c.Targets.Add(target);
            }

            savedId = target.Id;
            target.Name = Name;
            target.Type = Type;
            if (Type == BackupTargetType.Folder)
            {
                target.Path = Path.Trim();
                target.Host = target.Share = target.SubFolder = target.Username = target.Domain = target.Password = string.Empty;
                target.Port = 445;
            }
            else
            {
                target.Host = Host.Trim();
                target.Port = Port;
                target.Share = Share.Trim();
                target.SubFolder = string.Join('/', SubFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                target.Username = Username.Trim();
                target.Domain = Domain.Trim();
                if (Password.Length > 0)
                {
                    target.Password = targets.Protect(Password);
                }
            }
        });

        Flash($"Target “{Name}” is saved.");
        return RedirectToPage("Index");
    }

    /// <summary>Tries the connection with what is in the form (nothing is saved). The stored password is used when the field is empty.</summary>
    public async Task<IActionResult> OnPostTestAsync()
    {
        var existing = IsNew ? null : Find();
        ValidateConnection();
        if (ModelState.IsValid)
        {
            var probe = new BackupTarget { Type = Type, Path = Path.Trim(), Host = Host.Trim(), Port = Port, Share = Share.Trim(), SubFolder = SubFolder, Username = Username.Trim(), Domain = Domain.Trim() };
            var password = Password;
            if (Type == BackupTargetType.Smb && password.Length == 0 && existing is { HasPassword: true })
            {
                password = secrets.Unprotect(existing.Password) ?? string.Empty;
            }

            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                limit.CancelAfter(TimeSpan.FromSeconds(90));
                TestMessage = await targets.Create(probe, password).TestConnectionAsync(limit.Token);
                TestOk = true;
            }
            catch (BackupTargetException ex)
            {
                TestMessage = ex.Message;
                TestOk = false;
            }
            catch (OperationCanceledException)
            {
                TestMessage = "There was no answer in time.";
                TestOk = false;
            }
        }

        Prepare(existing);
        return Page();
    }

    public IActionResult OnPostDelete()
    {
        var target = Find();
        if (target is null)
        {
            return NotFound();
        }

        var users = config.Value.Schedules.Where(s => s.TargetId == target.Id).Select(s => s.Name).ToList();
        if (users.Count > 0)
        {
            ModelState.AddModelError(string.Empty, $"This target cannot be deleted: {(users.Count == 1 ? "the schedule" : "the schedules")} {string.Join(", ", users.Select(u => "“" + u + "”"))} {(users.Count == 1 ? "uses" : "use")} it. Give {(users.Count == 1 ? "it" : "them")} another target first, or delete {(users.Count == 1 ? "it" : "them")}.");
            Name = target.Name;
            Prepare(target);
            return Page();
        }

        config.Update(c => c.Targets.RemoveAll(t => t.Id == target.Id));
        Flash($"Target “{target.Name}” is deleted. The backups in it are still there.");
        return RedirectToPage("Index");
    }

    private BackupTarget? Find() => config.Value.Targets.FirstOrDefault(t => t.Id == Id);

    private void ValidateConnection()
    {
        if (Type == BackupTargetType.Folder)
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                ModelState.AddModelError(nameof(Path), "Please enter the folder.");
            }
            else if (!System.IO.Path.IsPathRooted(Path.Trim()))
            {
                ModelState.AddModelError(nameof(Path), "The folder is a full path, for example /backups.");
            }

            return;
        }

        // a port that is not a number is refused by the model binder with a technical message: say it in words
        var portIsNoNumber = ModelState.TryGetValue(nameof(Port), out var entry) && entry.Errors.Count > 0;
        if (portIsNoNumber)
        {
            entry!.Errors.Clear();
            ModelState.AddModelError(nameof(Port), "The port is a number between 1 and 65535 (SMB uses 445).");
        }

        if (SmbTarget.Validate(new SmbSettings(Host.Trim(), portIsNoNumber ? 445 : Port, Share.Trim(), SubFolder, Username, Domain, string.Empty)) is { } problem)
        {
            ModelState.AddModelError(problem.Field, problem.Message);
        }
    }

    private void Prepare(BackupTarget? existing = null)
    {
        existing ??= IsNew ? null : Find();
        HasStoredPassword = existing?.HasPassword == true;
        ScheduleCount = IsNew ? 0 : config.Value.Schedules.Count(s => s.TargetId == Id);
        ViewData["Title"] = IsNew ? "New target" : "Edit target";
        Crumbs(("Administration", null), ("Backups", "/Admin/Backups"), ("Targets", "/Admin/Backups/Targets"), (IsNew ? "New target" : Name, null));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MatNote.Services;

/// <summary>
/// One JSON file in <c>config</c> (JSON is for configuration; the logic of the app is in the database). The file is created with the defaults of
/// <typeparamref name="T"/> on the first start, read once, and every change is written at once (to a temporary file, then moved over the old one).
/// </summary>
public sealed class ConfigStore<T> where T : class, new()
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private T _value;

    public ConfigStore(AppPaths paths, string fileName)
    {
        _path = Path.Combine(paths.Config, fileName);
        _value = Read() ?? new T();
        if (!File.Exists(_path))
        {
            Write(_value);
        }
    }

    /// <summary>The current value. Treat it as read-only: change it with <see cref="Update"/>.</summary>
    public T Value
    {
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }
    }

    /// <summary>Changes the value and saves it. The change is made on a copy, so a reader never sees a half-changed value.</summary>
    public void Update(Action<T> change)
    {
        lock (_gate)
        {
            var copy = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(_value, Json), Json) ?? new T();
            change(copy);
            Write(copy);
            _value = copy;
        }
    }

    private T? Read()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(_path), Json) : null;
        }
        catch (JsonException)
        {
            // a file that cannot be read is kept (renamed) and replaced by the defaults, so that the app starts and nothing is lost
            File.Move(_path, _path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), overwrite: true);
            return null;
        }
    }

    private void Write(T value)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, _path, overwrite: true);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusLock;

internal enum EndAction
{
    Unlock,
    LockWindows,
    KeepBlocked,
}

/// <summary>Parent preferences, stored in %AppData%\FocusLock\settings.json. Only a salted hash of the PIN is kept.</summary>
internal sealed class Settings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusLock");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }
    public string? LastProcessName { get; set; }
    public int Minutes { get; set; } = 60;
    public EndAction EndAction { get; set; } = EndAction.Unlock;
    public bool WatchOnly { get; set; }

    [JsonIgnore]
    public bool HasPin => PinHash != null && PinSalt != null;

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void SetPin(string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        PinSalt = Convert.ToBase64String(salt);
        PinHash = Convert.ToBase64String(Hash(pin, salt));
    }

    public bool CheckPin(string pin) =>
        HasPin && CryptographicOperations.FixedTimeEquals(
            Hash(pin, Convert.FromBase64String(PinSalt!)), Convert.FromBase64String(PinHash!));

    private static byte[] Hash(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
}

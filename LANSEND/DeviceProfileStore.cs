using System.Text.Json;

namespace LANSEND;

internal sealed class DeviceProfileStore
{
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LANSEND",
        "devices.json");

    public List<DeviceProfile> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<DeviceProfile>();
            }

            var json = File.ReadAllText(_filePath);
            var profiles = JsonSerializer.Deserialize<List<DeviceProfile>>(json, AppConstants.JsonOptions)
                ?? new List<DeviceProfile>();

            return profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.IpAddress))
                .Select(Normalize)
                .GroupBy(profile => profile.IpAddress, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch (IOException)
        {
            return new List<DeviceProfile>();
        }
        catch (JsonException)
        {
            return new List<DeviceProfile>();
        }
    }

    public void Save(IEnumerable<DeviceProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var normalized = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.IpAddress))
            .Select(Normalize)
            .ToList();
        var json = JsonSerializer.Serialize(normalized, AppConstants.JsonOptions);
        var temporaryPath = _filePath + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, true);
    }

    private static DeviceProfile Normalize(DeviceProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(profile.ShareName))
        {
            profile.ShareName = AppConstants.DefaultShareName;
        }

        if (string.IsNullOrWhiteSpace(profile.DeviceName))
        {
            profile.DeviceName = profile.IpAddress;
        }

        return profile;
    }
}

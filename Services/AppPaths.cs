namespace CeroLicenseManager.Services;

public sealed class AppPaths
{
    public string DataRoot { get; }
    public string DatabasePath => Path.Combine(DataRoot, "license-manager.db");
    public string KeysPath => Path.Combine(DataRoot, "keys");
    public string ExportsPath => Path.Combine(DataRoot, "exports");
    public string DataProtectionPath => Path.Combine(DataRoot, "data-protection");

    private AppPaths(string dataRoot) => DataRoot = dataRoot;

    public static AppPaths Create(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["CERO_LICENSE_DATA_ROOT"];
        var defaultParent = Environment.GetFolderPath(OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(defaultParent, "CeroLicenseManager")
            : configured;
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("CERO_LICENSE_DATA_ROOT debe ser una ruta absoluta.");

        var paths = new AppPaths(Path.GetFullPath(root));
        var legacyDatabase = Path.Combine(environment.ContentRootPath, "license-manager.db");
        if (string.IsNullOrWhiteSpace(configured)
            && !File.Exists(paths.DatabasePath)
            && File.Exists(legacyDatabase)
            && !Path.GetFullPath(legacyDatabase).Equals(paths.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Se detectaron datos de una versión anterior junto al ejecutable. Migre la base y las llaves a la carpeta persistente antes de iniciar.");

        foreach (var directory in new[] { paths.DataRoot, paths.KeysPath, paths.ExportsPath, paths.DataProtectionPath })
            Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var directory in new[] { paths.DataRoot, paths.KeysPath, paths.ExportsPath, paths.DataProtectionPath })
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return paths;
    }
}

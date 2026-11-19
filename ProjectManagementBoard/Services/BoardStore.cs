using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ProjectManagementBoard.Models;

namespace ProjectManagementBoard.Services
{
    /// <summary>Settings (settings.json under %AppData%\ProjectManagementBoard) and JSON snapshots used by copy/paste.</summary>
    public static class BoardStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectManagementBoard");

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };

        public static string SerializeTool(ToolModel tool) => JsonSerializer.Serialize(tool, Options);

        public static ToolModel? DeserializeTool(string json) => JsonSerializer.Deserialize<ToolModel>(json, Options);

        public static string Serialize(IEnumerable<ToolModel> tools) => JsonSerializer.Serialize(tools, Options);

        public static string SerializeGroups(IEnumerable<GroupModel> groups) => JsonSerializer.Serialize(groups, Options);

        /// <summary>Writes through a temp file so a failed save never leaves a half-written project.</summary>
        public static void SaveProject(string path, ProjectFile file)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
            File.Move(temp, path, overwrite: true);
        }

        public static ProjectFile LoadProject(string path) =>
            JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("빈 파일");

        /// <summary>Writes one management tool on its own (*.pm&lt;tool&gt;), through a temp file like the project.</summary>
        public static void SaveToolFile(string path, ToolFileData file)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
            File.Move(temp, path, overwrite: true);
        }

        public static ToolFileData LoadToolFile(string path)
        {
            var file = JsonSerializer.Deserialize<ToolFileData>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("빈 파일");
            if (string.IsNullOrWhiteSpace(file.ToolId)) throw new InvalidDataException("관리 툴 파일이 아니에요");
            return file;
        }

        private static string SettingsPath => Path.Combine(Folder, "settings.json");

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options) ?? new();
            }
            catch (Exception) { /* corrupt settings: start from defaults */ }
            return new AppSettings();
        }

        public static bool TrySaveSettings(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var temp = SettingsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
                File.Move(temp, SettingsPath, overwrite: true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

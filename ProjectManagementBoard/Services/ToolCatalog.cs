using System.IO;
using System.Reflection;
using ProjectManagementBoard.Contracts;

namespace ProjectManagementBoard.Services
{
    /// <summary>
    /// Finds tool DLLs in <c>Plugins\</c> (next to the exe) and exposes the <see cref="IBoardTool"/> factories they contain.
    /// Assemblies are loaded into the default context (not a collectible one) so XAML pack URIs inside a tool keep working;
    /// the price is that changing a tool DLL needs a board restart.
    /// </summary>
    public sealed class ToolCatalog
    {
        public static string PluginsFolder => Path.Combine(AppContext.BaseDirectory, "Plugins");

        private readonly Dictionary<string, IBoardTool> _tools = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every tool found, ordered by display name.</summary>
        public IReadOnlyList<IBoardTool> Tools => _tools.Values.OrderBy(t => t.DisplayName, StringComparer.CurrentCulture).ToList();

        /// <summary>One line per DLL/type that could not be loaded (shown to the user, never fatal).</summary>
        public List<string> Errors { get; } = new();

        public IBoardTool? Find(string id) => _tools.GetValueOrDefault(id);

        /// <summary>
        /// Extension of a tool's individually saved files: what the tool declares (<see cref="IBoardToolFileExtension"/>, e.g.
        /// ".pmmo"), otherwise ".pm" + the first two letters of its id. The extension only sorts files in the dialogs;
        /// loading still checks the tool id stored inside the file.
        /// </summary>
        public string FileExtensionFor(string toolId)
        {
            if (Find(toolId) is IBoardToolFileExtension { FileExtension: { } declared } && NormalizeExtension(declared) is { } ext)
                return ext;
            var letters = new string(toolId.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(2).ToArray());
            return ".pm" + (letters.Length > 0 ? letters : "x");
        }

        /// <summary>".pmmo" / "pmmo" → ".pmmo"; null when it is not 1–8 letters or digits.</summary>
        private static string? NormalizeExtension(string ext)
        {
            var body = ext.Trim().TrimStart('.').ToLowerInvariant();
            return body.Length is >= 1 and <= 8 && body.All(char.IsLetterOrDigit) ? "." + body : null;
        }

        /// <summary>Reports tools whose file extensions clash (their files could not be told apart by extension).</summary>
        private void CheckExtensions()
        {
            foreach (var clash in _tools.Keys.GroupBy(FileExtensionFor).Where(g => g.Count() > 1))
                Errors.Add($"관리 툴 파일 확장자 ‘{clash.Key}’가 겹쳐요: {string.Join(", ", clash)}");
        }

        public static ToolCatalog Load()
        {
            var catalog = new ToolCatalog();
            try
            {
                Directory.CreateDirectory(PluginsFolder);
                foreach (var dll in Directory.EnumerateFiles(PluginsFolder, "*.dll", SearchOption.AllDirectories))
                    catalog.LoadAssembly(dll);
            }
            catch (Exception ex)
            {
                catalog.Errors.Add($"Plugins 폴더를 읽지 못했어요: {ex.Message}");
            }
            catalog.CheckExtensions();
            return catalog;
        }

        private void LoadAssembly(string path)
        {
            // The contract (and any other assembly the board itself ships) must come from the board, never from a plugin copy:
            // a second copy would make IBoardTool a different type and nothing would match.
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Equals(typeof(IBoardTool).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase)) return;

            Type[] types;
            try
            {
                types = Assembly.LoadFrom(path).GetExportedTypes();
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException or FileNotFoundException)
            {
                // Not a managed assembly, or a dependency of a tool (e.g. a NuGet DLL) that failed to load on its own: skip it.
                if (ex is not BadImageFormatException) Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                return;
            }

            foreach (var type in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBoardTool).IsAssignableFrom(t)))
            {
                try
                {
                    if (Activator.CreateInstance(type) is not IBoardTool tool) continue;
                    if (string.IsNullOrWhiteSpace(tool.Id))
                        Errors.Add($"{type.FullName}: Id가 비어 있어요");
                    else if (!_tools.TryAdd(tool.Id, tool))
                        Errors.Add($"{type.FullName}: Id ‘{tool.Id}’가 다른 툴과 겹쳐요");
                }
                catch (Exception ex)
                {
                    Errors.Add($"{type.FullName}: {ex.GetBaseException().Message}");
                }
            }
        }
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ProjectManagementBoard.Models
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    /// <summary>A labelled box drawn behind the tools. Membership is geometric (see <see cref="Contains(ToolModel)"/>).</summary>
    public class GroupModel : ObservableObject
    {
        public const string DefaultColor = "#9EC5FF";

        public const double DefaultTitleFontSize = 22;

        private string _title = "";
        private double _titleFontSize = DefaultTitleFontSize;
        private string _color = DefaultColor;
        private double _x, _y, _width = 760, _height = 520;

        public string Title { get => _title; set => SetField(ref _title, value ?? ""); }
        public double TitleFontSize { get => _titleFontSize; set => SetField(ref _titleFontSize, value > 0 ? value : DefaultTitleFontSize); }
        public string Color { get => _color; set => SetField(ref _color, value ?? DefaultColor); }
        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Width { get => _width; set => SetField(ref _width, value); }
        public double Height { get => _height; set => SetField(ref _height, value); }

        public bool Contains(ToolModel tool) => ContainsPoint(tool.X + tool.Width / 2, tool.Y + tool.Height / 2);

        public bool Contains(GroupModel child) =>
            child != this && child.Area < Area && ContainsPoint(child._x + child._width / 2, child._y + child._height / 2);

        [JsonIgnore]
        public double Area => _width * _height;

        public bool ContainsPoint(double x, double y) =>
            x >= _x && x <= _x + _width && y >= _y && y <= _y + _height;
    }

    public class AppSettings
    {
        public const string DefaultBackgroundColor = "#1F3864";

        public string BackgroundColor { get; set; } = DefaultBackgroundColor;

        /// <summary>The project file that was open last; reopened at startup.</summary>
        public string? LastFilePath { get; set; }

        /// <summary>Left panel (MENU / PROJECTS) and top bar (+ 새 관리 툴 ...) visibility, restored at startup.</summary>
        public bool LeftPanelOpen { get; set; } = true;
        public bool TopBarOpen { get; set; } = true;

        /// <summary>true = 맞춤이동 (positions snap to a grid), false = 자유이동.</summary>
        public bool SnapMove { get; set; }
    }

    /// <summary>What a project file (*.pmb, JSON) holds: every card (with its tool id and the tool's own state), the groups and the background.</summary>
    public class ProjectFile
    {
        public const string Extension = ".pmb";

        public int Version { get; set; } = 1;
        public List<ToolModel> Tools { get; set; } = new();
        public List<GroupModel> Groups { get; set; } = new();
        public string? BackgroundColor { get; set; }
    }

    /// <summary>
    /// One management tool on the board: a title above a rounded body. The body is the area where a WinForms / WPF
    /// program will be docked later.
    /// </summary>
    public class ToolModel : ObservableObject
    {
        public const double DefaultWidth = 420;
        public const double DefaultHeight = 300;
        public const string DefaultCardColor = "#4472C4";
        public const double DefaultTitleFontSize = 21;

        private string _title = "";
        private double _titleFontSize = DefaultTitleFontSize;
        private double _x, _y;
        private double _width = DefaultWidth;
        private double _height = DefaultHeight;
        private string _cardColor = DefaultCardColor;
        private string _toolId = "";
        private string _state = "";

        /// <summary>Id of the <c>IBoardTool</c> docked in this card; "" = an empty card.</summary>
        public string ToolId { get => _toolId; set => SetField(ref _toolId, value ?? ""); }

        /// <summary>The tool's own saved data (<c>IBoardToolInstance.SaveState</c>), refreshed by the card before copy/save.</summary>
        public string State { get => _state; set => SetField(ref _state, value ?? ""); }

        private string _toolFile = "";

        /// <summary>
        /// The individual tool file (*.pm&lt;tool&gt;, e.g. .pmmemo) this card was loaded from or last saved to; "" = none. The card's save button writes
        /// back to it. The whole board is still kept in the project file; this only feeds "불러오기" when adding a tool.
        /// </summary>
        public string ToolFile { get => _toolFile; set => SetField(ref _toolFile, value ?? ""); }

        public string Title { get => _title; set => SetField(ref _title, value ?? ""); }
        public double TitleFontSize { get => _titleFontSize; set => SetField(ref _titleFontSize, value > 0 ? value : DefaultTitleFontSize); }
        public string CardColor { get => _cardColor; set => SetField(ref _cardColor, value ?? DefaultCardColor); }
        public double X { get => _x; set => SetField(ref _x, value); }
        public double Y { get => _y; set => SetField(ref _y, value); }
        public double Width { get => _width; set => SetField(ref _width, value); }
        public double Height { get => _height; set => SetField(ref _height, value); }

        public static ToolModel CreateBlank() => new();
    }

    /// <summary>
    /// One management tool saved on its own (JSON; the extension is per tool, see <c>ToolCatalog.FileExtensionFor</c>), so it can be loaded into another board via
    /// + 새 관리 툴 → (tool) → 불러오기. Holds the tool's data and how its card looked; no position (it lands where it is added).
    /// </summary>
    public class ToolFileData
    {
        public int Version { get; set; } = 1;
        /// <summary>Which tool the data belongs to (<c>IBoardTool.Id</c>); loading checks it against the chosen tool.</summary>
        public string ToolId { get; set; } = "";
        public string Title { get; set; } = "";
        public string CardColor { get; set; } = ToolModel.DefaultCardColor;
        public double TitleFontSize { get; set; } = ToolModel.DefaultTitleFontSize;
        public double Width { get; set; } = ToolModel.DefaultWidth;
        public double Height { get; set; } = ToolModel.DefaultHeight;
        /// <summary>The tool's own data (<c>IBoardToolInstance.SaveState</c>).</summary>
        public string State { get; set; } = "";

        public static ToolFileData From(ToolModel card) => new()
        {
            ToolId = card.ToolId, Title = card.Title, CardColor = card.CardColor, TitleFontSize = card.TitleFontSize,
            Width = card.Width, Height = card.Height, State = card.State,
        };
    }
}

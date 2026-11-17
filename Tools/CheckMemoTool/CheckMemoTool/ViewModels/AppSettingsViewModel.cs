using System;
using CheckMemoTool.Models;

namespace CheckMemoTool.ViewModels
{
    // Per-card display settings: font size only (the floating window's opacity does not apply inside a card).
    public class AppSettingsViewModel : ObservableObject
    {
        private double _fontSize = FontLimits.DefaultFontSize;

        public event EventHandler Changed;

        // font size (pt) 10 ~ 24
        public double FontSize
        {
            get { return _fontSize; }
            set
            {
                double v = Math.Round(Math.Max(FontLimits.MinFontSize, Math.Min(FontLimits.MaxFontSize, value)));
                if (SetField(ref _fontSize, v))
                {
                    EventHandler handler = Changed;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
        }

        public void IncreaseFont() { FontSize = _fontSize + 1; }
        public void DecreaseFont() { FontSize = _fontSize - 1; }
        public void ResetFont() { FontSize = FontLimits.DefaultFontSize; }
    }
}

using MudBlazor;

namespace Dhadgar.UI.Shared.Services;

public static class ThemeService
{
    public static MudTheme CreateMeridianTheme()
    {
        return new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#6366f1",
                Secondary = "#06b6d4",
                Background = "#111827",
                Surface = "#1f2937",
                DrawerBackground = "#111827",
                AppbarBackground = "#111827",
                TextPrimary = "#ffffff",
                TextSecondary = "#d1d5db",
                Divider = "#4b5563",
                LinesDefault = "#4b5563",
                ActionDefault = "#d1d5db"
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "12px"
            }
        };
    }
}

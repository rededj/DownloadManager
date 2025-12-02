using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using MaterialSkin;
using MaterialSkin.Controls;

namespace DownloadManager
{
    public static class ThemeManager
    {
        private const string RegistryPath = @"Software\DownloadManager";
        private const string ThemeKey = "UserTheme";
        private static MaterialSkinManager _skinManager = MaterialSkinManager.Instance;

        public static void Initialize(Form form)
        {
            _skinManager.AddFormToManage((MaterialForm)form);

            string savedTheme = GetSavedTheme();

            if (savedTheme == "Light")
            {
                ApplyLightThemeInternal();
            }
            else if (savedTheme == "Dark")
            {
                ApplyDarkThemeInternal();
            }
            else
            {
                bool isSystemDark = DetectSystemTheme();
                if (isSystemDark)
                    ApplyDarkThemeInternal();
                else
                    ApplyLightThemeInternal();
            }
        }

        public static void ApplyLightTheme()
        {
            ApplyLightThemeInternal();
            SaveTheme("Light");
        }

        public static void ApplyDarkTheme()
        {
            ApplyDarkThemeInternal();
            SaveTheme("Dark");
        }

        private static void ApplyLightThemeInternal()
        {
            _skinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            _skinManager.ColorScheme = new ColorScheme(
                Primary.Blue400, Primary.Blue500, Primary.LightBlue200,
                Accent.LightBlue400, TextShade.WHITE);
        }

        private static void ApplyDarkThemeInternal()
        {
            _skinManager.Theme = MaterialSkinManager.Themes.DARK;
            _skinManager.ColorScheme = new ColorScheme(
                Primary.Blue600, Primary.Blue700, Primary.LightBlue300,
                Accent.Blue400, TextShade.WHITE);
        }

        public static void ToggleTheme()
        {
            if (_skinManager.Theme == MaterialSkinManager.Themes.LIGHT)
                ApplyDarkTheme();
            else
                ApplyLightTheme();
        }

        private static string GetSavedTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    return key?.GetValue(ThemeKey) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void SaveTheme(string themeName)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key?.SetValue(ThemeKey, themeName, RegistryValueKind.String);
                }
            }
            catch{}
        }

        private static bool DetectSystemTheme()
        {
            try
            {
                return DetectThemeViaRegistry() || DetectThemeViaSystemColors();
            }
            catch
            {
                return false;
            }
        }

        private static bool DetectThemeViaRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") is int value)
                    {
                        return value == 0; 
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool DetectThemeViaSystemColors()
        {
            var backColor = SystemColors.Window;
            double brightness = (0.299 * backColor.R + 0.587 * backColor.G + 0.114 * backColor.B) / 255.0;
            return brightness < 0.5;
        }
    }
}
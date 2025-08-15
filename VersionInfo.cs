using System.Reflection;

namespace PowerfulWizard
{
    public static class VersionInfo
    {
        /// <summary>
        /// Gets the current application version from assembly information
        /// </summary>
        public static string Version
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v2.0.0";
            }
        }

        /// <summary>
        /// Gets the application title with version
        /// </summary>
        public static string AppTitle => $"Powerful Wizard {Version}";

        /// <summary>
        /// Gets the settings window title with version
        /// </summary>
        public static string SettingsTitle => $"Settings | Powerful Wizard {Version}";
    }
}

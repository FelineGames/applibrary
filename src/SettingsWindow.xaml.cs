using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AppLibrary
{
    public sealed partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            this.InitializeComponent();

            // Load saved display text title
            CustomTitleBox.Text = AppStorage.GetString("CustomHeaderTitle", "");

            // Load saved OLED Dark Mode state
            bool isDark = AppStorage.GetBool("OledDarkMode", false);
            DarkModeToggle.IsOn = isDark;

            // Load saved state for the Import prompt checkbox
            bool hidePrompt = AppStorage.GetBool("HideImportSuccessDialog", false);
            ShowImportPromptCheckBox.IsChecked = !hidePrompt;

            // Apply current theme to SettingsWindow immediately on open
            ApplyThemeToSettingsWindow(isDark);
        }

        private void ApplyThemeToSettingsWindow(bool isDark)
        {
            this.SettingsRoot.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            this.SettingsRoot.Background = isDark ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(Colors.Transparent);
        }

        private void SettingsSave_Click(object sender, RoutedEventArgs e)
        {
            // Save custom header text
            string customText = CustomTitleBox.Text?.Trim() ?? "";
            AppStorage.SetString("CustomHeaderTitle", customText);

            // Save OLED Dark Mode setting
            bool isDark = DarkModeToggle.IsOn;
            AppStorage.SetBool("OledDarkMode", isDark);

            // Save Import Dialog prompt setting
            bool showPrompt = ShowImportPromptCheckBox.IsChecked == true;
            AppStorage.SetBool("HideImportSuccessDialog", !showPrompt);

            // Instantly update header title and OLED theme on MainPage
            if (App.MainWindow?.Content is Frame frame && frame.Content is MainPage mainPage)
            {
                mainPage.LoadAppHeaderTitleAndTheme();
            }

            // Apply theme to Settings Window
            ApplyThemeToSettingsWindow(isDark);
        }
    }
}
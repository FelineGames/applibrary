using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace AppLibrary
{
    public class GameApp : INotifyPropertyChanged
    {
        private string _name = "";
        private string _author = "";
        private string _exePath = "";
        private string _iconPath = "";
        private string _bannerPath = "";
        private bool _isPinned;
        private bool _isFavourite;
        private bool _isSelected;
        private Visibility _checkBoxVisibility = Visibility.Collapsed;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }
        public string Author { get => _author; set { _author = value; OnPropertyChanged(nameof(Author)); } }
        public string ExePath { get => _exePath; set { _exePath = value; OnPropertyChanged(nameof(ExePath)); } }
        
        public string IconPath
        {
            get => _iconPath;
            set
            {
                _iconPath = value;
                OnPropertyChanged(nameof(IconPath));
                OnPropertyChanged(nameof(IconImageSource));
            }
        }

        public string BannerPath
        {
            get => _bannerPath;
            set
            {
                _bannerPath = value;
                OnPropertyChanged(nameof(BannerPath));
                OnPropertyChanged(nameof(BannerImageSource));
            }
        }

        public bool IsPinned { get => _isPinned; set { _isPinned = value; OnPropertyChanged(nameof(IsPinned)); } }
        public bool IsFavourite { get => _isFavourite; set { _isFavourite = value; OnPropertyChanged(nameof(IsFavourite)); } }

        [JsonIgnore]
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }

        [JsonIgnore]
        public Visibility CheckBoxVisibility { get => _checkBoxVisibility; set { _checkBoxVisibility = value; OnPropertyChanged(nameof(CheckBoxVisibility)); } }

        // Safe ImageSource helpers so null/empty/invalid paths never crash XAML BitmapImage bindings
        [JsonIgnore]
        public ImageSource IconImageSource => CreateSafeImageSource(_iconPath);

        [JsonIgnore]
        public ImageSource BannerImageSource => CreateSafeImageSource(_bannerPath);

        private static ImageSource CreateSafeImageSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                return new BitmapImage(new Uri(path, UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed partial class MainPage : Page
    {
        public ObservableCollection<GameApp> Apps { get; set; } = new ObservableCollection<GameApp>();
        public ObservableCollection<GameApp> DisplayedApps { get; set; } = new ObservableCollection<GameApp>();

        private GameApp _appBeingEdited = null;
        private string _stagedIconSourcePath = null;
        private string _stagedBannerSourcePath = null;

        private string _currentTabTag = "all_apps";
        private bool _isSelectionActive = false;
        private bool _isLoadingData = false;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        private const string DATA_FILE_NAME = "library_data.json";

        // Common crash handler and helper binaries to ignore when scanning game directories
        private static readonly string[] IgnoredBinaryKeywords = new[]
        {
            "unitycrashhandler",
            "crashreportclient",
            "crashhandler",
            "unins000",
            "uninstall",
            "vc_redist",
            "dxsetup",
            "dotnet",
            "easyanticheat_setup",
            "beservice"
        };

        public MainPage()
        {
            this.InitializeComponent();

            Apps.CollectionChanged += (s, e) =>
            {
                if (!_isLoadingData)
                {
                    _ = SaveLibraryDataAsync();
                    ApplyFilter();
                }
            };

            this.Loaded += async (s, e) =>
            {
                LoadAppHeaderTitleAndTheme();
                await LoadLibraryDataAsync();
            };
        }

        public void LoadAppHeaderTitleAndTheme()
        {
            string title = AppStorage.GetString("CustomHeaderTitle", "App Library");
            AppHeaderTitle.Text = !string.IsNullOrWhiteSpace(title) ? title : "App Library";

            bool isDark = AppStorage.GetBool("OledDarkMode", false);
            this.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            RootGrid.Background = isDark ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(Colors.Transparent);

            if (App.MainWindow?.Content is FrameworkElement mainRoot)
            {
                mainRoot.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            }
        }

        // --- CROSS-PLATFORM WINDOW HANDLE HELPER --- //

        private void InitializePickerWithWindow(object picker)
        {
#if WINDOWS
            if (App.MainWindow != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
#endif
        }

        // --- INTERNAL STORAGE --- //

        private async Task LoadLibraryDataAsync()
        {
            _isLoadingData = true;
            try
            {
                string dataFilePath = Path.Combine(AppStorage.BaseFolderPath, DATA_FILE_NAME);
                if (File.Exists(dataFilePath))
                {
                    string json = await File.ReadAllTextAsync(dataFilePath);
                    var loadedApps = JsonSerializer.Deserialize<ObservableCollection<GameApp>>(json);
                    if (loadedApps != null)
                    {
                        Apps.Clear();
                        foreach (var app in loadedApps)
                        {
                            app.CheckBoxVisibility = _isSelectionActive ? Visibility.Visible : Visibility.Collapsed;
                            app.PropertyChanged += App_PropertyChanged;
                            Apps.Add(app);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading library data: {ex.Message}");
            }
            finally
            {
                _isLoadingData = false;
            }
            ApplyFilter();
        }

        private async Task SaveLibraryDataAsync()
        {
            if (_isLoadingData) return;

            await _saveLock.WaitAsync();
            try
            {
                string dataFilePath = Path.Combine(AppStorage.BaseFolderPath, DATA_FILE_NAME);
                string json = JsonSerializer.Serialize(Apps, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dataFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving library data: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private void App_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(GameApp.IsSelected) &&
                e.PropertyName != nameof(GameApp.CheckBoxVisibility) &&
                e.PropertyName != nameof(GameApp.IconImageSource) &&
                e.PropertyName != nameof(GameApp.BannerImageSource))
            {
                _ = SaveLibraryDataAsync();
                ApplyFilter();
            }
        }

        // --- FILTER & TAB NAVIGATION --- //

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            ApplyFilter();
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Activate();
                return;
            }

            if (args.InvokedItemContainer is NavigationViewItem item)
            {
                _currentTabTag = item.Tag?.ToString() ?? "all_apps";
                SwitchToTab(_currentTabTag);
            }
        }

        private void SwitchToTab(string tag)
        {
            AppsGridView.Visibility = Visibility.Collapsed;
            EditorGridView.Visibility = Visibility.Collapsed;
            EmptyStateGrid.Visibility = Visibility.Collapsed;
            AboutGrid.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility = Visibility.Collapsed;
            HeaderActionsPanel.Visibility = Visibility.Visible;

            if (tag == "editor")
            {
                HeaderActionsPanel.Visibility = Visibility.Collapsed;
                EditorGridView.Visibility = Visibility.Visible;
            }
            else if (tag == "about")
            {
                HeaderActionsPanel.Visibility = Visibility.Collapsed;
                AboutGrid.Visibility = Visibility.Visible;
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";
            var filtered = Apps.AsEnumerable();

            if (_currentTabTag == "pins")
                filtered = filtered.Where(a => a.IsPinned);
            else if (_currentTabTag == "favourites")
                filtered = filtered.Where(a => a.IsFavourite);

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(a =>
                    (!string.IsNullOrEmpty(a.Name) && a.Name.ToLowerInvariant().Contains(query)) ||
                    (!string.IsNullOrEmpty(a.Author) && a.Author.ToLowerInvariant().Contains(query))
                );
            }

            DisplayedApps.Clear();
            foreach (var item in filtered)
            {
                DisplayedApps.Add(item);
            }

            bool hasItems = DisplayedApps.Count > 0;
            if (_currentTabTag == "all_apps" || _currentTabTag == "pins" || _currentTabTag == "favourites")
            {
                AppsGridView.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                EmptyStateGrid.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (_currentTabTag == "editor")
            {
                EditorGridView.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                EmptyStateGrid.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // --- PLAY & FOLDER BUTTONS (WINDOWS + LINUX) --- //

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is GameApp app && !string.IsNullOrEmpty(app.ExePath) && File.Exists(app.ExePath))
            {
                try
                {
                    string workingDir = Path.GetDirectoryName(app.ExePath) ?? "";

                    if (OperatingSystem.IsLinux() && app.ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "wine",
                            Arguments = $"\"{app.ExePath}\"",
                            UseShellExecute = false,
                            WorkingDirectory = workingDir
                        });
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = app.ExePath,
                            UseShellExecute = true,
                            WorkingDirectory = workingDir
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start executable: {ex.Message}");
                }
            }
        }

        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is GameApp app && !string.IsNullOrEmpty(app.ExePath))
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Process.Start("explorer.exe", $"/select,\"{app.ExePath}\"");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        string dir = Path.GetDirectoryName(app.ExePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "xdg-open",
                                Arguments = $"\"{dir}\"",
                                UseShellExecute = false
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open directory: {ex.Message}");
                }
            }
        }

        // --- EDITOR PANEL & STAGED ASSET PICKING --- //

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is GameApp app)
            {
                _appBeingEdited = app;
                _stagedIconSourcePath = null;
                _stagedBannerSourcePath = null;

                EditNameBox.Text = app.Name ?? "";
                EditAuthorBox.Text = app.Author ?? "";

                EditIconPreview.Source = app.IconImageSource;
                EditBannerPreview.Source = app.BannerImageSource;

                AppsGridView.Visibility = Visibility.Collapsed;
                EditorGridView.Visibility = Visibility.Collapsed;
                EmptyStateGrid.Visibility = Visibility.Collapsed;
                AboutGrid.Visibility = Visibility.Collapsed;
                HeaderActionsPanel.Visibility = Visibility.Collapsed;

                EditorPanel.Visibility = Visibility.Visible;
            }
        }

        private async void ChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_appBeingEdited == null) return;

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".ico");

            InitializePickerWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file != null && File.Exists(file.Path))
            {
                // Stage the path for preview; only commit on SaveEdit_Click
                _stagedIconSourcePath = file.Path;
                EditIconPreview.Source = new BitmapImage(new Uri(file.Path, UriKind.Absolute));
            }
        }

        private async void ChangeBanner_Click(object sender, RoutedEventArgs e)
        {
            if (_appBeingEdited == null) return;

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            InitializePickerWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file != null && File.Exists(file.Path))
            {
                // Stage the path for preview; only commit on SaveEdit_Click
                _stagedBannerSourcePath = file.Path;
                EditBannerPreview.Source = new BitmapImage(new Uri(file.Path, UriKind.Absolute));
            }
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_appBeingEdited != null)
            {
                _appBeingEdited.Name = EditNameBox.Text?.Trim() ?? "";
                _appBeingEdited.Author = EditAuthorBox.Text?.Trim() ?? "";

                if (!string.IsNullOrEmpty(_stagedIconSourcePath) && File.Exists(_stagedIconSourcePath))
                {
                    string iconsFolder = Path.Combine(AppStorage.BaseFolderPath, "Icons");
                    Directory.CreateDirectory(iconsFolder);
                    string ext = Path.GetExtension(_stagedIconSourcePath);
                    string destPath = Path.Combine(iconsFolder, $"{Guid.NewGuid()}{ext}");
                    File.Copy(_stagedIconSourcePath, destPath, overwrite: true);
                    _appBeingEdited.IconPath = destPath;
                }

                if (!string.IsNullOrEmpty(_stagedBannerSourcePath) && File.Exists(_stagedBannerSourcePath))
                {
                    string bannersFolder = Path.Combine(AppStorage.BaseFolderPath, "Banners");
                    Directory.CreateDirectory(bannersFolder);
                    string ext = Path.GetExtension(_stagedBannerSourcePath);
                    string destPath = Path.Combine(bannersFolder, $"{Guid.NewGuid()}{ext}");
                    File.Copy(_stagedBannerSourcePath, destPath, overwrite: true);
                    _appBeingEdited.BannerPath = destPath;
                }
            }
            CloseEditor();
        }

        private void DiscardEdit_Click(object sender, RoutedEventArgs e)
        {
            CloseEditor();
        }

        private void CloseEditor()
        {
            _appBeingEdited = null;
            _stagedIconSourcePath = null;
            _stagedBannerSourcePath = null;
            SwitchToTab(_currentTabTag);
        }

        // --- AUTOMATED IMPORT & METADATA EXTRACTION (WINDOWS + LINUX) --- //

        private async void NewButton_Click(object sender, RoutedEventArgs e) => await ProcessFolderPick(searchSubFolders: false);
        private async void ImportButton_Click(object sender, RoutedEventArgs e) => await ProcessFolderPick(searchSubFolders: true);

        private async Task ProcessFolderPick(bool searchSubFolders)
        {
            FolderPicker folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            InitializePickerWithWindow(folderPicker);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null && Directory.Exists(folder.Path))
            {
                try
                {
                    var enumOptions = new EnumerationOptions
                    {
                        RecurseSubdirectories = searchSubFolders,
                        IgnoreInaccessible = true
                    };

                    string[] discoveredFiles;

                    if (OperatingSystem.IsWindows())
                    {
                        discoveredFiles = Directory.EnumerateFiles(folder.Path, "*.exe", enumOptions)
                            .Where( path => !IsIgnoredHelperBinary(path))
                            .ToArray();
                    }
                    else
                    {
                        discoveredFiles = Directory.EnumerateFiles(folder.Path, "*", enumOptions)
                            .Where(IsExecutableGameFileOnLinux)
                            .ToArray();
                    }

                    int addedCount = 0;

                    if (discoveredFiles.Length > 0)
                    {
                        var existingPaths = new HashSet<string>(
                            Apps.Select(a => a.ExePath),
                            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
                        );

                        foreach (var exePath in discoveredFiles)
                        {
                            if (existingPaths.Contains(exePath))
                                continue;

                            string appName = Path.GetFileNameWithoutExtension(exePath);
                            string authorName = "Unknown Author";

                            try
                            {
                                var info = FileVersionInfo.GetVersionInfo(exePath);
                                if (!string.IsNullOrWhiteSpace(info.ProductName)) appName = info.ProductName;
                                else if (!string.IsNullOrWhiteSpace(info.FileDescription)) appName = info.FileDescription;

                                if (!string.IsNullOrWhiteSpace(info.CompanyName)) authorName = info.CompanyName;
                            }
                            catch { }

                            string extractedIconPath = ExtractAndSaveIcon(exePath);

                            var newApp = new GameApp
                            {
                                Name = appName,
                                Author = authorName,
                                ExePath = exePath,
                                IconPath = extractedIconPath ?? "",
                                IsPinned = false,
                                IsFavourite = false,
                                CheckBoxVisibility = _isSelectionActive ? Visibility.Visible : Visibility.Collapsed
                            };

                            newApp.PropertyChanged += App_PropertyChanged;
                            Apps.Add(newApp);
                            existingPaths.Add(exePath);
                            addedCount++;
                        }

                        if (addedCount > 0)
                        {
                            await ShowSuccessDialog();
                        }
                        else
                        {
                            await ShowErrorDialog("All executables in this folder are already in your library.");
                        }
                    }
                    else
                    {
                        await ShowErrorDialog("No valid executables were found in the selected folder.");
                    }
                }
                catch
                {
                    await ShowErrorDialog("Process failed due to an error, would you like to retry?");
                }
            }
        }

        private static bool IsIgnoredHelperBinary(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            return IgnoredBinaryKeywords.Any(keyword => fileName.Contains(keyword));
        }

        private static bool IsExecutableGameFileOnLinux(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string lowerName = fileName.ToLowerInvariant();

            // Ignore hidden files, shared libraries (.so / .so.1), and known crash handlers
            if (lowerName.StartsWith(".") || lowerName.EndsWith(".so") || lowerName.Contains(".so.") || IsIgnoredHelperBinary(filePath))
                return false;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".exe" || ext == ".appimage" || ext == ".sh" || ext == ".bin" || ext == ".x86_64" || ext == ".x86")
                return true;

            // For extensionless files, verify both +x execute bit and ELF magic header (0x7F 'E' 'L' 'F')
            if (string.IsNullOrEmpty(ext))
            {
                try
                {
                    if (OperatingSystem.IsLinux())
                    {
                        UnixFileMode mode = File.GetUnixFileMode(filePath);
                        bool hasExecBit = (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
                        if (!hasExecBit) return false;
                    }

                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length >= 4)
                    {
                        byte[] magic = new byte[4];
                        if (fs.Read(magic, 0, 4) == 4)
                        {
                            return magic[0] == 0x7F && magic[1] == (byte)'E' && magic[2] == (byte)'L' && magic[3] == (byte)'F';
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        private string ExtractAndSaveIcon(string exePath)
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using (var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (sysIcon != null)
                        {
                            string iconsFolder = Path.Combine(AppStorage.BaseFolderPath, "Icons");
                            Directory.CreateDirectory(iconsFolder);
                            string destPath = Path.Combine(iconsFolder, $"{Guid.NewGuid()}.png");

                            using (var bitmap = sysIcon.ToBitmap())
                            {
                                bitmap.Save(destPath, System.Drawing.Imaging.ImageFormat.Png);
                            }
                            return destPath;
                        }
                    }
                }
                catch { }
            }
#endif
            return null;
        }

        // --- SELECTION MODE & BULK DELETE --- //

        private void SelectSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
        {
            _isSelectionActive = !_isSelectionActive;
            Visibility vis = _isSelectionActive ? Visibility.Visible : Visibility.Collapsed;

            foreach (var app in Apps)
            {
                app.CheckBoxVisibility = vis;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSelectionActive)
            {
                _isSelectionActive = true;
                foreach (var app in Apps)
                {
                    app.CheckBoxVisibility = Visibility.Visible;
                }
            }

            foreach (var app in DisplayedApps)
            {
                app.IsSelected = true;
            }
        }

        private void DeleteSelection_Click(object sender, RoutedEventArgs e)
        {
            var selectedList = Apps.Where(a => a.IsSelected).ToList();
            foreach (var app in selectedList)
            {
                app.PropertyChanged -= App_PropertyChanged;
                Apps.Remove(app);
            }
        }

        // --- ABOUT LINKS (CROSS-PLATFORM) --- //

        private async void LicenseButton_Click(object sender, RoutedEventArgs e) =>
            await LaunchUrlSafeAsync("https://choosealicense.com/licenses/mit/");

        private async void SourceCodeButton_Click(object sender, RoutedEventArgs e) =>
            await LaunchUrlSafeAsync("https://github.com");

        private static async Task LaunchUrlSafeAsync(string url)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
            }
            catch { }
        }

        // --- DIALOGS --- //

        private async Task ShowSuccessDialog()
        {
            // Respect the saved "Do not show this again" setting
            if (AppStorage.GetBool("HideImportSuccessDialog", false))
                return;

            CheckBox doNotShowCheckBox = new CheckBox
            {
                Content = "Do not show this again",
                IsChecked = false
            };

            StackPanel contentPanel = new StackPanel();
            contentPanel.Children.Add(new TextBlock
            {
                Text = "Process was completed successfully, would you like to update their Metadata?",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });
            contentPanel.Children.Add(doNotShowCheckBox);

            ContentDialog successDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Content = contentPanel,
                PrimaryButtonText = "Open Editor",
                SecondaryButtonText = "No, thanks"
            };

            Style buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Colors.Blue)));
            buttonStyle.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Colors.White)));
            successDialog.PrimaryButtonStyle = buttonStyle;
            successDialog.SecondaryButtonStyle = buttonStyle;

            var result = await successDialog.ShowAsync();

            if (doNotShowCheckBox.IsChecked == true)
            {
                AppStorage.SetBool("HideImportSuccessDialog", true);
            }

            if (result == ContentDialogResult.Primary)
            {
                NavView.SelectedItem = NavView.MenuItems[3];
                _currentTabTag = "editor";
                SwitchToTab("editor");
            }
        }

        private async Task ShowErrorDialog(string message = "Process failed due to an error, would you like to retry?")
        {
            ContentDialog errorDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Yes",
                SecondaryButtonText = "No"
            };

            Style buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Colors.Blue)));
            buttonStyle.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Colors.White)));
            errorDialog.PrimaryButtonStyle = buttonStyle;
            errorDialog.SecondaryButtonStyle = buttonStyle;

            if (await errorDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ProcessFolderPick(searchSubFolders: true);
            }
        }
    }
}
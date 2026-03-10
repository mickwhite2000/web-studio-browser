using Microsoft.VisualBasic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;



namespace Web_Studio_Browser
{
    public partial class MainWindow : Window
    {
        // ============================================
        // Privacy / Ad Block
        // ============================================

        private enum PrivacyMode
        {
            Off,
            Balanced,
            Strict,
            TrackingOnly
        }

        private PrivacyMode _privacyMode = PrivacyMode.Balanced;

        private readonly HashSet<string> _blockedHosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _adBlockWhitelist = new(StringComparer.OrdinalIgnoreCase);
        private bool _adBlockEnabled = true;

        private readonly string _adBlockListPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "adblock_hosts.txt");

        private readonly string _whitelistPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "adblock_whitelist.txt");

        private readonly string _privacyPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "privacy_mode.txt");

        // ============================================
        // Themes (stubs kept minimal)
        // ============================================

        private enum AppTheme
        {
            Light,
            Dark,
            Glass,
            Modern,
            Classic
        }

        private int _themeIndex = (int)AppTheme.Classic;

        // ============================================
        // Pages / Layout
        // ============================================

        private readonly string[] _pageUrls = new string[4];
        private int _activePageIndex = 0;

        // ============================================
        // Home URL (user preferred)
        // ============================================

        private string _homeUrl = "https://www.google.com.au/";
        private readonly string _homeUrlPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "home_url.txt");

        // Guards to prevent re-entrant focus/activation + double WebView init
        private bool _isActivatingPane;
        private readonly System.Threading.SemaphoreSlim _webViewInitGate = new(1, 1);
        private bool _webViewInitDone;
        private int _layoutPageCount = 1;
        private readonly bool[] _pageLocked = new bool[4];

        // ============================================
        // Quick URLs
        // ============================================

        private readonly string[] _presetUrls = new string[20];
        private readonly int[] _studioSlotIndex = new int[4]; // per pane: last used quick-url index
        private readonly int[] _blockedTrackerCounts = new int[4];
        private readonly int[] _blockedAdCounts = new int[4];
        private readonly string[] _statusMessages = new string[4];
        private readonly bool[] _statusWarnings = new bool[4];
        private readonly bool[] _compatibilityModeActive = new bool[4];
        private readonly string[] _compatibilityModeLabels = new string[4];
        private readonly string _presetUrlsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "quick_urls.json");

        // ============================================
        // Address display state
        // ============================================

        private string _lastUserInput = "";
        private bool _lastNavigationWasSearch = false;

        private bool _suppressAddressUpdates = false;
        private readonly DispatcherTimer _addressEditTimer = new DispatcherTimer();

        // ============================================
        // Updates
        // ============================================

        private static readonly HttpClient _http = new HttpClient();
        private bool _isLatestUpdate = false;

        private bool _hideIpEnabled = false;

        private readonly string[] _privacyLocations =
        {
    "Australia",
    "New Zealand",
    "Singapore",
    "United States",
    "United Kingdom"
};

        private int _privacyLocationIndex = 0;

        // ============================================
        // DNS + Search Engine Settings
        // ============================================

        private enum DnsApplyMode
        {
            SystemWindows,   // do nothing special
            AppOnlySecureDns, // DoH flags for WebView2 only
            WindowsIpv4      // change adapter IPv4 DNS (admin)
        }

        private DnsApplyMode _dnsMode = DnsApplyMode.SystemWindows;

        private readonly string _dnsModePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "dns_mode.txt");

        private void LoadDnsMode()
        {
            try
            {
                if (!File.Exists(_dnsModePath)) return;
                var txt = (File.ReadAllText(_dnsModePath) ?? "").Trim();
                if (Enum.TryParse(txt, out DnsApplyMode mode))
                    _dnsMode = mode;
            }
            catch { }
        }

        private void SaveDnsMode()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dnsModePath)!);
                File.WriteAllText(_dnsModePath, _dnsMode.ToString());
            }
            catch { }
        }
        private enum DnsPreset
        {
            SystemDefault,
            Cloudflare,
            Quad9,
            OpenDNS,
            CleanBrowsing,
            AdGuard,
            Google
        }

        private enum SearchEngine
        {
            Mojeek,
            DuckDuckGo,
            Brave,
            Startpage,
            Qwant,
            Google
        }

        private DnsPreset _dnsPreset = DnsPreset.SystemDefault;
        private SearchEngine _searchEngine = SearchEngine.Google;

        private readonly string _dnsPresetPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "dns_preset.txt");

        private readonly string _searchEnginePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "search_engine.txt");

        private readonly string _sessionPath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Web Studio Browser", "session.json");

        private void LoadDnsPreset()
        {
            try
            {
                if (!File.Exists(_dnsPresetPath)) return;
                var txt = (File.ReadAllText(_dnsPresetPath) ?? "").Trim();
                if (Enum.TryParse(txt, out DnsPreset preset))
                    _dnsPreset = preset;
            }
            catch { }
        }

        private void SaveDnsPreset()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dnsPresetPath)!);
                File.WriteAllText(_dnsPresetPath, _dnsPreset.ToString());
            }
            catch { }
        }



        private void LoadSearchEngine()
        {
            try
            {
                if (!File.Exists(_searchEnginePath)) return;
                var txt = (File.ReadAllText(_searchEnginePath) ?? "").Trim();
                if (Enum.TryParse(txt, out SearchEngine eng))
                    _searchEngine = eng;
            }
            catch { }
        }

        private void SaveSearchEngine()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_searchEnginePath)!);
                File.WriteAllText(_searchEnginePath, _searchEngine.ToString());
            }
            catch { }
        }

        // ============================================
        // Web History (Session)
        // ============================================
        private readonly List<string> _history = new();

        // Shared WebView2 environment (prevents "already initialized with different environment" errors)
        private CoreWebView2Environment? _sharedWebViewEnv;

        // ============================================
        // Bookmarks
        // ============================================

        private readonly List<BookmarkItem> _bookmarks = new();
        private readonly string _bookmarksPath =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "WebStudioBrowser", "bookmarks.json");

        private sealed record BookmarkItem(string Title, string Url, DateTime AddedUtc);


        // 👇 INSERT HERE
        private sealed record AppSessionState(
            string?[] PageUrls,
            int ActivePageIndex,
            int LayoutPageCount
        );

        private void LoadBookmarks()
        {
            try
            {
                if (!File.Exists(_bookmarksPath)) return;

                var json = File.ReadAllText(_bookmarksPath);
                var items = System.Text.Json.JsonSerializer.Deserialize<List<BookmarkItem>>(json);
                _bookmarks.Clear();
                if (items != null) _bookmarks.AddRange(items);
            }
            catch { /* swallow or log */ }
        }

        private void SaveBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_bookmarksPath)!);

                var json = System.Text.Json.JsonSerializer.Serialize(
                    _bookmarks,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_bookmarksPath, json);
            }
            catch { /* swallow or log */ }
        }


        private enum PaneLifeState
        {
            Normal,
            Minimized,
            Closed
        }

        private readonly PaneLifeState[] _paneState = new PaneLifeState[4]
        {
    PaneLifeState.Normal, PaneLifeState.Normal, PaneLifeState.Normal, PaneLifeState.Normal
        };

        private sealed class LayoutSnapshot
        {
            public PaneLifeState[] PaneState { get; init; } = new PaneLifeState[4];
            public int ActivePane { get; init; }
            public bool WasMaximizedMode { get; init; }
            public int MaximizedPane { get; init; } = -1;
            public int LayoutPageCount { get; init; }
        }

        private LayoutSnapshot? _lastSnapshot = null;

        // “Maximise pane” mode
        private bool _isPaneMaximizedMode = false;
        private int _maximizedPane = -1;

        private List<int> VisiblePanes()
        {
            var list = new List<int>(4);
            for (int i = 0; i < 4; i++)
                if (_paneState[i] == PaneLifeState.Normal)
                    list.Add(i);
            return list;
        }

        private void SaveSnapshot()
        {
            _lastSnapshot = new LayoutSnapshot
            {
                PaneState = (PaneLifeState[])_paneState.Clone(),
                ActivePane = _activePageIndex,
                WasMaximizedMode = _isPaneMaximizedMode,
                MaximizedPane = _maximizedPane,
                LayoutPageCount = _layoutPageCount
            };
        }

        private void RestoreSnapshot()
        {
            if (_lastSnapshot == null) return;

            Array.Copy(_lastSnapshot.PaneState, _paneState, 4);
            _activePageIndex = _lastSnapshot.ActivePane;

            _isPaneMaximizedMode = _lastSnapshot.WasMaximizedMode;
            _maximizedPane = _lastSnapshot.MaximizedPane;

            _layoutPageCount = _lastSnapshot.LayoutPageCount;

            ApplyLayout();
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
            UpdateSecureSiteVisual();
        }

        // TODO: change to where YOU host the update manifest
        private const string UpdateManifestUrl = "https://lexandwhitestudios.com/webstudio/latest.json";

        // ============================================
        // Constructor (SINGLE)
        // ============================================

        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < _studioSlotIndex.Length; i++)
                _studioSlotIndex[i] = -1;

            HomeTopButton.MouseRightButtonUp += (_, __) =>

            {
                try
                {
                    string defaultUrl = _homeUrl;
                    var current = GetActivePaneRealUrl();
                    if (!string.IsNullOrWhiteSpace(current) && (current.StartsWith("http://") || current.StartsWith("https://")))
                        defaultUrl = current;

                    var url = Interaction.InputBox(
                        "Set Home URL (include https://)",
                        "Home URL",
                        defaultUrl ?? "");

                    url = (url ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(url)) return;

                    if (!url.Contains("://") && url.Contains("."))
                        url = "https://" + url;

                    _homeUrl = url;
                    SaveHomeUrl();

                    MessageBox.Show("Home URL saved.", "Home", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            };


            // default theme = your current look
            ApplyTheme(AppTheme.Classic);
            UpdateThemesButtonLabel(AppTheme.Classic);
            _themeIndex = (int)AppTheme.Classic;

            InitAddressBarTimers();
            UpdateSecurityStatus("Secure", false);

            // default page slots
            _pageUrls[0] = "lexwhite://newtab";
            _pageUrls[1] = "about:blank";
            _pageUrls[2] = "about:blank";
            _pageUrls[3] = "about:blank";

            Loaded += async (_, __) =>
            {
                // --- LIGHTWEIGHT SETTINGS LOAD (fast file reads only) ---
                LoadPresetUrls();
                BuildPresetButtonMenus();
                RefreshPresetButtonsUI();

                LoadHomeUrl();
                LoadWhitelist();
                LoadPrivacyMode();
                LoadDnsPreset();
                LoadSearchEngine();
                LoadDnsMode();
                bool sessionRestored = LoadSessionState();

                UpdateDnsModeUi();
                UpdatePrivacyButtonLabel();
                LoadBookmarks();

                // --- Layout baseline ---
                _paneState[0] = PaneLifeState.Normal;
                _paneState[1] = PaneLifeState.Minimized;
                _paneState[2] = PaneLifeState.Minimized;
                _paneState[3] = PaneLifeState.Minimized;

                _layoutPageCount = 1;
                _activePageIndex = 0;

                ApplyLayout();
                UpdatePageModeButtonVisuals();
                UpdatePageHostBorders();

                // 🚀 SHOW HOME IMMEDIATELY
                // Home ALWAYS loads in pane 0 at startup
                _activePageIndex = 0;
                _studioSlotIndex[_activePageIndex] = 0;
                NavigateTo(_homeUrl);
                UpdateSecureSiteVisual();

                // --- HEAVIER INIT AFTER FIRST PAINT ---
                // Warm WebView AFTER first paint, then navigate cleanly
                await Dispatcher.InvokeAsync(async () =>
                {
                    await EnsureWebViewAsync();

                    _activePageIndex = 0;
                    _studioSlotIndex[_activePageIndex] = 0;
                    NavigateTo(_homeUrl);

                    _ = EnsureAdBlockListAsync(); // background
                }, DispatcherPriority.ApplicationIdle);
            };

        }

        // ============================================
        // Window control buttons (Top left capsule)
        // ============================================

        private void WinMin_Click(object sender, RoutedEventArgs e) => MinimizeActivePane();

        private void WinMaxRestore_Click(object sender, RoutedEventArgs e) => MaximizeActivePane();

        private void WinClose_Click(object sender, RoutedEventArgs e) => CloseActivePane();

        // ============================================
        // Top bar buttons wired in XAML
        // ============================================

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsDropdown == null) return;
            SettingsDropdown.IsOpen = !SettingsDropdown.IsOpen;
        }

        private void DnsNotes_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "DNS presets (via DNS-over-HTTPS):\n\n" +
                "System Default:\n  Uses Windows/ISP DNS.\n\n" +
                "Cloudflare:\n  Fast public DNS, privacy-focused.\n\n" +
                "Quad9:\n  Blocks known malicious domains (security-focused).\n\n" +
                "OpenDNS:\n  Reliable public DNS with optional filtering.\n\n" +
                "CleanBrowsing:\n  Filters malicious/adult content (depends on profile).\n\n" +
                "AdGuard DNS:\n  Often blocks ads/trackers at DNS level.\n\n" +
                "Google:\n  Fast, widely available public DNS.\n\n" +
                "Note: DNS change applies to WebView2 networking. Restart may be required.",
                "DNS Notes",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void StatusIndicatorHost_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (StatusPopup == null) return;

            RefreshActivePaneStatusVisual();
            StatusPopup.IsOpen = !StatusPopup.IsOpen;
        }

        private void UpdateDnsModeUi()
        {
            if (DnsModeStatusText == null) return;

            DnsModeStatusText.Text = _dnsMode switch
            {
                DnsApplyMode.SystemWindows => "Mode: System (Windows)",
                DnsApplyMode.AppOnlySecureDns => "Mode: Secure DNS (App Only)",
                DnsApplyMode.WindowsIpv4 => "Mode: Apply to Windows (IPv4)",
                _ => "Mode: System (Windows)"
            };
        }

        private void DnsMode_System_Click(object sender, RoutedEventArgs e)
        {
            _dnsMode = DnsApplyMode.SystemWindows;
            SaveDnsMode();
            UpdateDnsModeUi();
            MessageBox.Show("DNS mode set to System (Windows).", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DnsMode_App_Click(object sender, RoutedEventArgs e)
        {
            _dnsMode = DnsApplyMode.AppOnlySecureDns;
            SaveDnsMode();
            UpdateDnsModeUi();
            MessageBox.Show("DNS mode set to Secure DNS (App Only).\n\nRestart the app to apply.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DnsMode_Windows_Click(object sender, RoutedEventArgs e)
        {
            _dnsMode = DnsApplyMode.WindowsIpv4;
            SaveDnsMode();
            UpdateDnsModeUi();

            MessageBox.Show(
                "Windows DNS mode enabled.\n\nWhen you choose a DNS preset, Web Studio will change IPv4 DNS for active adapters.\n(Admin required)",
                "DNS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void DnsRestore_Windows_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsRunningAsAdmin())
                {
                    MessageBox.Show("Run as Administrator to restore Windows DNS.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var backup = LoadWindowsDnsBackup();
                if (backup.Count == 0)
                {
                    MessageBox.Show("No DNS backup found yet.\n\nSet Windows DNS mode and apply a preset first.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                RestoreWindowsIpv4DnsFromBackup(backup);
                MessageBox.Show("Windows DNS restored.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed:\n{ex.Message}", "DNS", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void SearchNotes_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Search engines:\n\n" +
                "Mojeek:\n  Independent index (not Google/Bing).\n\n" +
                "DuckDuckGo:\n  Privacy-focused, strong !bang shortcuts.\n\n" +
                "Brave:\n  Uses Brave’s own index + privacy features.\n\n" +
                "Startpage:\n  Google results with privacy proxying.\n\n" +
                "Qwant:\n  Privacy-oriented European search.\n\n" +
                "Google:\n  Most comprehensive results, less privacy.\n",
                "Search Notes",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void SetSearchEngine(SearchEngine engine)
        {
            _searchEngine = engine;
            SaveSearchEngine();
            MessageBox.Show("Search engine saved.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Wire these to Settings menu buttons/items:
        private void Search_Mojeek_Click(object sender, RoutedEventArgs e) => SetSearchEngine(SearchEngine.Mojeek);
        private void Search_DuckDuckGo_Click(object sender, RoutedEventArgs e) => SetSearchEngine(SearchEngine.DuckDuckGo);
        private void Search_Brave_Click(object sender, RoutedEventArgs e) => SetSearchEngine(SearchEngine.Brave);
        private void Search_Startpage_Click(object sender, RoutedEventArgs e) => SetSearchEngine(SearchEngine.Startpage);
        private void Search_Qwant_Click(object sender, RoutedEventArgs e) => SetSearchEngine(SearchEngine.Qwant);
        private void Search_Google_Click(object sender, RoutedEventArgs e) => SetSearchEngine(SearchEngine.Google);

        private void SetDnsPreset(DnsPreset preset)
        {
            _dnsPreset = preset;
            SaveDnsPreset();

            try
            {
                if (_dnsMode == DnsApplyMode.WindowsIpv4)
                {
                    if (!IsRunningAsAdmin())
                    {
                        try
                        {
                            SaveSessionState();

                            var psi = new ProcessStartInfo
                            {
                                FileName = Process.GetCurrentProcess().MainModule!.FileName!,
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            Process.Start(psi);
                            Application.Current.Shutdown();
                        }
                        catch
                        {
                            MessageBox.Show("Elevation cancelled.", "DNS",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        return;
                    }

                    // Backup once per change (safe)
                    var backup = CaptureActiveAdaptersDns();
                    SaveWindowsDnsBackup(backup);

                    SetWindowsIpv4DnsOnActiveAdapters(preset);

                    MessageBox.Show("Windows IPv4 DNS updated.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_dnsMode == DnsApplyMode.AppOnlySecureDns)
                {
                    MessageBox.Show("App DNS preset saved.\n\nRestart the app to apply Secure DNS.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // SystemWindows
                MessageBox.Show("DNS preset saved.\n\nSystem mode does not override Windows DNS.", "DNS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DNS change failed:\n{ex.Message}", "DNS", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Wire these to buttons/menu items in Settings:
        private void Dns_SystemDefault_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.SystemDefault);
        private void Dns_Cloudflare_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.Cloudflare);
        private void Dns_Quad9_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.Quad9);
        private void Dns_OpenDns_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.OpenDNS);
        private void Dns_CleanBrowsing_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.CleanBrowsing);
        private void Dns_AdGuard_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.AdGuard);
        private void Dns_Google_Click(object sender, RoutedEventArgs e) => SetDnsPreset(DnsPreset.Google);

        private void PrivacyButton_Click(object sender, RoutedEventArgs e)
        {
            if (PrivacyDropdown == null) return;

            PrivacyDropdown.IsOpen = !PrivacyDropdown.IsOpen;

            if (PrivacyDropdown.IsOpen)
            {
                RefreshPrivacyNetworkVisuals();
                UpdatePrivacyDropdownVisuals();
            }
        }


        private void WebActionsButton_Click(object sender, RoutedEventArgs e)
        {
            WebActionsDropdown.IsOpen = true;
        }

        private void ThemesButton_Click(object sender, RoutedEventArgs e)
        {
            AppTheme current = (AppTheme)_themeIndex;
            AppTheme next = current switch
            {
                AppTheme.Classic => AppTheme.Light,
                AppTheme.Light => AppTheme.Dark,
                _ => AppTheme.Classic
            };

            _themeIndex = (int)next;
            ApplyTheme(next);
            UpdateThemesButtonLabel(next);
        }

        private void StudioButton_Click(object sender, RoutedEventArgs e)
        {
            // Cycle the ACTIVE pane through the 20 quick URLs
            int pane = _activePageIndex;

            // Start from last-used index for this pane
            int start = _studioSlotIndex[pane];
            int idx = start;

            for (int step = 0; step < _presetUrls.Length; step++)
            {
                idx = (idx + 1) % _presetUrls.Length;

                var url = _presetUrls[idx];
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _studioSlotIndex[pane] = idx;
                    NavigateTo(url);
                    return;
                }
            }
        

        // If we get here: no non-empty quick URLs exist
        MessageBox.Show(
     "No Quick URLs are set.\n\nOpen Settings and add at least one URL.",
     "Studio",
     MessageBoxButton.OK,
     MessageBoxImage.Information);

            return;
        }

        // ============================================
        // Web Actions
        // ============================================

        private void WebAction_ViewBookmarks_Click(object sender, RoutedEventArgs e)
        {
            if (_bookmarks.Count == 0)
            {
                MessageBox.Show("No bookmarks saved yet.", "Bookmarks",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var menu = new ContextMenu();

            foreach (var bm in _bookmarks.OrderByDescending(b => b.AddedUtc))
            {
                var item = new MenuItem
                {
                    Header = bm.Title,
                    ToolTip = bm.Url
                };

                // Navigate when clicking main item
                item.Click += (_, __) =>
                {
                    var wv = GetActiveWebView();
                    if (wv?.CoreWebView2 != null)
                        wv.CoreWebView2.Navigate(bm.Url);
                };

                // Delete submenu item
                var deleteItem = new MenuItem
                {
                    Header = "Delete"
                };

                deleteItem.Click += (_, __) =>
                {
                    _bookmarks.Remove(bm);
                    SaveBookmarks();
                };

                item.Items.Add(new Separator());
                item.Items.Add(deleteItem);

                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void WebAction_Print_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wv = GetActiveWebView();
                if (wv?.CoreWebView2 == null)
                {
                    MessageBox.Show("No active page to print.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Requirement: tell user if no printers installed
                if (!HasAnyPrintersInstalled())
                {
                    MessageBox.Show("No Printer Installed", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Use built-in WebView2 print UI (avoids System.Drawing + PrintSettings constructor issues)
                wv.CoreWebView2.ShowPrintUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Print failed:\n{ex.Message}", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }


        private void WebAction_ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            var wv = GetActiveWebView();
            if (wv?.CoreWebView2 == null) return;

            wv.ZoomFactor = Math.Min(3.0, wv.ZoomFactor + 0.10);
        }

        private void WebAction_ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            var wv = GetActiveWebView();
            if (wv?.CoreWebView2 == null) return;

            wv.ZoomFactor = Math.Max(0.25, wv.ZoomFactor - 0.10);
        }

        private void WebAction_ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            var wv = GetActiveWebView();
            if (wv?.CoreWebView2 == null) return;

            wv.ZoomFactor = 1.0;
        }

        private void WebAction_Downloads_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");

                if (!Directory.Exists(downloads))
                    Directory.CreateDirectory(downloads);

                Process.Start(new ProcessStartInfo("explorer.exe", downloads) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open Downloads:\n{ex.Message}", "Downloads", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void WebAction_Bookmark_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wv = GetActiveWebView();
                if (wv?.CoreWebView2 == null) return;

                var url = GetActivePaneRealUrl();
                if (string.IsNullOrWhiteSpace(url) ||
                    !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("Nothing to bookmark on this page.", "Bookmark", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string title = wv.CoreWebView2.DocumentTitle ?? url;

                _bookmarks.Insert(0, new BookmarkItem(title, url, DateTime.UtcNow));
                if (_bookmarks.Count > 200) _bookmarks.RemoveRange(200, _bookmarks.Count - 200);

                SaveBookmarks();

                MessageBox.Show($"Saved:\n{title}", "Bookmark", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Bookmark failed:\n{ex.Message}", "Bookmark", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void WebAction_History_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count == 0)
            {
                MessageBox.Show("No history yet.", "History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var list = string.Join("\n", _history.Take(25));
            MessageBox.Show(list, "History (Latest 25)", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void WebAction_Share_Click(object sender, RoutedEventArgs e)
        {
            if (ShareButton?.ContextMenu == null)
                return;

            ShareButton.ContextMenu.PlacementTarget = ShareButton;
            ShareButton.ContextMenu.IsOpen = true;
        }

        private string GetShareUrl()
        {
            var url = GetActivePaneRealUrl();
            return string.IsNullOrWhiteSpace(url) ? string.Empty : url;
        }

        private string GetShareTitle()
        {
            try
            {
                var wv = GetActiveWebView();
                if (wv?.CoreWebView2?.DocumentTitle is string title && !string.IsNullOrWhiteSpace(title))
                    return title.Trim();
            }
            catch
            {
            }

            var host = GetHost(GetShareUrl());
            return string.IsNullOrWhiteSpace(host) ? "Web Studio Page" : host;
        }

        private void ShareCopyLink_Click(object sender, RoutedEventArgs e)
        {
            var url = GetShareUrl();
            if (string.IsNullOrWhiteSpace(url))
                return;

            Clipboard.SetText(url);
            UpdateSecurityStatus("Link copied", false);
        }

        private void ShareCopyTitleLink_Click(object sender, RoutedEventArgs e)
        {
            var url = GetShareUrl();
            if (string.IsNullOrWhiteSpace(url))
                return;

            var title = GetShareTitle();
            Clipboard.SetText($"{title}\r\n{url}");
            UpdateSecurityStatus("Title and link copied", false);
        }

        private void ShareEmail_Click(object sender, RoutedEventArgs e)
        {
            var url = GetShareUrl();
            if (string.IsNullOrWhiteSpace(url))
                return;

            var title = GetShareTitle();
            var subject = Uri.EscapeDataString(title);
            var body = Uri.EscapeDataString($"{title}\r\n{url}");

            Process.Start(new ProcessStartInfo
            {
                FileName = $"mailto:?subject={subject}&body={body}",
                UseShellExecute = true
            });

            UpdateSecurityStatus("Share opened in email", false);
        }

        // ============================================
        // Top bar buttons
        // ============================================

        private void HomeTopButton_Click(object sender, RoutedEventArgs e)
        {
            if (GuardLocked()) return;
            NavigateTo(_homeUrl);
        }

        private void BackTopButton_Click(object sender, RoutedEventArgs e) => BackButton_Click(sender, e);

        private void ForwardTopButton_Click(object sender, RoutedEventArgs e) => ForwardButton_Click(sender, e);

        private void SnapBackTopButton_Click(object sender, RoutedEventArgs e)
        {
            if (GuardLocked()) return;

            try
            {
                var activeWeb = GetActiveWebView();
                if (activeWeb?.CoreWebView2 == null) return;

                var currentUrl = GetActivePaneRealUrl();
                if (string.IsNullOrWhiteSpace(currentUrl)) return;

                activeWeb.CoreWebView2.Stop();
                ResetActivePaneStatus();
                activeWeb.CoreWebView2.Navigate(currentUrl);

                _pageUrls[_activePageIndex] = currentUrl;

                if (AddressBox != null)
                {
                    AddressBox.Text = FormatUrlForDisplay(currentUrl);
                    NormalizeAddressBoxView();
                }
            }
            catch { }
        }

        private void PowerOffButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Before closing Web Studio Browser, make sure you’ve saved any work in open pages.",
                "Close Web Studio Browser?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Close();
            }
        }

        // ============================================
        // WebView init
        // ============================================

        private async Task EnsureWebViewAsync()
        {
            await _webViewInitGate.WaitAsync();
            try
            {
                if (_webViewInitDone) return;

                if (Web == null) { _webViewInitDone = true; return; }

                // Create one shared environment for ALL WebViews (important)
                if (_sharedWebViewEnv == null)
                {
                    var userData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Web Studio Browser", "WebView2");

                    Directory.CreateDirectory(userData);

                    CoreWebView2EnvironmentOptions? opts = null;
                    if (_dnsMode == DnsApplyMode.AppOnlySecureDns)
                        opts = new CoreWebView2EnvironmentOptions(BuildWebView2ArgumentsForDns(_dnsPreset));

                    _sharedWebViewEnv = await CoreWebView2Environment.CreateAsync(null, userData, opts);
                }

                async Task InitPane(WebView2? wv, int paneIndex)
                {
                    if (wv == null) return;

                    // Avoid re-hooking events if EnsureWebViewAsync is called more than once
                    if ((wv.Tag as string) == "inited") return;

                    if (wv.CoreWebView2 == null)
                        await wv.EnsureCoreWebView2Async(_sharedWebViewEnv);

                    EnableAdBlockOn(wv);
                    HookAddressUpdates(wv);
                    if (wv.CoreWebView2 != null)
                        wv.CoreWebView2.SourceChanged += (_, __) => Dispatcher.Invoke(UpdateSecureSiteVisual);
                    HookPaneActivation(wv, paneIndex);

                    wv.Tag = "inited";
                }

                await InitPane(Web, 0);
                await InitPane(Web2, 1);
                await InitPane(Web3, 2);
                await InitPane(Web4, 3);

                _webViewInitDone = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2 init failed:\n" + ex.Message,
                    "Web Studio Browser",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _webViewInitGate.Release();
            }
        }

        private void HookNewWindow(WebView2 wv)
        {
            if (wv?.CoreWebView2 == null) return;

            wv.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrWhiteSpace(e.Uri))
                    NavigateTo(e.Uri);
            };

            wv.CoreWebView2.NavigationCompleted += (_, __) =>
            {
                Dispatcher.Invoke(UpdateSecureSiteVisual);
            };
        }

        private void HookHistory(WebView2? wv)
        {
            if (wv?.CoreWebView2 == null) return;

            wv.CoreWebView2.NavigationCompleted += (_, __) =>
            {
                try
                {
                    var url = wv.CoreWebView2.Source ?? "";
                    if (string.IsNullOrWhiteSpace(url)) return;

                    _history.Add(url);
                    if (_history.Count > 5000)
                        _history.RemoveRange(0, _history.Count - 5000);
                }
                catch { }
            };
        }

        // ============================================
        // Active pane
        // ============================================

        private void EnsurePanesUpTo(int maxIndex)
        {
            if (maxIndex < 0) maxIndex = 0;
            if (maxIndex > 3) maxIndex = 3;

            // Make panes 0..maxIndex visible again (re-open if Closed/Minimized)
            for (int i = 0; i <= maxIndex; i++)
                _paneState[i] = PaneLifeState.Normal;

            // Hide panes above maxIndex
            for (int i = maxIndex + 1; i < 4; i++)
                _paneState[i] = PaneLifeState.Minimized;

            _layoutPageCount = maxIndex + 1;

            ApplyLayout();
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();

            // Load default quick URL for any pane that is blank/newtab/about:blank
            for (int i = 0; i <= maxIndex; i++)
            {
                var current = _pageUrls[i] ?? "";
                bool needsDefault =
                    string.IsNullOrWhiteSpace(current) ||
                    current.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
                    current.Equals("lexwhite://newtab", StringComparison.OrdinalIgnoreCase);

                if (needsDefault)
                {
                    int prevActive = _activePageIndex;
                    _activePageIndex = i;
                    LoadQuickUrlSlotIntoActivePane(i); // uses slot i
                    _activePageIndex = prevActive;
                }
            }
        }

        private WebView2? GetWebView(int index) => index switch
        {
            0 => Web,   // <-- change these names to your actual XAML WebView2 names
            1 => Web2,
            2 => Web3,
            3 => Web4,
            _ => null
        };

        private WebView2? GetActiveWebView() => GetWebView(_activePageIndex);

        private Border? GetLockOverlay(int index) => index switch
        {
            0 => LockOverlay1,
            1 => LockOverlay2,
            2 => LockOverlay3,
            3 => LockOverlay4,
            _ => null
        };

        private Border? GetPageHost(int index) => index switch
        {
            0 => PageHost1,
            1 => PageHost2,
            2 => PageHost3,
            3 => PageHost4,
            _ => null
        };

        private Button? GetPageModeButton(int index) => index switch
        {
            0 => Page1Button,
            1 => Page2Button,
            2 => Page3Button,
            3 => Page4Button,
            _ => null
        };

        private void HookPaneActivation(WebView2 web, int paneIndex)
        {
            if (web == null) return;

            void Activate()
            {
                // Always hop to UI thread, avoids weirdness if called during init.
                Dispatcher.Invoke(() => ActivatePane(paneIndex));
            }

            // Focus-based activation is the most reliable for WPF WebView2 (HWND-hosted)
            web.GotFocus += (_, __) => Activate();
            web.GotKeyboardFocus += (_, __) => Activate();

            // Keep this too — sometimes it fires even when the focus events don't
            web.PreviewMouseDown += (_, __) => Activate();
        }

        // ============================================
        // Layout
        // ============================================

        private void ApplyLayout()
        {
            if (PagesHost == null) return;

            // Reset all hosts to collapsed and clear spans
            for (int i = 0; i < 4; i++)
            {
                var host = GetPageHost(i);
                if (host == null) continue;
                host.Visibility = Visibility.Collapsed;

                Grid.SetRow(host, 0);
                Grid.SetColumn(host, 0);
                Grid.SetRowSpan(host, 1);
                Grid.SetColumnSpan(host, 1);
            }

            PagesHost.RowDefinitions.Clear();
            PagesHost.ColumnDefinitions.Clear();

            // If maximized mode: show ONLY that pane (if it’s still visible)
            if (_isPaneMaximizedMode && _maximizedPane >= 0 && _maximizedPane <= 3
                && _paneState[_maximizedPane] == PaneLifeState.Normal)
            {
                PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var host = GetPageHost(_maximizedPane);
                if (host != null)
                {
                    host.Visibility = Visibility.Visible;
                    Grid.SetRow(host, 0);
                    Grid.SetColumn(host, 0);
                }
                return;
            }

            _isPaneMaximizedMode = false;
            _maximizedPane = -1;

            var visible = VisiblePanes();

            // Nothing visible? show pane 0 as a fallback newtab
            if (visible.Count == 0)
            {
                _paneState[0] = PaneLifeState.Normal;
                visible = VisiblePanes();
                _activePageIndex = 0;
            }

            int count = visible.Count;
            _layoutPageCount = count;

            // Build grid based on visible count
            if (count == 1)
            {
                PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Place(visible[0], 0, 0);
                return;
            }

            if (count == 2)
            {
                PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Place(visible[0], 0, 0);
                Place(visible[1], 0, 1);
                return;
            }

            if (count == 3)
            {
                PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // left tall + right split
                Place(visible[0], 0, 0, rowSpan: 2);
                Place(visible[1], 0, 1);
                Place(visible[2], 1, 1);
                return;
            }

            // 4 panes: 2x2
            PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            PagesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PagesHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Place(visible[0], 0, 0);
            Place(visible[1], 0, 1);
            Place(visible[2], 1, 1); // pane 3 (index 2) goes bottom-right
            Place(visible[3], 1, 0); // pane 4 (index 3) goes bottom-left

            void Place(int paneIndex, int row, int col, int rowSpan = 1, int colSpan = 1)
            {
                var host = GetPageHost(paneIndex);
                if (host == null) return;

                host.Visibility = Visibility.Visible;
                Grid.SetRow(host, row);
                Grid.SetColumn(host, col);
                Grid.SetRowSpan(host, rowSpan);
                Grid.SetColumnSpan(host, colSpan);
            }
        }

        // ============================================
        // Page mode buttons (1-4)
        // ============================================

        private void HandlePageModeButtonClick(int index)
        {
            if (_paneState[index] != PaneLifeState.Normal)
            {
                SaveSnapshot();
                _paneState[index] = PaneLifeState.Normal;
            }
            if (index < 0 || index > 3) return;

            if (_activePageIndex == index)
            {
                SetPageLocked(index, !_pageLocked[index]);
                return;
            }

            EnsurePanesUpTo(index);
            _activePageIndex = index;
            RefreshActivePaneStatusVisual();
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
            UpdateSecureSiteVisual();

            // Auto-load the Quick URL that matches this pane number (1-4 buttons)
            // Pane 1 -> slot 0, Pane 2 -> slot 1, Pane 3 -> slot 2, Pane 4 -> slot 3
            LoadQuickUrlSlotIntoActivePane(index);
        }

        private void UpdatePageHostBorders()
        {
            // Keep thickness constant to avoid layout jitter (changing thickness resizes panes)
            var t = new Thickness(2);

            for (int i = 0; i < 4; i++)
            {
                var host = GetPageHost(i);
                if (host == null) continue;

                host.BorderThickness = t;

                if (_pageLocked[i])
                    host.BorderBrush = Brushes.IndianRed;
                else if (i == _activePageIndex)
                    host.BorderBrush = Brushes.DarkGreen;
                else
                    host.BorderBrush = Brushes.SandyBrown;
            }
        }


        private void UpdatePageModeButtonVisuals()
        {
            Brush normal = Brushes.SandyBrown;
            try
            {
                if (Application.Current?.Resources["ButtonBorder"] is Brush b)
                    normal = b;
            }
            catch { }

            Brush active = Brushes.DarkGreen;
            Brush locked = Brushes.IndianRed;

            for (int i = 0; i < 4; i++)
            {
                var btn = GetPageModeButton(i);
                if (btn == null) continue;

                if (_pageLocked[i])
                {
                    btn.BorderBrush = locked;
                    btn.BorderThickness = new Thickness(2);
                }
                else if (_activePageIndex == i)
                {
                    btn.BorderBrush = active;
                    btn.BorderThickness = new Thickness(2);
                }
                else
                {
                    btn.BorderBrush = normal;
                    btn.BorderThickness = new Thickness(1);
                }
            }
        }

        private void MinimizeActivePane()
        {
            if (_paneState[_activePageIndex] != PaneLifeState.Normal)
                return;

            SaveSnapshot();

            _paneState[_activePageIndex] = PaneLifeState.Minimized;

            // pick next visible pane as active (or stay if none)
            var visible = VisiblePanes();
            if (visible.Count > 0)
                _activePageIndex = visible[0];

            ApplyLayout();
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
            UpdateSecureSiteVisual();
        }

        private void MaximizeActivePane()
        {
            // If already in maximize-mode, SnapBack should restore instead (but user wants SnapBack button)
            if (_paneState[_activePageIndex] != PaneLifeState.Normal)
                return;

            SaveSnapshot();

            _isPaneMaximizedMode = true;
            _maximizedPane = _activePageIndex;

            ApplyLayout();
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
            UpdateSecureSiteVisual();
        }

        private void CloseActivePane()
        {
            if (_paneState[_activePageIndex] == PaneLifeState.Closed)
                return;

            SaveSnapshot();

            // blank it out so it’s “closed”
            try
            {
                var wv = GetWebView(_activePageIndex);
                wv?.CoreWebView2?.Navigate("about:blank");
            }
            catch { }

            _pageUrls[_activePageIndex] = "about:blank";
            _paneState[_activePageIndex] = PaneLifeState.Closed;

            var visible = VisiblePanes();
            if (visible.Count > 0)
                _activePageIndex = visible[0];

            // if we just closed the maximized pane, exit maximize mode
            if (_isPaneMaximizedMode && _maximizedPane == _activePageIndex)
            {
                _isPaneMaximizedMode = false;
                _maximizedPane = -1;
            }

            ApplyLayout();
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
            UpdateSecureSiteVisual();
        }

        private void SetPageLocked(int index, bool locked)
        {
            if (index < 0 || index > 3) return;

            _pageLocked[index] = locked;

            var wv = GetWebView(index);
            if (wv != null) wv.IsEnabled = !locked;

            var overlay = GetLockOverlay(index);
            if (overlay != null)
                overlay.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;

            var host = GetPageHost(index);
            if (host != null)
            {
                host.BorderBrush = locked ? Brushes.Red
                    : (_activePageIndex == index ? Brushes.Green : Brushes.SandyBrown);
                host.BorderThickness = locked ? new Thickness(4) : new Thickness(2);
            }

            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
        }

        private void Page1Button_Click(object sender, RoutedEventArgs e) => HandlePageModeButtonClick(0);
        private void Page2Button_Click(object sender, RoutedEventArgs e) => HandlePageModeButtonClick(1);
        private void Page3Button_Click(object sender, RoutedEventArgs e) => HandlePageModeButtonClick(2);
        private void Page4Button_Click(object sender, RoutedEventArgs e) => HandlePageModeButtonClick(3);

        // ============================================
        // Page host click activation
        // ============================================

        // ==============================
        // Active Pane Sync (CLEAN)
        // ==============================

        private void PageHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            int idx = -1;

            // Tag may come through as int or string depending on XAML parsing
            if (fe.Tag is int i) idx = i;
            else if (fe.Tag is string s && int.TryParse(s, out var j)) idx = j;

            if (idx < 0 || idx > 3) return;

            // Do NOT allow minimized/closed panes to become active
            if (_paneState[idx] != PaneLifeState.Normal) return;

            if (_activePageIndex == idx) return;

            _activePageIndex = idx;
            RefreshActivePaneStatusVisual();

            UpdatePageHostBorders();
            UpdatePageModeButtonVisuals();
            UpdateSecureSiteVisual();
        }

        private void WebView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            SetActiveFromWebView(sender);
        }

        private void WebView_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SetActiveFromWebView(sender);
        }

        private void SetActiveFromWebView(object sender)
        {
            int idx =
                ReferenceEquals(sender, Web) ? 0 :
                ReferenceEquals(sender, Web2) ? 1 :
                ReferenceEquals(sender, Web3) ? 2 :
                ReferenceEquals(sender, Web4) ? 3 :
                -1;

            if (idx < 0) return;

            // Do NOT allow minimized/closed panes to steal focus
            if (_paneState[idx] != PaneLifeState.Normal) return;

            if (_activePageIndex == idx) return;

            _activePageIndex = idx;
            RefreshActivePaneStatusVisual();

            UpdatePageHostBorders();
            UpdatePageModeButtonVisuals();
            UpdateSecureSiteVisual();
        }

        // ============================================
        // Navigation / Address bar
        // ============================================

        private bool GuardLocked()
        {
            if (_pageLocked[_activePageIndex])
            {
                UpdateSecurityStatus("Page Locked", true);
                return true;
            }
            return false;
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            if (GuardLocked()) return;
            NavigateTo(AddressBox.Text);
        }

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (GuardLocked()) return;

            NavigateTo(AddressBox.Text);
            e.Handled = true;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (GuardLocked()) return;
            try
            {
                var currentUrl = GetActivePaneRealUrl();

                if (!string.IsNullOrWhiteSpace(currentUrl) &&
                    string.Equals(currentUrl.TrimEnd('/'), _homeUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "This is the first page in this tab's history.",
                        "Back",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var activeWeb = GetActiveWebView();
                if (activeWeb?.CoreWebView2 != null && activeWeb.CoreWebView2.CanGoBack)
                    activeWeb.CoreWebView2.GoBack();
            }
            catch { }
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (GuardLocked()) return;
            try
            {
                var activeWeb = GetActiveWebView();
                if (activeWeb?.CoreWebView2 != null && activeWeb.CoreWebView2.CanGoForward)
                {
                    activeWeb.CoreWebView2.GoForward();
                    return;
                }

                MessageBox.Show(
                    "This is the last page in this tab's history.",
                    "Forward",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch { }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (GuardLocked()) return;

            try
            {
                var activeWeb = GetActiveWebView();
                if (activeWeb?.CoreWebView2 == null) return;

                activeWeb.CoreWebView2.Reload();
            }
            catch { }
        }

        private void NavigateToNewTab()
        {
            _pageUrls[_activePageIndex] = "lexwhite://newtab";

            if (AddressBox != null)
            {
                AddressBox.Text = "lexwhite://newtab";
                NormalizeAddressBoxView();
            }

            try
            {
                var activeWeb = GetActiveWebView();
                activeWeb?.CoreWebView2?.Navigate("about:blank");
            }
            catch { }
        }

        private void NavigateTo(string inputRaw)
        {
            if (GuardLocked()) return;
            if (string.IsNullOrWhiteSpace(inputRaw)) return;

            string input = inputRaw.Trim();

            if (input.Equals("lexwhite://newtab", StringComparison.OrdinalIgnoreCase))
            {
                NavigateToNewTab();
                _pageUrls[_activePageIndex] = "lexwhite://newtab";
                return;
            }

            _lastUserInput = inputRaw.Trim();
            _lastNavigationWasSearch = false;

            Uri? uri;

            if (input.Contains("://"))
            {
                if (!Uri.TryCreate(input, UriKind.Absolute, out uri))
                {
                    uri = BuildSearchUri(inputRaw);
                    _lastNavigationWasSearch = true;
                }
            }
            else
            {
                if (LooksLikeDomain(input))
                {
                    uri = new Uri("https://" + input);
                }
                else
                {
                    uri = BuildSearchUri(inputRaw);
                    _lastNavigationWasSearch = true;
                }
            }

            var activeWeb = GetActiveWebView();

            try
            {
                if (activeWeb?.CoreWebView2 != null)
                {
                    activeWeb.CoreWebView2.Navigate(uri.ToString());
                }
                else
                {
                    // Don't set Source — it can auto-init with the wrong environment.
                    _ = Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await EnsureWebViewAsync();
                        var wv2 = GetActiveWebView();
                        wv2?.CoreWebView2?.Navigate(uri.ToString());
                    }));
                }
            }
            catch
            {
                try { if (activeWeb != null) activeWeb.Source = uri; } catch { }
            }

            _pageUrls[_activePageIndex] = uri.ToString();

            if (AddressBox != null)
            {
                AddressBox.Text = _lastNavigationWasSearch ? _lastUserInput : FormatUrlForDisplay(uri.ToString());
                NormalizeAddressBoxView();
            }

            UpdateSecureSiteVisual();
        }

        private void ResetActivePaneStatus()
        {
            _blockedTrackerCounts[_activePageIndex] = 0;
            _blockedAdCounts[_activePageIndex] = 0;
            _statusMessages[_activePageIndex] = "Secure";
            _statusWarnings[_activePageIndex] = false;
            _compatibilityModeActive[_activePageIndex] = false;
            _compatibilityModeLabels[_activePageIndex] = "";
            UpdateSecurityStatus("Secure", false);
        }

        private void RefreshActivePaneStatusVisual()
        {
            if (StatusIndicator == null || StatusIndicatorHost == null)
                return;

            var trackersBlocked = _blockedTrackerCounts[_activePageIndex];
            var adsBlocked = _blockedAdCounts[_activePageIndex];
            var message = string.IsNullOrWhiteSpace(_statusMessages[_activePageIndex])
                ? "Secure"
                : _statusMessages[_activePageIndex];
            var warning = _statusWarnings[_activePageIndex];
            var compatibility = _compatibilityModeActive[_activePageIndex];

            string summary;
            string dotColor;

            if (warning)
            {
                summary = "Warning";
                dotColor = "#FFFF5A5A";   // red
            }
            else if (trackersBlocked > 0 || adsBlocked > 0 || compatibility || _privacyMode != PrivacyMode.Off)
            {
                summary = "Protected";
                dotColor = "#FFFFB347";   // orange
            }
            else
            {
                summary = "Safe";
                dotColor = "#FF57D657";   // green
            }

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotColor));
            StatusIndicator.Fill = brush;

            if (StatusIndicator.Effect is DropShadowEffect glow)
                glow.Color = brush.Color;

            var currentUrl = GetActivePaneRealUrl();
            var host = GetHost(currentUrl);
            if (string.IsNullOrWhiteSpace(host))
                host = "Current page";

            StatusIndicatorHost.ToolTip =
                $"Status: {summary}\n" +
                $"Connection: {message}\n" +
                $"Trackers blocked: {trackersBlocked}\n" +
                $"Ads blocked: {adsBlocked}\n" +
                $"Privacy: {GetPrivacyModeLabel()}\n" +
                $"Page: {host}";

            if (StatusPopupSummaryText != null)
                StatusPopupSummaryText.Text = summary;

            if (StatusPopupConnectionText != null)
                StatusPopupConnectionText.Text = $"Connection: {message}";

            if (StatusPopupTrackersText != null)
                StatusPopupTrackersText.Text = $"Trackers blocked: {trackersBlocked}";

            if (StatusPopupAdsText != null)
                StatusPopupAdsText.Text = $"Ads blocked: {adsBlocked}";

            if (StatusPopupPrivacyText != null)
                StatusPopupPrivacyText.Text = $"Privacy: {GetPrivacyModeLabel()}";

            if (StatusPopupPageText != null)
                StatusPopupPageText.Text = $"Page: {host}";

            if (StatusPopupCompatibilityText != null)
            {
                if (compatibility)
                {
                    var label = string.IsNullOrWhiteSpace(_compatibilityModeLabels[_activePageIndex])
                        ? "Compatibility active"
                        : $"Compatibility active ({_compatibilityModeLabels[_activePageIndex]})";

                    StatusPopupCompatibilityText.Text = $"Mode: {label}";
                    StatusPopupCompatibilityText.Visibility = Visibility.Visible;
                }
                else
                {
                    StatusPopupCompatibilityText.Text = "";
                    StatusPopupCompatibilityText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void HookAddressUpdates(WebView2 web)
        {
            if (web?.CoreWebView2 == null) return;

            web.CoreWebView2.NavigationCompleted += (_, __) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_suppressAddressUpdates) return;

                    var src = web.CoreWebView2.Source;
                    if (string.IsNullOrWhiteSpace(src)) return;
                    if (AddressBox == null) return;

                    AddressBox.Text = _lastNavigationWasSearch ? _lastUserInput : FormatUrlForDisplay(src);
                    NormalizeAddressBoxView();
                });
            };
        }

        // ============================================
        // Address bar polish (your original behavior)
        // ============================================

        private void InitAddressBarTimers()
        {
            _addressEditTimer.Interval = TimeSpan.FromMilliseconds(600);
            _addressEditTimer.Tick += (_, __) =>
            {
                _addressEditTimer.Stop();
                _suppressAddressUpdates = false;
            };
        }

        private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _suppressAddressUpdates = true;
            _addressEditTimer.Stop();
            _addressEditTimer.Start();
        }

        private void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _suppressAddressUpdates = false;
            _addressEditTimer.Stop();
        }

        private void AddressBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
                tb.SelectAll();
                tb.ScrollToHome();
                tb.Dispatcher.BeginInvoke(new Action(() =>
                {
                    tb.SelectAll();
                    tb.ScrollToHome();
                }), DispatcherPriority.Input);
            }
        }

        private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var full = GetActivePaneRealUrl();
                if (!string.IsNullOrWhiteSpace(full))
                    tb.Text = full;

                tb.SelectAll();
                tb.ScrollToHome();
                tb.Dispatcher.BeginInvoke(new Action(() =>
                {
                    tb.SelectAll();
                    tb.ScrollToHome();
                }), DispatcherPriority.Input);
            }
        }

        private void NormalizeAddressBoxView()
        {
            if (AddressBox == null) return;
            if (AddressBox.IsKeyboardFocusWithin) return;

            AddressBox.CaretIndex = 0;
            AddressBox.Select(0, 0);
            AddressBox.ScrollToHome();

            AddressBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                AddressBox.CaretIndex = 0;
                AddressBox.Select(0, 0);
                AddressBox.ScrollToHome();
            }), DispatcherPriority.Background);
        }

        // ============================================
        // Secure / Not secure badge
        // ============================================

        private void UpdateSecureSiteVisual()
        {
            string? src = null;

            try
            {
                var wv = GetActiveWebView();
                src = wv?.CoreWebView2?.Source;
            }
            catch { }

            // Fallback if WebView2.Source isn't ready yet
            if (string.IsNullOrWhiteSpace(src))
                src = GetActivePaneRealUrl();

            bool isHttps = false;

            if (!string.IsNullOrWhiteSpace(src) &&
                Uri.TryCreate(src, UriKind.Absolute, out var u))
            {
                isHttps = u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            }

            UpdateSecurityStatus(isHttps ? "Secure" : "Not Secure", !isHttps);
        }

        private void UpdateSecurityStatus(string message, bool warning)
        {
            var st = new System.Diagnostics.StackTrace(1, false);
            var caller = st.GetFrame(0)?.GetMethod()?.Name ?? "unknown";
            System.Diagnostics.Debug.WriteLine(
                $"[STATUS] pane={_activePageIndex} text='{message}' flag={warning} caller={caller}"
            );

            _statusMessages[_activePageIndex] = message;
            _statusWarnings[_activePageIndex] = warning;

            RefreshActivePaneStatusVisual();
        }

       
        // ============================================
        // Quick URLs: load/save + context menus
        // ============================================

        private void LoadPresetUrls()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_presetUrlsPath)!);

                if (!File.Exists(_presetUrlsPath))
                    return;

                var json = File.ReadAllText(_presetUrlsPath);
                var loaded = JsonSerializer.Deserialize<string[]>(json);

                for (int i = 0; i < _presetUrls.Length; i++)
                {
                    if (loaded != null && i < loaded.Length)
                        _presetUrls[i] = loaded[i] ?? "";
                    else
                        _presetUrls[i] = "";
                }

                SavePresetUrls(); // normalize to 20 slots
            }
            catch { }
        }

        private void SavePresetUrls()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_presetUrlsPath)!);
                var json = JsonSerializer.Serialize(_presetUrls);
                File.WriteAllText(_presetUrlsPath, json);
            }
            catch { }
        }

        private void BuildPresetButtonMenus()
        {
            for (int i = 1; i <= 20; i++)
            {
                var btn = FindName($"PresetBtn{i}") as Button;
                if (btn == null) continue;

                var menu = new ContextMenu();

                var setItem = new MenuItem { Header = "Set URL..." };
                setItem.Click += PresetSetUrl_Click;

                var clearItem = new MenuItem { Header = "Clear" };
                clearItem.Click += PresetClear_Click;

                menu.Items.Add(setItem);
                menu.Items.Add(clearItem);

                btn.ContextMenu = menu;
            }
        }

        private void RefreshPresetButtonsUI()
        {
            for (int i = 1; i <= 20; i++)
            {
                var btn = FindName($"PresetBtn{i}") as Button;
                if (btn == null) continue;

                int idx = i - 1;
                var url = _presetUrls[idx];
                btn.Content = string.IsNullOrWhiteSpace(url) ? "(empty)" : GetSiteDisplayName(url);
            }
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!int.TryParse(btn.Tag?.ToString(), out int idx)) return;
            if (idx < 0 || idx >= _presetUrls.Length) return;

            var url = _presetUrls[idx];
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("No URL set for this button.\n\nRight-click it and choose 'Set URL...'.",
                                "Quick URL",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            NavigateTo(url);

            if (SettingsDropdown != null)
                SettingsDropdown.IsOpen = false;
        }

        private void PresetSetUrl_Click(object sender, RoutedEventArgs e)
        {
            var btn = GetPresetButtonFromContextMenuSender(sender);
            if (btn == null) return;

            if (!int.TryParse(btn.Tag?.ToString(), out int idx)) return;
            if (idx < 0 || idx >= _presetUrls.Length) return;

            string defaultUrl = _presetUrls[idx];
            if (string.IsNullOrWhiteSpace(defaultUrl))
                defaultUrl = GetActivePaneRealUrl();

            var url = Interaction.InputBox(
                "Paste a URL (include https://)",
                "Set Quick URL",
                defaultUrl ?? "");

            url = (url ?? "").Trim();
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (!url.Contains("://") && url.Contains("."))
                url = "https://" + url;

            _presetUrls[idx] = url;
            SavePresetUrls();
            RefreshPresetButtonsUI();
        }

        private void PresetClear_Click(object sender, RoutedEventArgs e)
        {
            var btn = GetPresetButtonFromContextMenuSender(sender);
            if (btn == null) return;

            if (!int.TryParse(btn.Tag?.ToString(), out int idx)) return;
            if (idx < 0 || idx >= _presetUrls.Length) return;

            _presetUrls[idx] = "";
            SavePresetUrls();
            RefreshPresetButtonsUI();
        }

        private Button? GetPresetButtonFromContextMenuSender(object sender)
        {
            if (sender is not MenuItem mi) return null;
            if (mi.Parent is not ContextMenu cm) return null;
            return cm.PlacementTarget as Button;
        }

        private void LoadQuickUrlSlotIntoActivePane(int presetIndex)
        {
            if (presetIndex < 0 || presetIndex >= _presetUrls.Length) return;

            // IMPORTANT: do NOT auto-expand panes here.
            // Pane expansion is controlled by the Page 1-4 buttons / EnsurePanesUpTo().
            int pane = _activePageIndex;

            // No ApplyLayout() here either — caller decides layout.
            // Only keep visuals in sync:
            UpdatePageModeButtonVisuals();
            UpdatePageHostBorders();
            UpdateSecureSiteVisual();

            if (_pageLocked[_activePageIndex])
            {
                UpdateSecurityStatus("Page Locked", true);
                return;
            }

            var url = _presetUrls[presetIndex];

            if (string.IsNullOrWhiteSpace(url))

            {
                NavigateTo("lexwhite://newtab");
                return;
            }

            _studioSlotIndex[pane] = presetIndex;
            ResetActivePaneStatus();
            NavigateTo(url);

            if (SettingsDropdown != null)
                SettingsDropdown.IsOpen = false;
        }

        private static string GetSiteDisplayName(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                var host = u.Host;
                if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    host = host.Substring(4);

                var first = host.Split('.').FirstOrDefault() ?? host;
                if (first.Length == 0) return host;

                return char.ToUpper(first[0]) + first.Substring(1);
            }

            return url;
        }

        // ============================================
        // Privacy dropdown
        // ============================================

        private void PrivacyNotes_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Privacy modes:\n\n" +

                "Off:\n" +
                "  No blocking. Everything loads normally.\n\n" +

                "Balanced:\n" +
                "  Blocks known ad and tracker hosts from the blocklist.\n" +
                "  Designed to reduce tracking with minimal site breakage.\n\n" +

                "Tracking Only:\n" +
                "  Blocks tracking scripts and known tracker domains.\n" +
                "  Ads and most site systems are still allowed to load.\n" +
                "  Best mode if some sites break under Balanced or Strict.\n\n" +

                "Strict:\n" +
                "  Blocks aggressive tracking, ad networks and suspicious scripts.\n" +
                "  Some websites may not work fully.\n\n" +

                "Status Indicator:\n" +
                "  Green  – Safe browsing, nothing blocked.\n" +
                "  Orange – Tracking or ads blocked.\n" +
                "  Red    – Security warning.\n\n" +

                "Tip: If a site breaks, switch privacy mode or whitelist it.",
                "Privacy Notes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void HideIpButton_Click(object sender, RoutedEventArgs e)
        {
            _hideIpEnabled = !_hideIpEnabled;
            RefreshPrivacyNetworkVisuals();
            RefreshActivePaneStatusVisual();
        }

        private void ChangeLocationButton_Click(object sender, RoutedEventArgs e)
        {
            _privacyLocationIndex++;
            if (_privacyLocationIndex >= _privacyLocations.Length)
                _privacyLocationIndex = 0;

            RefreshPrivacyNetworkVisuals();
        }

        private async void PrivacyDeleteHistory_Click(object sender, RoutedEventArgs e)
        {
            // 1) Clear in-app session list
            _history.Clear();

            // 2) Clear WebView2 browsing data for each initialized pane
            try
            {
                await ClearBrowsingDataFor(Web);
                await ClearBrowsingDataFor(Web2);
                await ClearBrowsingDataFor(Web3);
                await ClearBrowsingDataFor(Web4);

                MessageBox.Show("History cleared.", "Privacy", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"History clear failed:\n{ex.Message}", "Privacy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static async Task ClearBrowsingDataFor(WebView2? wv)
        {
            if (wv?.CoreWebView2?.Profile == null) return;

            // "All" includes history, cookies, cache, storage, etc.
            await wv.CoreWebView2.Profile.ClearBrowsingDataAsync();
        }

        private void PrivacyOff_Click(object sender, RoutedEventArgs e)
        {
            _privacyMode = PrivacyMode.Off;
            SavePrivacyMode();
            UpdatePrivacyButtonLabel();
            UpdatePrivacyDropdownVisuals();
        }

        private void PrivacyBalanced_Click(object sender, RoutedEventArgs e)
        {
            _privacyMode = PrivacyMode.Balanced;
            SavePrivacyMode();
            UpdatePrivacyButtonLabel();
            UpdatePrivacyDropdownVisuals();
        }

        private void PrivacyStrict_Click(object sender, RoutedEventArgs e)
        {
            _privacyMode = PrivacyMode.Strict;
            SavePrivacyMode();
            UpdatePrivacyButtonLabel();
            UpdatePrivacyDropdownVisuals();
        }

        private void PrivacyTracking_Click(object sender, RoutedEventArgs e)
        {
            _privacyMode = PrivacyMode.TrackingOnly;
            SavePrivacyMode();
            UpdatePrivacyButtonLabel();
            UpdatePrivacyDropdownVisuals();
        }

        private void LoadPrivacyMode()
        {
            try
            {
                if (!File.Exists(_privacyPath)) return;

                var txt = (File.ReadAllText(_privacyPath) ?? "").Trim();
                if (Enum.TryParse(txt, out PrivacyMode mode))
                    _privacyMode = mode;
            }
            catch { }
        }

        private void SavePrivacyMode()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_privacyPath)!);
                File.WriteAllText(_privacyPath, _privacyMode.ToString());
            }
            catch { }
        }

        private void LoadHomeUrl()
        {
            try
            {
                // Default for fresh installs
                _homeUrl = "https://www.google.com";

                if (!File.Exists(_homeUrlPath))
                    return;

                var txt = (File.ReadAllText(_homeUrlPath) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(txt)) return;

                // Basic safety: only accept http/https
                if (txt.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    _homeUrl = txt;
                }
            }
            catch
            {
                _homeUrl = "https://www.google.com";
            }
        }

        private void SaveHomeUrl()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_homeUrlPath)!);
                File.WriteAllText(_homeUrlPath, _homeUrl ?? "");
            }
            catch { }
        }

        private void UpdatePrivacyButtonLabel()
        {
            if (PrivacyButton == null) return;

            PrivacyButton.Content = "🛡";

            PrivacyButton.ToolTip = _privacyMode switch
            {
                PrivacyMode.Off => "Privacy: Off",
                PrivacyMode.Balanced => "Privacy: Balanced",
                PrivacyMode.Strict => "Privacy: Strict",
                PrivacyMode.TrackingOnly => "Privacy: Tracking",
                _ => "Privacy"
            };
        }

        private void UpdatePrivacyDropdownVisuals()
        {
            if (PrivacyOffBtn == null || PrivacyBalancedBtn == null || PrivacyTrackingBtn == null || PrivacyStrictBtn == null)
                return;

            Brush normalBg = TryFindResource("ButtonBg") as Brush ?? Brushes.Beige;
            Brush activeBg = TryFindResource("ButtonPressedBg") as Brush ?? Brushes.BurlyWood;
            Brush normalBorder = TryFindResource("ButtonBorder") as Brush ?? Brushes.SandyBrown;
            Brush activeBorder = Brushes.DarkGreen;

            void Style(Button btn, bool active)
            {
                btn.Background = active ? activeBg : normalBg;
                btn.BorderBrush = active ? activeBorder : normalBorder;
                btn.BorderThickness = active ? new Thickness(2) : new Thickness(1);
                btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }

            Style(PrivacyOffBtn, _privacyMode == PrivacyMode.Off);
            Style(PrivacyBalancedBtn, _privacyMode == PrivacyMode.Balanced);
            Style(PrivacyTrackingBtn, _privacyMode == PrivacyMode.TrackingOnly);
            Style(PrivacyStrictBtn, _privacyMode == PrivacyMode.Strict);
        }

        private void ResetPrivacyButtonVisual(Button button)
        {
            button.Background = (Brush)FindResource("ButtonBg");
            button.BorderBrush = (Brush)FindResource("ButtonBorder");
            button.BorderThickness = new Thickness(1);
            button.Foreground = (Brush)FindResource("TextPrimary");
            button.FontWeight = FontWeights.Normal;
        }

        private void SetActivePrivacyButtonVisual(Button button)
        {
            button.Background = (Brush)FindResource("InputBg");
            button.BorderBrush = (Brush)FindResource("ButtonBorder");
            button.BorderThickness = new Thickness(2);
            button.Foreground = (Brush)FindResource("TextPrimary");
            button.FontWeight = FontWeights.SemiBold;
        }

      
        // ============================================
        // Adblock list + whitelist
        // ============================================

        private Task EnsureAdBlockListAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_adBlockListPath)!);

                // If user already has a blocklist, just load it.
                if (File.Exists(_adBlockListPath))
                {
                    LoadAdBlockHosts();
                    return Task.CompletedTask;
                }

                // First run: copy starter list shipped with the app (FAST, offline).
                var starterPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "adblock_hosts_starter.txt");

                if (File.Exists(starterPath))
                {
                    File.Copy(starterPath, _adBlockListPath, overwrite: true);
                }
                else
                {
                    // Absolute fallback (still fast)
                    File.WriteAllLines(_adBlockListPath, new[]
                    {
                "doubleclick.net",
                "googlesyndication.com",
                "googleadservices.com",
                "googletagmanager.com",
                "taboola.com",
                "outbrain.com"
            });
                }

                LoadAdBlockHosts();
            }
            catch
            {
                // If anything goes wrong, don’t block startup.
                try { LoadAdBlockHosts(); } catch { }
            }

            return Task.CompletedTask;
        }

        private void LoadAdBlockHosts()
        {
            _blockedHosts.Clear();
            if (!File.Exists(_adBlockListPath)) return;

            foreach (var line in File.ReadAllLines(_adBlockListPath))
            {
                var host = line.Trim();
                if (string.IsNullOrWhiteSpace(host)) continue;
                _blockedHosts.Add(host);
            }
        }

        private void LoadWhitelist()
        {
            _adBlockWhitelist.Clear();
            if (!File.Exists(_whitelistPath)) return;

            foreach (var line in File.ReadAllLines(_whitelistPath))
            {
                var host = line.Trim();
                if (!string.IsNullOrWhiteSpace(host))
                    _adBlockWhitelist.Add(host);
            }
        }

        private void SaveWhitelist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_whitelistPath)!);
            File.WriteAllLines(_whitelistPath, _adBlockWhitelist);
        }

        private async void UpdateBlocklist_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateBlocklistButton != null) UpdateBlocklistButton.IsEnabled = false;
            if (PrivacyStatusText != null) PrivacyStatusText.Text = "Updating blocklist...";

            try
            {
                if (File.Exists(_adBlockListPath))
                    File.Delete(_adBlockListPath);

                // Download EasyList ONLY when user clicks update
                using var client = new HttpClient();
                var raw = await client.GetStringAsync("https://easylist.to/easylist/easylist.txt");
                var hosts = ExtractHostsFromEasyList(raw);
                File.WriteAllLines(_adBlockListPath, hosts);

                LoadAdBlockHosts();

                if (PrivacyStatusText != null) PrivacyStatusText.Text = "Blocklist updated.";
                MessageBox.Show("Blocklist updated.");
            }
            catch
            {
                if (PrivacyStatusText != null) PrivacyStatusText.Text = "Blocklist update failed.";
                MessageBox.Show("Blocklist update failed.");
            }
            finally
            {
                if (UpdateBlocklistButton != null) UpdateBlocklistButton.IsEnabled = true;
            }
        }

        private static IEnumerable<string> ExtractHostsFromEasyList(string raw)
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("!")) continue;
                if (trimmed.StartsWith("[")) continue;

                if (trimmed.StartsWith("||"))
                {
                    var host = trimmed.Substring(2);
                    int end = host.IndexOfAny(new[] { '^', '/', '$' });
                    if (end > 0)
                        host = host.Substring(0, end);

                    if (!string.IsNullOrWhiteSpace(host))
                        list.Add(host);
                }
            }

            return list;
        }

        private static string GetHost(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return "";
            return Uri.TryCreate(uri, UriKind.Absolute, out var u) ? (u.Host ?? "") : "";
        }

        private static string RegistrableDomain(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return "";
            var parts = host.Split('.');
            if (parts.Length <= 2) return host;
            return parts[^2] + "." + parts[^1];
        }

        private static bool IsYouTubeHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;

            host = host.ToLowerInvariant();

            return host == "youtube.com"
                || host == "www.youtube.com"
                || host == "m.youtube.com"
                || host.EndsWith(".youtube.com")
                || host == "youtubei.googleapis.com"
                || host.EndsWith(".googlevideo.com")
                || host.EndsWith(".ytimg.com")
                || host.EndsWith(".ggpht.com");
        }

        private static bool IsYouTubePage(string? url)
        {
            var host = GetHost(url);
            return IsYouTubeHost(host);
        }

        private static bool IsYouTubeEssentialRequest(string? pageUrl, string? requestUrl)
        {
            if (!IsYouTubePage(pageUrl)) return false;

            var reqHost = GetHost(requestUrl);
            return IsYouTubeHost(reqHost);
        }

        private static bool HostMatches(string? host, params string[] suffixes)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;

            host = host.ToLowerInvariant();

            foreach (var suffix in suffixes)
            {
                var s = suffix.ToLowerInvariant();
                if (host == s || host.EndsWith("." + s))
                    return true;
            }

            return false;
        }

        private static bool IsBankingSite(string? url)
        {
            var host = GetHost(url);
            return HostMatches(host,
                "anz.com.au",
                "westpac.com.au",
                "stgeorge.com.au",
                "nab.com.au",
                "commbank.com.au",
                "bankwest.com.au",
                "suncorpbank.com.au",
                "macquarie.com.au");
        }

        private static bool IsGovernmentSite(string? url)
        {
            var host = GetHost(url);
            return HostMatches(host,
                "gov.au",
                "my.gov.au",
                "ato.gov.au",
                "service.nsw.gov.au",
                "vic.gov.au",
                "qld.gov.au");
        }

        private static bool IsAuthOrPaymentSite(string? url)
        {
            var host = GetHost(url);
            return HostMatches(host,
                "paypal.com",
                "stripe.com",
                "checkout.com",
                "afterpay.com",
                "zip.co");
        }

        private static string GetCompatibilityProfileLabel(string? url)
        {
            if (IsYouTubePage(url)) return "YouTube";
            if (IsBankingSite(url)) return "Banking";
            if (IsGovernmentSite(url)) return "Government";
            if (IsAuthOrPaymentSite(url)) return "Payments / Login";
            return "";
        }

        private static bool IsCompatibilitySensitiveSite(string? url)
        {
            return IsYouTubePage(url)
                || IsBankingSite(url)
                || IsGovernmentSite(url)
                || IsAuthOrPaymentSite(url);
        }

        private static bool IsObviousAdOrTrackerHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;

            host = host.ToLowerInvariant();

            return host == "doubleclick.net"
                || host.EndsWith(".doubleclick.net")
                || host == "pagead2.googlesyndication.com"
                || host.EndsWith(".googlesyndication.com")
                || host == "adservice.google.com"
                || host.EndsWith(".adservice.google.com")
                || host == "google-analytics.com"
                || host.EndsWith(".google-analytics.com")
                || host == "googletagmanager.com"
                || host.EndsWith(".googletagmanager.com")
                || host == "facebook.net"
                || host.EndsWith(".facebook.net")
                || host == "connect.facebook.net"
                || host == "hotjar.com"
                || host.EndsWith(".hotjar.com");
        }

        private static bool IsThirdParty(string pageUrl, string requestUrl)
        {
            var pageHost = GetHost(pageUrl);
            var reqHost = GetHost(requestUrl);

            if (string.IsNullOrWhiteSpace(pageHost) || string.IsNullOrWhiteSpace(reqHost))
                return false;

            return !RegistrableDomain(pageHost).Equals(RegistrableDomain(reqHost), StringComparison.OrdinalIgnoreCase);
        }
          

        private bool IsWhitelisted(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (_adBlockWhitelist.Contains(host)) return true;

            var parts = host.Split('.');
            for (int i = 1; i < parts.Length - 1; i++)
            {
                var suffix = string.Join(".", parts.Skip(i));
                if (_adBlockWhitelist.Contains(suffix)) return true;
            }

            return false;
        }

        private bool IsBlockedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;

            if (_blockedHosts.Contains(host)) return true;

            var parts = host.Split('.');
            for (int i = 1; i < parts.Length - 1; i++)
            {
                var suffix = string.Join(".", parts.Skip(i));
                if (_blockedHosts.Contains(suffix)) return true;
            }

            return false;
        }

        private void EnableAdBlockOn(WebView2 wv)
        {
            if (wv?.CoreWebView2 == null) return;

            wv.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

            wv.CoreWebView2.WebResourceRequested += (s, e) =>
            {
                if (!_adBlockEnabled) return;
                if (_privacyMode == PrivacyMode.Off) return;

                var reqUrl = e.Request?.Uri;
                if (string.IsNullOrWhiteSpace(reqUrl)) return;

                var pageUrl = "";
                try { pageUrl = wv.CoreWebView2.Source ?? ""; } catch { }

                var pageHost = GetHost(pageUrl);
                var reqHost = GetHost(reqUrl);

                if (string.IsNullOrWhiteSpace(reqHost)) return;

                var ctx = e.ResourceContext;

                // Never block the main page document itself
                if (ctx == CoreWebView2WebResourceContext.Document)
                    return;

                // Temporary YouTube compatibility mode:
                // while we stabilise playback, do not filter YouTube pages at all.

                bool thirdParty = IsThirdParty(pageUrl, reqUrl);
                bool knownBad = IsBlockedHost(reqHost);
                bool trackerContext = IsLikelyTrackerContext(ctx);
                bool safeMedia = IsSafeMediaContext(ctx);

                bool compatibilitySensitive = IsCompatibilitySensitiveSite(pageUrl);
                string compatibilityLabel = GetCompatibilityProfileLabel(pageUrl);

                if (compatibilitySensitive)
                {
                    _compatibilityModeActive[_activePageIndex] = true;
                    _compatibilityModeLabels[_activePageIndex] = compatibilityLabel;

                    // YouTube: allow essential playback infrastructure,
                    // but still block obvious ad/tracker hosts.
                    if (compatibilityLabel == "YouTube")
                    {
                        if (IsYouTubeEssentialRequest(pageUrl, reqUrl))
                            return;

                        if (thirdParty && IsObviousAdOrTrackerHost(reqHost))
                        {
                            BlockRequest(wv, e);
                            return;
                        }

                        return;
                    }

                    // Banking / Government / Payments:
                    // use gentle filtering instead of full bypass.
                    // Only block known bad third-party tracker-style requests.
                    if (thirdParty && knownBad && trackerContext && !safeMedia)
                    {
                        BlockRequest(wv, e);
                        return;
                    }

                    return;
                }

                // Full-site whitelist always wins
                if (!string.IsNullOrWhiteSpace(pageHost) && IsWhitelisted(pageHost))
                    return;
                          

                // Lightweight tracker URL heuristics
                string lowerUrl = reqUrl.ToLowerInvariant();
                bool suspiciousTrackerUrl =
                    lowerUrl.Contains("/collect") ||
                    lowerUrl.Contains("/analytics") ||
                    lowerUrl.Contains("/tracking") ||
                    lowerUrl.Contains("/tracker") ||
                    lowerUrl.Contains("/telemetry") ||
                    lowerUrl.Contains("/metrics") ||
                    lowerUrl.Contains("/pixel") ||
                    lowerUrl.Contains("utm_") ||
                    lowerUrl.Contains("fbclid=") ||
                    lowerUrl.Contains("gclid=");

                // First-party requests should almost always be allowed.
                // This is the main compatibility rule.
                if (!thirdParty)
                    return;

                switch (_privacyMode)
                {
                    case PrivacyMode.Balanced:
                        // Balanced:
                        // Only block known bad third-party hosts.
                        if (knownBad)
                        {
                            BlockRequest(wv, e);
                            return;
                        }
                        return;

                    case PrivacyMode.TrackingOnly:
                        // Tracking Only:
                        // Focus on third-party tracking behavior, but avoid breaking media-rich pages.
                        if ((knownBad || suspiciousTrackerUrl) && trackerContext && !safeMedia)
                        {
                            BlockRequest(wv, e);
                            return;
                        }
                        return;

                    case PrivacyMode.Strict:
                        // Strict:
                        // Block known bad third-party hosts.
                        if (knownBad && !safeMedia)
                        {
                            BlockRequest(wv, e);
                            return;
                        }

                        // Also block suspicious third-party tracker-style requests
                        // even if they are not yet in the host block list.
                        if ((trackerContext || suspiciousTrackerUrl) && !safeMedia)
                        {
                            BlockRequest(wv, e);
                            return;
                        }
                        return;

                    default:
                        return;
                }
            };
        }

        private void BlockRequest(WebView2 wv, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var reqUrl = e.Request?.Uri ?? "";
                var reqHost = GetHost(reqUrl)?.ToLowerInvariant() ?? "";
                var lowerUrl = reqUrl.ToLowerInvariant();

                bool looksLikeAd =
                    reqHost.Contains("doubleclick") ||
                    reqHost.Contains("googlesyndication") ||
                    reqHost.Contains("adservice") ||
                    lowerUrl.Contains("/ads") ||
                    lowerUrl.Contains("adformat=") ||
                    lowerUrl.Contains("advert");

                bool looksLikeTracker =
                    reqHost.Contains("analytics") ||
                    reqHost.Contains("googletagmanager") ||
                    reqHost.Contains("facebook") ||
                    reqHost.Contains("hotjar") ||
                    lowerUrl.Contains("/collect") ||
                    lowerUrl.Contains("/tracking") ||
                    lowerUrl.Contains("/tracker") ||
                    lowerUrl.Contains("/telemetry") ||
                    lowerUrl.Contains("/metrics") ||
                    lowerUrl.Contains("/pixel") ||
                    lowerUrl.Contains("utm_") ||
                    lowerUrl.Contains("fbclid=") ||
                    lowerUrl.Contains("gclid=");

                if (looksLikeAd && !looksLikeTracker)
                    _blockedAdCounts[_activePageIndex]++;
                else
                    _blockedTrackerCounts[_activePageIndex]++;

                var env = wv.CoreWebView2.Environment;
                e.Response = env.CreateWebResourceResponse(
                    null, 403, "Blocked", "Content-Type: text/plain");

                UpdateSecurityStatus("Tracking / ad activity blocked", false);
            }
            catch { }
        }

        // ============================================
        // Helpers
        // ============================================
        private string GetPrivacyModeLabel()
        {
            return _privacyMode switch
            {
                PrivacyMode.Off => "Off",
                PrivacyMode.Balanced => "Balanced",
                PrivacyMode.TrackingOnly => "Tracking Only",
                PrivacyMode.Strict => "Strict",
                _ => "Balanced"
            };
        }

        private void RefreshPrivacyNetworkVisuals()
        {
            if (HideIpButton != null)
                HideIpButton.Content = _hideIpEnabled ? "Hide IP: On" : "Hide IP: Off";

            if (ChangeLocationButton != null)
                ChangeLocationButton.Content = $"Change Location: {_privacyLocations[_privacyLocationIndex]}";

            if (PrivacyNetworkStatusText != null)
            {
                PrivacyNetworkStatusText.Text = _hideIpEnabled
                    ? $"Privacy relay requested • Exit: {_privacyLocations[_privacyLocationIndex]} • Backend not connected"
                    : "Privacy network not connected";
            }
        }
        // ============================================
        // Privacy helpers (resource contexts)
        // ============================================

        private static bool IsLikelyTrackerContext(CoreWebView2WebResourceContext ctx) =>
            ctx == CoreWebView2WebResourceContext.Script ||
            ctx == CoreWebView2WebResourceContext.XmlHttpRequest;

        private static bool IsSafeMediaContext(CoreWebView2WebResourceContext ctx) =>
            ctx == CoreWebView2WebResourceContext.Image ||
            ctx == CoreWebView2WebResourceContext.Media ||
            ctx == CoreWebView2WebResourceContext.Stylesheet ||
            ctx == CoreWebView2WebResourceContext.Font;

        private void ActivatePane(int paneIndex)
        {
            if (paneIndex < 0 || paneIndex > 3) return;

            if (_paneState[paneIndex] != PaneLifeState.Normal)
                return;

            // Prevent re-entrant focus events causing jitter / repeated activation
            if (_isActivatingPane) return;

            _isActivatingPane = true;
            try
            {
                if (_activePageIndex != paneIndex)
                {
                    _activePageIndex = paneIndex;
                    UpdatePageHostBorders();
                    UpdatePageModeButtonVisuals();
                    UpdateSecureSiteVisual();
                }

                // Focus the active pane (only if it doesn't already have focus)
                var web = GetWebView(paneIndex);
                if (web != null && !web.IsKeyboardFocusWithin)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            web.Focus();
                            System.Windows.Input.Keyboard.Focus(web);
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
            finally
            {
                // Drop the guard after the current UI input cycle
                Dispatcher.BeginInvoke(new Action(() => _isActivatingPane = false),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }


        private void SaveSessionState()
        {
            try
            {
                var state = new AppSessionState(
                    _pageUrls,
                    _activePageIndex,
                    _layoutPageCount
                );

                Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);

                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_sessionPath, json);
            }
            catch { }
        }

        private bool LoadSessionState()
        {
            try
            {
                if (!File.Exists(_sessionPath))
                    return false;

                var json = File.ReadAllText(_sessionPath);
                var state = JsonSerializer.Deserialize<AppSessionState>(json);
                if (state == null)
                    return false;

                if (state.PageUrls != null)
                {
                    for (int i = 0; i < _pageUrls.Length && i < state.PageUrls.Length; i++)
                    {
                        _pageUrls[i] = state.PageUrls[i] ?? "";
                    }
                }

                _activePageIndex = state.ActivePageIndex;
                EnsurePanesUpTo(state.LayoutPageCount - 1);

                // Restore URLs into panes
                for (int i = 0; i < _layoutPageCount; i++)
                {
                    var url = _pageUrls[i];
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    int prev = _activePageIndex;
                    _activePageIndex = i;
                    NavigateTo(url);
                    _activePageIndex = prev;
                }

                return true; // session restored
            }
            catch
            {
                return false;
            }
        }

        private sealed class WindowsDnsBackup
        {
            public string AdapterCaption { get; }
            public string[]? DnsServers { get; }
            public WindowsDnsBackup(string adapterCaption, string[]? dnsServers)
            {
                AdapterCaption = adapterCaption;
                DnsServers = dnsServers;
            }
        }


        private readonly string _windowsDnsBackupPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Web Studio Browser", "windows_dns_backup.json");

        private void SaveWindowsDnsBackup(List<WindowsDnsBackup> backup)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_windowsDnsBackupPath)!);
                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_windowsDnsBackupPath, json);
            }
            catch { }
        }

        private List<WindowsDnsBackup> LoadWindowsDnsBackup()
        {
            try
            {
                if (!File.Exists(_windowsDnsBackupPath)) return new List<WindowsDnsBackup>();
                var json = File.ReadAllText(_windowsDnsBackupPath);
                return JsonSerializer.Deserialize<List<WindowsDnsBackup>>(json) ?? new List<WindowsDnsBackup>();
            }
            catch
            {
                return new List<WindowsDnsBackup>();
            }
        }

        private static List<WindowsDnsBackup> CaptureActiveAdaptersDns()
        {
            var result = new List<WindowsDnsBackup>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE");

            foreach (ManagementObject mo in searcher.Get())
            {
                // Only adapters that look like they're actually in use
                var gateways = mo["DefaultIPGateway"] as string[];
                if (gateways == null || gateways.Length == 0) continue;

                var caption = (mo["Caption"] as string) ?? "Unknown Adapter";
                var dns = mo["DNSServerSearchOrder"] as string[];
                result.Add(new WindowsDnsBackup(caption, dns));
            }

            return result;
        }

        private static void SetWindowsIpv4DnsOnActiveAdapters(DnsPreset preset)
        {
            var (dns, dhcp) = GetIpv4DnsForPreset(preset);

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE");

            foreach (ManagementObject mo in searcher.Get())
            {
                var gateways = mo["DefaultIPGateway"] as string[];
                if (gateways == null || gateways.Length == 0) continue;

                if (dhcp)
                {
                    // Reset to automatic
                    mo.InvokeMethod("SetDNSServerSearchOrder", null);
                }
                else
                {
                    var parms = mo.GetMethodParameters("SetDNSServerSearchOrder");
                    parms["DNSServerSearchOrder"] = dns;
                    mo.InvokeMethod("SetDNSServerSearchOrder", parms, null);
                }
            }
        }

        private static void RestoreWindowsIpv4DnsFromBackup(List<WindowsDnsBackup> backup)
        {
            if (backup.Count == 0) return;

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE");

            foreach (ManagementObject mo in searcher.Get())
            {
                var caption = (mo["Caption"] as string) ?? "";
                var match = backup.FirstOrDefault(b => caption.Contains(b.AdapterCaption, StringComparison.OrdinalIgnoreCase)
                                                   || b.AdapterCaption.Contains(caption, StringComparison.OrdinalIgnoreCase));

                // If no match, skip
                if (match == null) continue;

                if (match.DnsServers == null || match.DnsServers.Length == 0)
                {
                    mo.InvokeMethod("SetDNSServerSearchOrder", null);
                }
                else
                {
                    var parms = mo.GetMethodParameters("SetDNSServerSearchOrder");
                    parms["DNSServerSearchOrder"] = match.DnsServers;
                    mo.InvokeMethod("SetDNSServerSearchOrder", parms, null);
                }
            }
        }

        private static bool IsRunningAsAdmin()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private static (string[]? dns, bool dhcp) GetIpv4DnsForPreset(DnsPreset preset) => preset switch
        {
            DnsPreset.Cloudflare => (new[] { "1.1.1.1", "1.0.0.1" }, false),
            DnsPreset.Quad9 => (new[] { "9.9.9.9", "149.112.112.112" }, false),
            DnsPreset.OpenDNS => (new[] { "208.67.222.222", "208.67.220.220" }, false),
            DnsPreset.CleanBrowsing => (new[] { "185.228.168.9", "185.228.169.9" }, false),
            DnsPreset.AdGuard => (new[] { "94.140.14.14", "94.140.15.15" }, false),
            DnsPreset.Google => (new[] { "8.8.8.8", "8.8.4.4" }, false),
            _ => ((string[]?)null, true), // SystemDefault => DHCP/auto
        };

        private static void SetWindowsIpv4Dns(DnsPreset preset)
        {
            var (dns, dhcp) = GetIpv4DnsForPreset(preset);

            // Pick the "active" adapter(s): IPEnabled + has a default gateway
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE");

            foreach (ManagementObject mo in searcher.Get())
            {
                var gateways = mo["DefaultIPGateway"] as string[];
                if (gateways == null || gateways.Length == 0) continue;

                if (dhcp)
                {
                    // Reset DNS to automatic
                    mo.InvokeMethod("SetDNSServerSearchOrder", null);
                }
                else
                {
                    var newDns = mo.GetMethodParameters("SetDNSServerSearchOrder");
                    newDns["DNSServerSearchOrder"] = dns;
                    mo.InvokeMethod("SetDNSServerSearchOrder", newDns, null);
                }
            }
        }
        private async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
        {
            var args = _dnsMode == DnsApplyMode.AppOnlySecureDns
        ? BuildWebView2ArgumentsForDns(_dnsPreset)
        : "";

            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = args
            };

            // Let WebView2 use its default user data folder unless you want to specify one
            return await CoreWebView2Environment.CreateAsync(null, null, options);
        }

        private static string BuildWebView2ArgumentsForDns(DnsPreset preset)
        {
            // SystemDefault: do nothing
            if (preset == DnsPreset.SystemDefault) return "";

            // Chromium DoH flags
            // mode=secure => use DoH and fail closed if not available (more strict)
            // You can swap to "automatic" if you want it to fall back.
            var doh = preset switch
            {
                DnsPreset.Cloudflare => "https://cloudflare-dns.com/dns-query",
                DnsPreset.Quad9 => "https://dns.quad9.net/dns-query",
                DnsPreset.OpenDNS => "https://doh.opendns.com/dns-query",
                DnsPreset.CleanBrowsing => "https://doh.cleanbrowsing.org/doh/security-filter/",
                DnsPreset.AdGuard => "https://dns.adguard-dns.com/dns-query",
                DnsPreset.Google => "https://dns.google/dns-query",
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(doh)) return "";

            return $"--dns-over-https-mode=secure --dns-over-https-servers=\"{doh}\"";
        }
        private bool HasAnyPrintersInstalled()
        {
            try
            {
                var server = new System.Printing.LocalPrintServer();
                var queues = server.GetPrintQueues();
                return queues != null && queues.Any();
            }
            catch
            {
                return false;
            }
        }

        private string GetActivePaneRealUrl()
        {
            try
            {
                var wv = GetActiveWebView();
                var src = wv?.CoreWebView2?.Source;
                if (!string.IsNullOrWhiteSpace(src))
                    return src;
            }
            catch { }

            var tracked = _pageUrls[_activePageIndex];
            return string.IsNullOrWhiteSpace(tracked) ? "" : tracked;
        }



        private static string FormatUrlForDisplay(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim();

            if (url.StartsWith("lexwhite://", StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return url;

            if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                if (u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                    u.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    var host = u.Host;
                    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                        host = host.Substring(4);

                    var display = host + u.PathAndQuery + u.Fragment;
                    return string.IsNullOrWhiteSpace(display) ? url : display;
                }
            }

            return url;
        }


        private static bool LooksLikeDomain(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            input = input.Trim();

            if (input.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            if (System.Net.IPAddress.TryParse(input, out _))
                return true;

            if (input.Contains('.') && !input.Any(char.IsWhiteSpace))
                return true;

            return false;
        }

        private Uri BuildSearchUri(string query)
        {
            var q = Uri.EscapeDataString(query);

            var url = _searchEngine switch
            {
                SearchEngine.Mojeek => $"https://www.mojeek.com/search?q={q}",
                SearchEngine.DuckDuckGo => $"https://duckduckgo.com/?q={q}",
                SearchEngine.Brave => $"https://search.brave.com/search?q={q}",
                SearchEngine.Startpage => $"https://www.startpage.com/sp/search?q={q}",
                SearchEngine.Qwant => $"https://www.qwant.com/?q={q}",
                SearchEngine.Google => $"https://www.google.com/search?q={q}",
                _ => $"https://www.google.com/search?q={q}"
            };

            return new Uri(url);
        }

        // ============================================
        // Themes (minimal)
        // ============================================

        private void ApplyTheme(AppTheme theme)
        {
            void SetBrush(string key, string hex)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);

                if (Resources.Contains(key))
                    Resources[key] = new SolidColorBrush(color);
            }

            if (theme == AppTheme.Classic)
            {
                SetBrush("AppBg", "#FFF6F1EA");
                SetBrush("TopBarBg", "#FFF6F1EA");
                SetBrush("ButtonBg", "#FFEDE6DB");
                SetBrush("ButtonBorder", "#FFB9A07B");
                SetBrush("ButtonHoverBg", "#FFF3EEE6");
                SetBrush("ButtonPressedBg", "#FFE2D6C6");
                SetBrush("DividerBrush", "#FFE0D1BE");
                SetBrush("TextPrimary", "#FF1F1A12");
                SetBrush("TextSecondary", "#FF6E6254");
                SetBrush("CardBg", "#FFFFFFFF");
                SetBrush("CardBorder", "#FFE7DED2");
                SetBrush("InputBg", "#FFFFFFFF");
                SetBrush("InputBorder", "#FFB9A07B");
                return;
            }

            if (theme == AppTheme.Light)
            {
                SetBrush("AppBg", "#FFF5F6F8");
                SetBrush("TopBarBg", "#FFF3F4F6");
                SetBrush("ButtonBg", "#FFFFFFFF");
                SetBrush("ButtonBorder", "#FFD6DAE0");
                SetBrush("ButtonHoverBg", "#FFF7F8FA");
                SetBrush("ButtonPressedBg", "#FFE9EDF2");
                SetBrush("DividerBrush", "#FFD9DDE3");
                SetBrush("TextPrimary", "#FF1F2937");
                SetBrush("TextSecondary", "#FF6B7280");
                SetBrush("CardBg", "#FFFFFFFF");
                SetBrush("CardBorder", "#FFE5E7EB");
                SetBrush("InputBg", "#FFFFFFFF");
                SetBrush("InputBorder", "#FFD1D5DB");
                return;
            }

            if (theme == AppTheme.Dark)
            {
                SetBrush("AppBg", "#FF14161A");
                SetBrush("TopBarBg", "#FF1B1E24");
                SetBrush("ButtonBg", "#FF252932");
                SetBrush("ButtonBorder", "#FF3A404C");
                SetBrush("ButtonHoverBg", "#FF2D3340");
                SetBrush("ButtonPressedBg", "#FF353C49");
                SetBrush("DividerBrush", "#FF323844");

                // brighter text for readability
                SetBrush("TextPrimary", "#FFFFFFFF");
                SetBrush("TextSecondary", "#FFD1D5DB");

                SetBrush("CardBg", "#FF20242B");
                SetBrush("CardBorder", "#FF343B47");
                SetBrush("InputBg", "#FF111318");
                SetBrush("InputBorder", "#FF434B59");
            }
        }

        private void UpdateThemesButtonLabel(AppTheme theme)
        {
            if (ThemesButton == null) return;

            ThemesButton.Content = "◑";
            ThemesButton.ToolTip = $"Themes: {theme}";
        }

        // ============================================
        // Updates (optional button wiring if you add it)
        // ============================================

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var currentStr = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                Version current = Version.TryParse(currentStr, out var c) ? c : new Version(0, 0, 0);

                var json = await _http.GetStringAsync(UpdateManifestUrl);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestStr = root.TryGetProperty("latest", out var latestProp)
                    ? (latestProp.GetString() ?? "0.0.0")
                    : "0.0.0";

                Version latest = Version.TryParse(latestStr, out var l) ? l : new Version(0, 0, 0);

                var downloadUrl = root.TryGetProperty("downloadUrl", out var durl)
                    ? (durl.GetString() ?? "")
                    : "";

                var notes = root.TryGetProperty("notes", out var notesProp)
                    ? (notesProp.GetString() ?? "")
                    : "";

                if (current >= latest)
                {
                    _isLatestUpdate = true;
                    MessageBox.Show($"You're up to date.\n\nCurrent: {current}\nLatest: {latest}",
                        "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _isLatestUpdate = false;
                var msg =
                    $"Update available.\n\nCurrent: {current}\nLatest: {latest}" +
                    (string.IsNullOrWhiteSpace(notes) ? "" : $"\n\nNotes:\n{notes}") +
                    "\n\nOpen download page?";

                var open = MessageBox.Show(msg, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (open == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(downloadUrl))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                _isLatestUpdate = false;
                MessageBox.Show("Update check failed.\n\n(Unable to reach update server.)",
                    "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            } // end MainWindow
        }     // end namespace


        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only drag when clicking "empty" bar space (not buttons, textbox, etc.)
            if (e.ButtonState != MouseButtonState.Pressed) return;

            // If the click started inside an interactive control, do NOT drag
            if (IsClickOnInteractiveControl(e.OriginalSource as DependencyObject))
                return;

            try
            {
                // Double-click on the top bar = maximize/restore (nice Windows feel)
                if (e.ClickCount == 2)
                {
                    WindowState = (WindowState == WindowState.Maximized)
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    return;
                }

                DragMove();
            }
            catch { }
        }

        private static bool IsClickOnInteractiveControl(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ButtonBase) return true;
                if (source is TextBoxBase) return true;
                if (source is ComboBox) return true;
                if (source is PasswordBox) return true;
                if (source is ToggleButton) return true;
                if (source is ListBox) return true;
                if (source is MenuItem) return true;
                if (source is ScrollBar) return true;
                if (source is Microsoft.Web.WebView2.Wpf.WebView2) return true;

                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
    }

}
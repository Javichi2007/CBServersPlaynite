using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK;

namespace CBServersPlaynite
{
    public partial class CBServersView : UserControl
    {
        private readonly LauncherService service;
        private readonly IPlayniteAPI api;
        private readonly List<CbGame> allGames;
        private readonly ObservableCollection<CbGame> visibleGames = new ObservableCollection<CbGame>();
        private readonly ObservableCollection<ServerInfo> servers = new ObservableCollection<ServerInfo>();
        private readonly List<ServerInfo> allServers = new List<ServerInfo>();

        private readonly DispatcherTimer dataTimer;
        private readonly DispatcherTimer serverTimer;

        private CbGame detailGame;
        private CbGame currentServerGame;
        private string filter = "all";
        private bool suppressToggles;
        private bool serversLoading;
        private bool loadedOnce;

        public CBServersView(LauncherService service, IPlayniteAPI api)
        {
            InitializeComponent();

            this.service = service;
            this.api = api;
            allGames = Catalog.Create(Path.Combine(service.PluginRoot, "Assets", "Games"));

            GamesPanel.ItemsSource = visibleGames;
            ServersList.ItemsSource = servers;
            ServerGameList.ItemsSource = allGames;

            HeaderLogo.Source = LoadBitmap(Path.Combine(service.PluginRoot, "Assets", "cb-icon.png"), 64);
            VersionLabel.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            dataTimer.Tick += async (sender, args) => await RefreshDataAsync();

            serverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            serverTimer.Tick += async (sender, args) =>
            {
                if (ServersTab.Visibility == Visibility.Visible && !serversLoading)
                {
                    await LoadServersAsync(currentServerGame);
                }
            };

            ApplyFilter();
            UpdateLauncherStatus();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            dataTimer.Start();
            serverTimer.Start();

            if (!loadedOnce)
            {
                loadedOnce = true;
                await RefreshDataAsync();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            dataTimer.Stop();
            serverTimer.Stop();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) CloseDetail();
        }

        // ---------------- Datos ----------------

        private async Task RefreshDataAsync()
        {
            try
            {
                SetStatus("Actualizando datos de CB…");
                service.Reload();

                var properties = await Task.Run(() => service.ReadProperties());
                foreach (var game in allGames) service.ApplyState(game, properties);

                var playerName = service.GetPlayerName(properties);
                PlayerLabel.Text = string.IsNullOrWhiteSpace(playerName) ? "INVITADO" : playerName.ToUpperInvariant();

                UpdateLauncherStatus();
                UpdateSettingsValues(properties);

                try
                {
                    var counts = await service.GetPlayerCountsAsync();
                    foreach (var game in allGames)
                    {
                        GameCount count;
                        if (counts.TryGetValue(game.BackendId, out count))
                        {
                            game.OnlinePlayers = count.Players;
                            game.OnlineServers = count.Servers;
                        }
                    }
                    SetStatus("Datos actualizados · " + DateTime.Now.ToString("HH:mm"));
                }
                catch (Exception ex)
                {
                    SetStatus("Servicio de servidores no disponible: " + ex.Message);
                }

                ApplyFilter();
                UpdateCounts();
            }
            catch (Exception ex)
            {
                SetStatus("Error al actualizar: " + ex.Message);
            }
        }

        private void UpdateCounts()
        {
            var installed = allGames.Count(game => game.Installed);
            GamesCountText.Text = allGames.Count + " JUEGOS · " + installed + (installed == 1 ? " INSTALADO" : " INSTALADOS");
        }

        private void UpdateLauncherStatus()
        {
            var running = service.IsRunning();
            StatusDot.Fill = new SolidColorBrush(running ? Color.FromRgb(0x35, 0xD3, 0x99) : Color.FromRgb(0x5A, 0x66, 0x73));
            StatusLabel.Text = running ? "CB ABIERTO" : "CB CERRADO";
            StatusLabel.Foreground = new SolidColorBrush(running ? Color.FromRgb(0xA8, 0xE6, 0xCC) : Color.FromRgb(0x9A, 0xA7, 0xB4));
        }

        private void SetStatus(string text)
        {
            StatusLine.Text = text;
        }

        // ---------------- Juegos ----------------

        private void ApplyFilter()
        {
            var query = GameSearch == null ? "" : (GameSearch.Text ?? "").Trim();

            IEnumerable<CbGame> list = allGames;
            if (filter == "installed") list = list.Where(game => game.Installed);
            if (filter == "missing") list = list.Where(game => !game.Installed);
            if (query.Length > 0)
            {
                list = list.Where(game =>
                    game.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    game.ShortName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            visibleGames.Clear();
            foreach (var game in list) visibleGames.Add(game);

            GamesEmpty.Visibility = visibleGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string ?? "all";
            filter = tag;

            FilterAll.IsChecked = tag == "all";
            FilterInstalled.IsChecked = tag == "installed";
            FilterMissing.IsChecked = tag == "missing";

            ApplyFilter();
        }

        private void GameSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void CardOpen_Click(object sender, MouseButtonEventArgs e)
        {
            var game = GetGame(sender);
            if (game != null) OpenDetail(game);
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            var game = GetGame(sender);
            if (game == null) return;

            if (!game.Installed)
            {
                InstallGame(game);
                return;
            }

            LaunchGame(game, game.DefaultMode);
        }

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            var game = GetGame(sender);
            if (game == null) return;

            var menu = new ContextMenu();

            if (game.Modes != null && game.Modes.Length > 0)
            {
                foreach (var mode in game.Modes)
                {
                    var captured = mode;
                    var item = new MenuItem { Header = "Jugar · " + ModeLabels.Label(mode) };
                    item.Click += (s, args) => LaunchGame(game, captured);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
            }

            AddMenu(menu, game.Installed ? "Reinstalar / actualizar" : "Instalar", () => InstallGame(game));
            AddMenu(menu, "Abrir ficha en CB", () => OpenPage(game));
            AddMenu(menu, "Abrir mods en CB", () => OpenMods(game));
            if (!string.IsNullOrEmpty(game.InstallPath))
            {
                AddMenu(menu, "Abrir carpeta de instalación", () => Guard(() => service.OpenFolder(game.InstallPath)));
            }
            AddMenu(menu, "Ver detalles", () => OpenDetail(game));

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private static void AddMenu(ContextMenu menu, string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        private void LaunchGame(CbGame game, string mode)
        {
            Guard(() =>
            {
                service.Launch(game, mode);
                SetStatus("Lanzando " + game.Name + (string.IsNullOrEmpty(mode) ? "" : " · " + ModeLabels.Label(mode)) + "…");
                CloseDetail();
            });
        }

        private void InstallGame(CbGame game)
        {
            Guard(() =>
            {
                service.Install(game);
                SetStatus("Abriendo la instalación de " + game.Name + " en CB Launcher…");
                CloseDetail();
            });
        }

        private void OpenPage(CbGame game)
        {
            Guard(() =>
            {
                service.OpenGamePage(game);
                SetStatus("Abriendo la ficha de " + game.Name + " en CB Launcher…");
            });
        }

        private void OpenMods(CbGame game)
        {
            Guard(() =>
            {
                service.OpenMods(game);
                SetStatus("Abriendo los mods de " + game.Name + " en CB Launcher…");
            });
        }

        private static CbGame GetGame(object sender)
        {
            var element = sender as FrameworkElement;
            if (element == null) return null;
            return element.Tag as CbGame ?? element.DataContext as CbGame;
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
            }
        }

        // ---------------- Detalle ----------------

        private void OpenDetail(CbGame game)
        {
            detailGame = game;

            DetailHero.Source = LoadBitmap(game.HeroPath, 900);
            DetailLogo.Source = LoadBitmap(game.LogoPath, 400);
            DetailName.Text = game.Name;
            DetailOnline.Text = game.Installed
                ? game.OnlinePlayers + " jugadores en línea · " + game.OnlineServers + " servidores"
                : "No instalado";
            DetailOnline.Foreground = new SolidColorBrush(game.Installed ? Color.FromRgb(0x35, 0xD3, 0x99) : Color.FromRgb(0x82, 0x90, 0xA0));
            DetailPath.Text = game.Installed && !string.IsNullOrEmpty(game.InstallPath)
                ? game.InstallPath
                : "Este juego todavía no está instalado. Usa INSTALAR para abrir el flujo de CB Launcher.";

            DetailModes.Children.Clear();
            if (game.Modes != null)
            {
                foreach (var mode in game.Modes)
                {
                    var captured = mode;
                    var button = new Button
                    {
                        Content = ModeLabels.Label(mode).ToUpperInvariant(),
                        Style = (Style)FindResource("PillButton"),
                        Margin = new Thickness(0, 0, 6, 6)
                    };
                    button.Click += (s, e) => LaunchGame(game, captured);
                    DetailModes.Children.Add(button);
                }
            }

            if (game.Modes == null || game.Modes.Length == 0)
            {
                var button = new Button
                {
                    Content = "JUGAR",
                    Style = (Style)FindResource("PillButton"),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                button.Click += (s, e) => LaunchGame(game, null);
                DetailModes.Children.Add(button);
            }

            DetailPlay.Content = game.Installed ? "JUGAR" : "INSTALAR";
            DetailOverlay.Visibility = Visibility.Visible;
        }

        private void CloseDetail()
        {
            DetailOverlay.Visibility = Visibility.Collapsed;
            detailGame = null;
        }

        private void Overlay_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == DetailOverlay) CloseDetail();
        }

        private void DetailInner_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            CloseDetail();
        }

        private void DetailPlay_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame == null) return;
            if (detailGame.Installed) LaunchGame(detailGame, detailGame.DefaultMode);
            else InstallGame(detailGame);
        }

        private void DetailInstall_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null) InstallGame(detailGame);
        }

        private void DetailOpenPage_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null) OpenPage(detailGame);
        }

        private void DetailMods_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null) OpenMods(detailGame);
        }

        private void DetailFolder_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null && !string.IsNullOrEmpty(detailGame.InstallPath))
            {
                Guard(() => service.OpenFolder(detailGame.InstallPath));
            }
        }

        // ---------------- Servidores ----------------

        private void EnsureServerSelection()
        {
            if (ServerGameList.SelectedItem == null)
            {
                var saved = allGames.FirstOrDefault(game => game.Id == service.Settings.ServerGameId);
                ServerGameList.SelectedItem = saved ?? allGames.FirstOrDefault(game => game.Installed) ?? allGames.FirstOrDefault();
            }
        }

        private void ServerGame_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var game = ServerGameList.SelectedItem as CbGame;
            if (game == null) return;

            currentServerGame = game;
            service.Settings.ServerGameId = game.Id;
            service.SaveSettings();

            if (ServersTab.Visibility != Visibility.Visible) return;

            var ignored = LoadServersAsync(game);
        }

        private async Task LoadServersAsync(CbGame game)
        {
            if (game == null || serversLoading) return;

            serversLoading = true;
            ServersUpdatedText.Text = "Cargando…";

            try
            {
                var list = await service.GetServersAsync(game.BackendId);
                allServers.Clear();
                allServers.AddRange(list);
                ApplyServerFilter();
                ServersUpdatedText.Text = list.Count + " servidores · " + DateTime.Now.ToString("HH:mm");
            }
            catch (Exception ex)
            {
                allServers.Clear();
                servers.Clear();
                ServersUpdatedText.Text = "Error";
                ServersEmpty.Text = "No se pudo cargar la lista de servidores: " + ex.Message;
                ServersEmpty.Visibility = Visibility.Visible;
            }
            finally
            {
                serversLoading = false;
            }
        }

        private void ApplyServerFilter()
        {
            var query = ServerSearch == null ? "" : (ServerSearch.Text ?? "").Trim();

            IEnumerable<ServerInfo> list = allServers;
            if (query.Length > 0)
            {
                list = list.Where(server =>
                    Contains(server.Name, query) ||
                    Contains(server.Map, query) ||
                    Contains(server.Address, query));
            }

            servers.Clear();
            foreach (var server in list) servers.Add(server);

            if (servers.Count == 0)
            {
                ServersEmpty.Text = query.Length > 0
                    ? "Ningún servidor coincide con el filtro."
                    : (currentServerGame == null ? "Selecciona un juego para ver sus servidores." : "No hay servidores para este juego ahora mismo.");
                ServersEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                ServersEmpty.Visibility = Visibility.Collapsed;
            }
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ServerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyServerFilter();
        }

        private async void RefreshServers_Click(object sender, RoutedEventArgs e)
        {
            await LoadServersAsync(currentServerGame ?? ServerGameList.SelectedItem as CbGame);
        }

        private void CopyIp_Click(object sender, RoutedEventArgs e)
        {
            var server = (sender as FrameworkElement)?.Tag as ServerInfo;
            if (server == null) return;

            try
            {
                Clipboard.SetText(server.Address);
                SetStatus("IP copiada: " + server.Address);
            }
            catch
            {
            }
        }

        private void OpenServerGame_Click(object sender, RoutedEventArgs e)
        {
            var game = currentServerGame ?? ServerGameList.SelectedItem as CbGame;
            if (game == null) return;

            Guard(() =>
            {
                service.OpenGamePage(game);
                SetStatus("Ficha de " + game.Name + " abierta en CB. Entra al servidor desde su pestaña Servers.");
            });
        }

        // ---------------- Ajustes ----------------

        private void UpdateSettingsValues(Dictionary<string, string> properties)
        {
            if (!LauncherPathBox.IsKeyboardFocusWithin)
            {
                LauncherPathBox.Text = service.LauncherPath ?? "";
            }

            DataPathText.Text = service.DataRoot ?? "No detectada";
            PlayerNameText.Text = service.GetPlayerName(properties) ?? "Sin nombre configurado";

            suppressToggles = true;
            try
            {
                CloseOnLaunchCheck.IsChecked = service.GetBoolProperty(properties, "launcher-close-on-launch");
                OfflineCheck.IsChecked = service.GetBoolProperty(properties, "launcher-offline-mode");
            }
            finally
            {
                suppressToggles = false;
            }
        }

        private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecciona cb-launcher.exe",
                Filter = "CB Servers Launcher (cb-launcher.exe)|cb-launcher.exe|Ejecutables (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                service.SetLauncherPath(dialog.FileName);
                UpdateSettingsValues(service.ReadProperties());
                var ignored = RefreshDataAsync();
            }
        }

        private void DetectLauncher_Click(object sender, RoutedEventArgs e)
        {
            service.SetLauncherPath(null);
            UpdateSettingsValues(service.ReadProperties());
            var ignored = RefreshDataAsync();
            SetStatus(service.LauncherPath == null ? "No se ha encontrado cb-launcher.exe." : "Launcher detectado: " + service.LauncherPath);
        }

        private void OpenLauncher_Click(object sender, RoutedEventArgs e)
        {
            Guard(() => service.OpenLauncher());
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            Guard(() => service.OpenDataFolder());
        }

        private void ReloadData_Click(object sender, RoutedEventArgs e)
        {
            var ignored = RefreshDataAsync();
            if (ServersTab.Visibility == Visibility.Visible)
            {
                var ignoredServers = LoadServersAsync(currentServerGame);
            }
        }

        private void CloseOnLaunch_Click(object sender, RoutedEventArgs e)
        {
            WriteBool("launcher-close-on-launch", CloseOnLaunchCheck.IsChecked == true);
        }

        private void Offline_Click(object sender, RoutedEventArgs e)
        {
            WriteBool("launcher-offline-mode", OfflineCheck.IsChecked == true);
        }

        private void WriteBool(string key, bool value)
        {
            if (suppressToggles) return;

            try
            {
                service.SetProperty(key, value ? "true" : "false");
                SetStatus("Ajuste guardado: " + key);

                if (service.IsRunning())
                {
                    api.Dialogs.ShowMessage("CB Launcher está abierto: ciérralo para que el cambio no se sobrescriba al salir.", "CB Servers");
                }

                UpdateSettingsValues(service.ReadProperties());
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
            }
        }

        // ---------------- Navegación ----------------

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as FrameworkElement)?.Tag as string ?? "games";
            ShowTab(tag);
        }

        private void ShowTab(string tag)
        {
            GamesTab.Visibility = tag == "games" ? Visibility.Visible : Visibility.Collapsed;
            ServersTab.Visibility = tag == "servers" ? Visibility.Visible : Visibility.Collapsed;
            SettingsTab.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

            TabGames.IsChecked = tag == "games";
            TabServers.IsChecked = tag == "servers";
            TabSettings.IsChecked = tag == "settings";

            if (tag == "servers") EnsureServerSelection();
            if (tag == "settings") UpdateSettingsValues(service.ReadProperties());
        }

        // ---------------- Utilidades ----------------

        private static BitmapImage LoadBitmap(string path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}

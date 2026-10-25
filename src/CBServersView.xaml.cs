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
        private readonly SocialService social;
        private readonly IPlayniteAPI api;
        private readonly List<CbGame> allGames;
        private readonly ObservableCollection<CbGame> visibleGames = new ObservableCollection<CbGame>();
        private readonly ObservableCollection<ServerInfo> servers = new ObservableCollection<ServerInfo>();
        private readonly List<ServerInfo> allServers = new List<ServerInfo>();
        private readonly ObservableCollection<CbFriend> incomingFriends = new ObservableCollection<CbFriend>();
        private readonly ObservableCollection<CbFriend> outgoingFriends = new ObservableCollection<CbFriend>();
        private readonly ObservableCollection<CbFriend> onlineFriends = new ObservableCollection<CbFriend>();
        private readonly ObservableCollection<CbFriend> offlineFriends = new ObservableCollection<CbFriend>();

        private readonly DispatcherTimer dataTimer;
        private readonly DispatcherTimer serverTimer;
        private readonly DispatcherTimer socialTimer;

        private CbGame detailGame;
        private CbGame currentServerGame;
        private string filter = "all";
        private bool suppressToggles;
        private bool serversLoading;
        private bool friendsLoading;
        private bool launcherPreparing;
        private bool loadedOnce;

        public CBServersView(LauncherService service, IPlayniteAPI api)
        {
            InitializeComponent();

            this.service = service;
            this.api = api;
            social = new SocialService(service);
            allGames = Catalog.Create(Path.Combine(service.PluginRoot, "Assets", "Games"));

            GamesPanel.ItemsSource = visibleGames;
            ServersList.ItemsSource = servers;
            ServerGameList.ItemsSource = allGames;
            IncomingList.ItemsSource = incomingFriends;
            OutgoingList.ItemsSource = outgoingFriends;
            OnlineFriendList.ItemsSource = onlineFriends;
            OfflineFriendList.ItemsSource = offlineFriends;

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

            socialTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            socialTimer.Tick += async (sender, args) =>
            {
                if (FriendsTab.Visibility == Visibility.Visible && !friendsLoading)
                {
                    await LoadFriendsAsync(true);
                }
            };

            ApplyFilter();
            UpdateLauncherStatus();
            UpdateFriendsIdentity();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            dataTimer.Start();
            serverTimer.Start();
            socialTimer.Start();

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
            socialTimer.Stop();
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

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            var game = GetGame(sender);
            if (game == null) return;

            if (!game.Installed)
            {
                await InstallGameAsync(game);
                return;
            }

            await LaunchGameAsync(game, game.DefaultMode);
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
                    item.Click += async (s, args) => await LaunchGameAsync(game, captured);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
            }

            AddMenu(menu, game.Installed ? "Reinstalar / actualizar" : "Instalar", () => InstallGameAsync(game));
            AddMenu(menu, "Abrir ficha en CB", () => OpenPageAsync(game));
            AddMenu(menu, "Abrir mods en CB", () => OpenModsAsync(game));
            if (!string.IsNullOrEmpty(game.InstallPath))
            {
                AddMenu(menu, "Abrir carpeta de instalación", () => GuardAsync(() => service.OpenFolder(game.InstallPath)));
            }
            AddMenu(menu, "Ver detalles", () => { OpenDetail(game); return Done(); });

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private static void AddMenu(ContextMenu menu, string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (s, e) => await action();
            menu.Items.Add(item);
        }

        private static Task Done()
        {
            return Task.FromResult(0);
        }

        private async Task<bool> EnsureLauncherReadyAsync()
        {
            if (!string.IsNullOrEmpty(service.LauncherPath) && File.Exists(service.LauncherPath)) return true;
            if (launcherPreparing)
            {
                SetStatus("La descarga de CB Launcher ya está en curso…");
                return false;
            }

            launcherPreparing = true;
            SetStatus("Descargando CB Launcher (solo la primera vez)…");

            try
            {
                await service.EnsureLauncherAsync();
                UpdateLauncherStatus();
                UpdateSettingsValues(service.ReadProperties());
                var ignored = RefreshDataAsync();
                SetStatus("CB Launcher listo: " + service.LauncherPath);
                return true;
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
                SetStatus("No se pudo preparar CB Launcher.");
                return false;
            }
            finally
            {
                launcherPreparing = false;
            }
        }

        private async Task LaunchGameAsync(CbGame game, string mode)
        {
            if (!await EnsureLauncherReadyAsync()) return;

            Guard(() =>
            {
                service.Launch(game, mode);
                SetStatus("Lanzando " + game.Name + (string.IsNullOrEmpty(mode) ? "" : " · " + ModeLabels.Label(mode)) + "…");
                CloseDetail();
            });
        }

        private async Task InstallGameAsync(CbGame game)
        {
            if (!await EnsureLauncherReadyAsync()) return;

            Guard(() =>
            {
                service.Install(game);
                SetStatus("Abriendo la instalación de " + game.Name + " en CB Launcher…");
                CloseDetail();
            });
        }

        private async Task OpenPageAsync(CbGame game)
        {
            if (!await EnsureLauncherReadyAsync()) return;

            Guard(() =>
            {
                service.OpenGamePage(game);
                SetStatus("Abriendo la ficha de " + game.Name + " en CB Launcher…");
            });
        }

        private async Task OpenModsAsync(CbGame game)
        {
            if (!await EnsureLauncherReadyAsync()) return;

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

        private Task GuardAsync(Action action)
        {
            Guard(action);
            return Done();
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
                    button.Click += async (s, e) => await LaunchGameAsync(game, captured);
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
                button.Click += async (s, e) => await LaunchGameAsync(game, null);
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

        private async void DetailPlay_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame == null) return;
            if (detailGame.Installed) await LaunchGameAsync(detailGame, detailGame.DefaultMode);
            else await InstallGameAsync(detailGame);
        }

        private async void DetailInstall_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null) await InstallGameAsync(detailGame);
        }

        private async void DetailOpenPage_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null) await OpenPageAsync(detailGame);
        }

        private async void DetailMods_Click(object sender, RoutedEventArgs e)
        {
            if (detailGame != null) await OpenModsAsync(detailGame);
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

        private async void OpenServerGame_Click(object sender, RoutedEventArgs e)
        {
            var game = currentServerGame ?? ServerGameList.SelectedItem as CbGame;
            if (game == null) return;

            await OpenPageAsync(game);
            SetStatus("Ficha de " + game.Name + " abierta en CB. Entra al servidor desde su pestaña Servers.");
        }

        // ---------------- Ajustes ----------------

        private void UpdateSettingsValues(Dictionary<string, string> properties)
        {
            if (!LauncherPathBox.IsKeyboardFocusWithin)
            {
                LauncherPathBox.Text = service.LauncherPath ?? "";
            }

            LauncherStateText.Text = string.IsNullOrEmpty(service.LauncherPath)
                ? "No instalado · se descarga al usarlo"
                : "Listo para usar";

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

        private async void OpenLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsureLauncherReadyAsync()) return;
            Guard(() => service.OpenLauncher());
        }

        private async void DownloadLauncher_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Preparando CB Launcher…");
            if (await EnsureLauncherReadyAsync())
            {
                UpdateSettingsValues(service.ReadProperties());
                SetStatus("CB Launcher disponible en " + service.LauncherPath);
            }
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

        // ---------------- Amigos ----------------

        private void UpdateFriendsIdentity()
        {
            social.ReloadIdentity();

            var linked = social.IsLinked;
            FriendsSetupPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            FriendActionsPanel.IsEnabled = linked;
            FriendActionsPanel.Opacity = linked ? 1.0 : 0.55;

            if (linked)
            {
                FriendsErrorText.Visibility = Visibility.Collapsed;
                FriendsEmptyPanel.Visibility = Visibility.Collapsed;
                return;
            }

            FriendsSetupText.Text = social.LinkError ?? "Conecta tu cuenta de CB para ver a tus amigos.";
            FriendsDownloadButton.Content = string.IsNullOrEmpty(service.LauncherPath) ? "DESCARGAR CB" : "ABRIR CB";
            FriendsCountText.Text = "SIN CUENTA";
            FriendsStateDot.Fill = new SolidColorBrush(Color.FromRgb(0x5A, 0x66, 0x73));

            IncomingList.Visibility = Visibility.Collapsed;
            OutgoingList.Visibility = Visibility.Collapsed;
            RequestsSection.Visibility = Visibility.Collapsed;
            OnlineSection.Visibility = Visibility.Collapsed;
            OfflineSection.Visibility = Visibility.Collapsed;

            FriendsEmptyPanel.Visibility = Visibility.Visible;
            FriendsEmptyTitle.Text = "Conecta tu cuenta de CB";
            FriendsEmptyText.Text = "Descarga y abre CB Launcher una vez para crear tu identidad. Después pulsa REINTENTAR y tus amigos aparecerán aquí.";
        }

        private async Task LoadFriendsAsync(bool silent = false)
        {
            if (friendsLoading) return;

            UpdateFriendsIdentity();
            if (!social.IsLinked)
            {
                ClearFriends();
                return;
            }

            friendsLoading = true;
            if (!silent)
            {
                FriendsCountText.Text = "Cargando…";
                FriendsStateDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x1A));
            }

            try
            {
                var snapshot = await social.GetFriendsAsync();

                incomingFriends.Clear();
                outgoingFriends.Clear();
                onlineFriends.Clear();
                offlineFriends.Clear();

                foreach (var friend in snapshot.Incoming.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                    incomingFriends.Add(friend);
                foreach (var friend in snapshot.Outgoing.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                    outgoingFriends.Add(friend);
                foreach (var friend in snapshot.Friends.Where(item => item.Online).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                    onlineFriends.Add(friend);
                foreach (var friend in snapshot.Friends.Where(item => !item.Online).OrderByDescending(item => item.LastSeen).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                    offlineFriends.Add(friend);

                FriendsStateDot.Fill = new SolidColorBrush(snapshot.OnlineCount > 0 ? Color.FromRgb(0x35, 0xD3, 0x99) : Color.FromRgb(0x5A, 0x66, 0x73));
                FriendsCountText.Text = snapshot.OnlineCount + " EN LÍNEA · " + snapshot.Friends.Count + (snapshot.Friends.Count == 1 ? " AMIGO" : " AMIGOS");
                if (snapshot.RequestCount > 0)
                {
                    FriendsCountText.Text += " · " + snapshot.RequestCount + (snapshot.RequestCount == 1 ? " SOLICITUD" : " SOLICITUDES");
                }

                FriendsErrorText.Visibility = Visibility.Collapsed;
                UpdateFriendSections();

                if (!silent) SetStatus("Amigos actualizados · " + DateTime.Now.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                ClearFriends();
                FriendsStateDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x6A));
                FriendsCountText.Text = "SIN CONEXIÓN";
                FriendsErrorText.Text = ex.Message;
                FriendsErrorText.Visibility = Visibility.Visible;
                UpdateFriendSections();
                FriendsEmptyPanel.Visibility = Visibility.Collapsed;
            }
            finally
            {
                friendsLoading = false;
            }
        }

        private void ClearFriends()
        {
            incomingFriends.Clear();
            outgoingFriends.Clear();
            onlineFriends.Clear();
            offlineFriends.Clear();
        }

        private void UpdateFriendSections()
        {
            var requests = incomingFriends.Count + outgoingFriends.Count;

            RequestsSection.Visibility = requests > 0 ? Visibility.Visible : Visibility.Collapsed;
            RequestsHeader.Text = "SOLICITUDES · " + requests;
            IncomingList.Visibility = incomingFriends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OutgoingList.Visibility = outgoingFriends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            OnlineSection.Visibility = onlineFriends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OfflineSection.Visibility = offlineFriends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            OnlineHeader.Text = "EN LÍNEA · " + onlineFriends.Count;
            OfflineHeader.Text = "DESCONECTADOS · " + offlineFriends.Count;

            if (!social.IsLinked)
            {
                FriendsEmptyPanel.Visibility = Visibility.Visible;
                return;
            }

            var empty = requests == 0 && onlineFriends.Count == 0 && offlineFriends.Count == 0;
            FriendsEmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (empty)
            {
                FriendsEmptyTitle.Text = "Todavía no tienes amigos en CB";
                FriendsEmptyText.Text = "Escribe el handle de alguien arriba y pulsa AGREGAR para enviarle una solicitud.";
            }
        }

        private async void RefreshFriends_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(service.LauncherPath) && !social.IsLinked)
            {
                UpdateFriendsIdentity();
                return;
            }

            await LoadFriendsAsync();
        }

        private async void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            var handle = (FriendHandleBox.Text ?? "").Trim().TrimStart('@');
            if (handle.Length == 0)
            {
                SetStatus("Escribe el handle de CB de la persona.");
                return;
            }

            try
            {
                await social.AddFriendAsync(handle);
                FriendHandleBox.Text = string.Empty;
                SetStatus("Solicitud de amistad enviada a @" + handle + ".");
                await LoadFriendsAsync(true);
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
            }
        }

        private async void FriendHandle_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var handle = (FriendHandleBox.Text ?? "").Trim().TrimStart('@');
            if (handle.Length == 0) return;

            try
            {
                await social.AddFriendAsync(handle);
                FriendHandleBox.Text = string.Empty;
                SetStatus("Solicitud de amistad enviada a @" + handle + ".");
                await LoadFriendsAsync(true);
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
            }
        }

        private async void AcceptFriend_Click(object sender, RoutedEventArgs e)
        {
            await RunFriendActionAsync(sender, friend => social.AcceptFriendAsync(friend.CbId), "Solicitud aceptada.");
        }

        private async void DeclineFriend_Click(object sender, RoutedEventArgs e)
        {
            await RunFriendActionAsync(sender, friend => social.DeclineFriendAsync(friend.CbId), "Solicitud rechazada.");
        }

        private async void CancelFriend_Click(object sender, RoutedEventArgs e)
        {
            await RunFriendActionAsync(sender, friend => social.CancelFriendAsync(friend.CbId), "Solicitud cancelada.");
        }

        private async void RemoveFriend_Click(object sender, RoutedEventArgs e)
        {
            var friend = (sender as FrameworkElement)?.Tag as CbFriend;
            if (friend == null) return;

            var answer = api.Dialogs.ShowMessage("¿Quitar a " + friend.Name + " de tus amigos de CB?", "CB Servers", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                await social.RemoveFriendAsync(friend.CbId);
                SetStatus(friend.Name + " ya no está en tus amigos.");
                await LoadFriendsAsync(true);
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
            }
        }

        private async Task RunFriendActionAsync(object sender, Func<CbFriend, Task> action, string successMessage)
        {
            var friend = (sender as FrameworkElement)?.Tag as CbFriend;
            if (friend == null) return;

            try
            {
                await action(friend);
                SetStatus(successMessage + " · " + friend.Name);
                await LoadFriendsAsync(true);
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "CB Servers");
            }
        }

        private async void FriendsDownload_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsureLauncherReadyAsync()) return;

            Guard(() => service.OpenLauncher());
            SetStatus("CB Launcher abierto. Inicia sesión una vez y pulsa REINTENTAR.");
            social.ReloadIdentity();
            UpdateFriendsIdentity();
            FriendsDownloadButton.Content = "ABRIR CB";
        }

        private async void FriendsRetry_Click(object sender, RoutedEventArgs e)
        {
            social.ReloadIdentity();
            UpdateFriendsIdentity();

            if (!social.IsLinked)
            {
                api.Dialogs.ShowMessage(social.LinkError ?? "Todavía no hay una cuenta de CB en este PC.", "CB Servers");
                return;
            }

            await LoadFriendsAsync();
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
            FriendsTab.Visibility = tag == "friends" ? Visibility.Visible : Visibility.Collapsed;
            SettingsTab.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

            TabGames.IsChecked = tag == "games";
            TabServers.IsChecked = tag == "servers";
            TabFriends.IsChecked = tag == "friends";
            TabSettings.IsChecked = tag == "settings";

            if (tag == "servers") EnsureServerSelection();
            if (tag == "settings") UpdateSettingsValues(service.ReadProperties());
            if (tag == "friends")
            {
                var ignored = LoadFriendsAsync();
            }
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

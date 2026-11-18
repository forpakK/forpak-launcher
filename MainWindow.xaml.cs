using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ForpakLauncher.Core;
using Microsoft.Win32;

namespace ForpakLauncher
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient _http;
        private VeloxApi _velox;
        private readonly Settings _settings;
        private readonly GameManager _game;
        private readonly ModManager _mods;
        private readonly Updater _updater;
        private readonly CurseForge _cf;
        private readonly Modpacks _packs;
        private readonly DiscordPresence _discord = new();
        private string _joinServer;
        private string _remoteVersion;
        private List<McVersion> _allVersions = new();
        private McAccount _account;
        private CancellationTokenSource _loginCts;
        private string _pendingCode;
        private bool _busy;
        private bool _loading = true;
        private bool _gameRunning;
        private bool _bgRunning;
        private readonly Random _rng = new();

        public MainWindow()
        {
            InitializeComponent();

            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("VeloxClient/" + Updater.CurrentVersionText);
            _settings = Settings.Load();
            _game = new GameManager(_http)
            {
                Status = msg => Dispatcher.Invoke(() => StatusText.Text = msg),
                Progress = v => Dispatcher.Invoke(() => SetProgress(v))
            };
            _mods = new ModManager(_http);
            _updater = new Updater(_http);
            _cf = new CurseForge(_http) { ApiKey = _settings.CurseForgeApiKey };
            _packs = new Modpacks(_http);
            _velox = new VeloxApi(_http);
            ApplyTheme(_settings.Theme);
            BuildThemePanel();
            BuildCapePanel();
            ForpakLauncher.Core.VeloxApi.WriteShared(_settings);
            VersionLabel.Text = "v" + Updater.CurrentVersionText;

            FabricSwitch.IsChecked = _settings.UseFabric;
            PerfSwitch.IsChecked = _settings.PerformanceMode;
            ClientSwitch.IsChecked = _settings.ForpakClient;
            _loading = false;
            UpdateSubtitle();

            _discord.Enabled = _settings.DiscordStatus;
            Closed += (_, _) => { _discord.Clear(); Thread.Sleep(150); _discord.Dispose(); };
            Loaded += async (_, _) =>
            {
                _discord.Set("Dans le launcher", "Prépare sa partie", resetTimer: true);
                LoadLogo(ModrinthLogo, "modrinth.com");
                LoadLogo(CurseForgeLogo, "www.curseforge.com");
                CurseForgeLogo.Opacity = 0.45;
                if (_settings.AnimatedBackground) StartBackground();
                StartOnlineCounter();
                await InitAsync();
            };
            // Pas d'animation quand la fenêtre est réduite : on économise le CPU
            StateChanged += (_, _) =>
            {
                if (WindowState == WindowState.Minimized) StopBackground();
                else if (_settings.AnimatedBackground && !_gameRunning) StartBackground();
            };
        }

        // ------------------------------------------------------------- démarrage

        private async Task InitAsync()
        {
            try
            {
                _allVersions = await _game.GetVersionsAsync();
                RefreshVersionList();
                StatusText.Text = "Prêt.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Impossible de charger les versions (pas d'internet ?) : " + ex.Message;
            }

            // Mises à jour + nouveautés
            _remoteVersion = await _updater.GetRemoteVersionAsync();
            if (Updater.IsNewer(_remoteVersion))
            {
                UpdateButton.Content = "Mise à jour v" + _remoteVersion;
                UpdateButton.Visibility = Visibility.Visible;
            }
            var notes = await _updater.GetReleaseNotesAsync();
            if (!string.IsNullOrWhiteSpace(notes))
            {
                NewsTitle.Text = "Version " + (_remoteVersion ?? Updater.CurrentVersionText);
                NewsText.Text = notes.Trim();
            }

            // Compte enregistré : on reste connecté
            _account = _settings.LoadCachedAccount();
            UpdateAccountUi();
            if (_account != null && !_account.TokenValid(TimeSpan.FromHours(2)) && _account.RefreshToken != null)
                await EnsureFreshTokenAsync(required: false);
        }

        private void RefreshVersionList()
        {
            var previous = (VersionCombo.SelectedItem as McVersion)?.Id ?? _settings.LastVersion;
            if (string.IsNullOrEmpty(previous)) previous = "1.21.11";
            var prevPack = (VersionCombo.SelectedItem as McVersion)?.PackId ?? _settings.LastPack;
            var list = new List<McVersion>();
            foreach (var pack in _settings.Modpacks)
            {
                var basev = _allVersions.FirstOrDefault(v => v.Id == pack.McVersion);
                if (basev == null) continue;
                list.Add(new McVersion
                {
                    Id = basev.Id, Type = "modpack", Url = basev.Url, Sha1 = basev.Sha1,
                    PackId = pack.Id, PackName = pack.Name
                });
            }
            list.AddRange(_allVersions
                .Where(v => v.Type == "release" || (_settings.ShowSnapshots && v.Type == "snapshot")));
            VersionCombo.ItemsSource = list;
            VersionCombo.SelectedItem =
                (prevPack != null ? list.FirstOrDefault(v => v.PackId == prevPack) : null)
                ?? list.FirstOrDefault(v => v.PackId == null && v.Id == previous)
                ?? list.FirstOrDefault(v => v.PackId == null);
        }

        // ------------------------------------------------------------- logos Modrinth / CurseForge

        /// <summary>Charge l'icône officielle du site (favicon). Si ça rate, le point de couleur reste visible.</summary>
        private static void LoadLogo(Image target, string domain)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri($"https://{domain}/favicon.ico");
                img.DecodePixelWidth = 32;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.DownloadFailed += (_, _) => LoadLogoFallback(target, domain);
                img.DecodeFailed += (_, _) => LoadLogoFallback(target, domain);
                target.Source = img;
            }
            catch { LoadLogoFallback(target, domain); }
        }

        private static void LoadLogoFallback(Image target, string domain)
        {
            try { target.Source = new BitmapImage(new Uri($"https://www.google.com/s2/favicons?domain={domain}&sz=64")); }
            catch { target.Source = null; }
        }

        // ------------------------------------------------------------- fond animé

        private void StartBackground()
        {
            if (_bgRunning) return;
            _bgRunning = true;

            // Parallax : chaque couche défile à sa vitesse (boucle infinie)
            Scroll(StarsBrush, 400);
            Scroll(Layer1Brush, 240);
            Scroll(Layer2Brush, 150);
            Scroll(Layer3Brush, 90);
            Scroll(Layer4Brush, 50);

            // Aurores : elles ondulent dans des sens opposés et "respirent"
            Scroll(Aurora1Brush, 260);
            Scroll(Aurora2Brush, 380, reverse: true);
            Aurora1Rect.BeginAnimation(OpacityProperty, Slow(new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(4.5))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            }));
            Aurora2Rect.BeginAnimation(OpacityProperty, Slow(new DoubleAnimation(0.9, 0.35, TimeSpan.FromSeconds(6.5))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            }));

            // Étoiles qui scintillent
            StarsRect.BeginAnimation(OpacityProperty, Slow(new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(2.6))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }));

            // Logo qui flotte
            HeroFloat.BeginAnimation(TranslateTransform.YProperty, Slow(new DoubleAnimation(-5, 5, TimeSpan.FromSeconds(2.4))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            }));

            // Météo du fond : neige, pluie ou rien
            Particles.Children.Clear();
            double w = Math.Max(800, ActualWidth), h = Math.Max(500, ActualHeight);
            if (_settings.BgWeather == "rain")
            {
                for (int i = 0; i < 90; i++)
                {
                    double len = 10 + _rng.NextDouble() * 12;
                    var drop = new System.Windows.Shapes.Rectangle
                    {
                        Width = 1.5, Height = len,
                        Fill = new SolidColorBrush(Color.FromRgb(156, 200, 255)),
                        Opacity = 0.25 + _rng.NextDouble() * 0.35,
                        RenderTransform = new RotateTransform(12)
                    };
                    double x0 = _rng.NextDouble() * (w + 120);
                    Particles.Children.Add(drop);
                    var fallTime = TimeSpan.FromSeconds(0.9 + _rng.NextDouble() * 0.6);
                    var begin = TimeSpan.FromSeconds(-_rng.NextDouble() * 2);
                    drop.BeginAnimation(Canvas.TopProperty, Slow(new DoubleAnimation(-30, h + 30, fallTime)
                    { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin }));
                    drop.BeginAnimation(Canvas.LeftProperty, Slow(new DoubleAnimation(x0, x0 - (h + 60) * 0.21, fallTime)
                    { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin }));
                }
                return;
            }
            if (_settings.BgWeather == "none") return;
            for (int i = 0; i < 70; i++)
            {
                int size = _rng.Next(10) < 6 ? 2 : _rng.Next(10) < 7 ? 3 : 4;
                var flake = new System.Windows.Shapes.Rectangle
                {
                    Width = size, Height = size,
                    Fill = new SolidColorBrush(Color.FromRgb(235, 243, 255)),
                    Opacity = 0.35 + size * 0.15 + _rng.NextDouble() * 0.15
                };
                double x = _rng.NextDouble() * w;
                Canvas.SetLeft(flake, x);
                Particles.Children.Add(flake);

                var fall = TimeSpan.FromSeconds((size == 2 ? 22 : size == 3 ? 15 : 10) + _rng.NextDouble() * 6);
                flake.BeginAnimation(Canvas.TopProperty, Slow(new DoubleAnimation(-10, h + 10, fall)
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(-_rng.NextDouble() * fall.TotalSeconds)
                }));
                // Balancement gauche-droite
                var sway = TimeSpan.FromSeconds(2.5 + _rng.NextDouble() * 3);
                double amp = 6 + _rng.NextDouble() * 18;
                flake.BeginAnimation(Canvas.LeftProperty, Slow(new DoubleAnimation(x - amp, x + amp, sway)
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(-_rng.NextDouble() * sway.TotalSeconds),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                }));
            }
        }

        private void StopBackground()
        {
            if (!_bgRunning) return;
            _bgRunning = false;
            foreach (var b in new[] { StarsBrush, Aurora1Brush, Aurora2Brush, Layer1Brush, Layer2Brush, Layer3Brush, Layer4Brush })
                b.BeginAnimation(TileBrush.ViewportProperty, null);
            Aurora1Rect.BeginAnimation(OpacityProperty, null);
            Aurora2Rect.BeginAnimation(OpacityProperty, null);
            StarsRect.BeginAnimation(OpacityProperty, null);
            HeroFloat.BeginAnimation(TranslateTransform.YProperty, null);
            foreach (UIElement el in Particles.Children)
            {
                el.BeginAnimation(Canvas.TopProperty, null);
                el.BeginAnimation(Canvas.LeftProperty, null);
            }
            Particles.Children.Clear();
        }

        private static void Scroll(ImageBrush brush, double seconds, bool reverse = false)
        {
            var from = new Rect(reverse ? -1920 : 0, 0, 1920, 540);
            var to = new Rect(reverse ? 0 : -1920, 0, 1920, 540);
            var anim = new RectAnimation(from, to, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever };
            brush.BeginAnimation(TileBrush.ViewportProperty, Slow(anim));
        }

        /// <summary>30 images/s suffisent pour un fond : moins de CPU.</summary>
        private static T Slow<T>(T anim) where T : Timeline
        {
            Timeline.SetDesiredFrameRate(anim, 30);
            return anim;
        }

        // ------------------------------------------------------------- navigation

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (HomePage == null || SettingsPage == null) return; // pendant InitializeComponent
            var pages = new (RadioButton nav, FrameworkElement page)[]
            {
                (NavHome, HomePage), (NavMods, ModsPage), (NavPacks, PacksPage),
                (NavServers, ServersPage), (NavShots, ShotsPage), (NavFriends, FriendsPage), (NavSkin, SkinPage), (NavLogs, LogsPage), (NavSettings, SettingsPage)
            };
            FrameworkElement shown = HomePage;
            foreach (var (nav, page) in pages)
            {
                bool on = nav.IsChecked == true;
                page.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) shown = page;
            }
            Animate(shown);

            if (NavMods.IsChecked == true) _ = OpenModsPageAsync();
            if (NavPacks.IsChecked == true) _ = OpenPacksPageAsync();
            if (NavServers.IsChecked == true) _ = RefreshServersAsync();
            if (NavShots.IsChecked == true) LoadShots();
            if (NavLogs.IsChecked == true) LoadLogs();
            if (NavSkin.IsChecked == true) LoadSkinPage();
            if (NavFriends.IsChecked == true) _ = RefreshFriendsAsync();
            if (NavSettings.IsChecked == true) LoadSettingsPage();
        }

        private static void Animate(FrameworkElement el)
        {
            var tt = new TranslateTransform(0, 10);
            el.RenderTransform = tt;
            var dur = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
        }

        private void GoHome() => NavHome.IsChecked = true;

        // ------------------------------------------------------------- interrupteurs de l'accueil

        private void Fabric_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (FabricSwitch.IsChecked != true)
            {
                // Sans Fabric, pas de mods
                PerfSwitch.IsChecked = false;
                ClientSwitch.IsChecked = false;
            }
            SaveHomeSwitches();
        }

        private void Perf_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (PerfSwitch.IsChecked == true) FabricSwitch.IsChecked = true;
            SaveHomeSwitches();
        }

        private void Client_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (ClientSwitch.IsChecked == true) FabricSwitch.IsChecked = true;
            SaveHomeSwitches();
        }

        private void SaveHomeSwitches()
        {
            _settings.UseFabric = FabricSwitch.IsChecked == true;
            _settings.PerformanceMode = PerfSwitch.IsChecked == true;
            _settings.ForpakClient = ClientSwitch.IsChecked == true;
            _settings.Save();
            UpdateSubtitle();
        }

        private void UpdateSubtitle()
        {
            if (SubtitleText == null) return;
            if (FabricSwitch.IsChecked != true) SubtitleText.Text = "Minecraft vanilla, sans mods.";
            else if (PerfSwitch.IsChecked == true && ClientSwitch.IsChecked == true)
                SubtitleText.Text = "Pack FPS + modules PvP activés · Shift droit en jeu pour le menu.";
            else if (PerfSwitch.IsChecked == true) SubtitleText.Text = "Pack FPS activé : Sodium, Lithium et compagnie.";
            else SubtitleText.Text = "Fabric activé : ajoute tes mods dans l'onglet Mods.";
        }

        // ------------------------------------------------------------- compte

        private async void Account_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_account != null)
            {
                var r = MessageBox.Show($"Se déconnecter de {_account.Name} ?", "Compte",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes)
                {
                    _account = null;
                    _settings.SaveAccount(null);
                    UpdateAccountUi();
                }
                return;
            }

            var auth = new MicrosoftAuth(_http, _settings.ClientId);
            try
            {
                var code = await auth.StartDeviceCodeAsync();
                _pendingCode = code.UserCode;
                LoginCode.Text = code.UserCode;
                LoginUrl.Text = code.VerificationUri;
                LoginOverlay.Visibility = Visibility.Visible;
                Animate(LoginOverlay);
                try { Clipboard.SetText(code.UserCode); } catch { }
                OpenUrl(code.VerificationUri);

                _loginCts = new CancellationTokenSource();
                _account = await auth.WaitForDeviceCodeAsync(code, _loginCts.Token);
                SaveAccount();
                StatusText.Text = "Connecté en tant que " + _account.Name + ".";
            }
            catch (OperationCanceledException) { StatusText.Text = "Connexion annulée."; }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Connexion", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                LoginOverlay.Visibility = Visibility.Collapsed;
                UpdateAccountUi();
            }
        }

        private void SaveAccount() => _settings.SaveAccount(_account);

        /// <summary>
        /// Rafraîchit le jeton si besoin. Une panne réseau ne déconnecte pas : on garde le compte.
        /// Renvoie false seulement si on ne peut vraiment pas jouer (jeton expiré et impossible à renouveler).
        /// </summary>
        private async Task<bool> EnsureFreshTokenAsync(bool required)
        {
            if (_account == null) return false;
            if (_account.TokenValid(TimeSpan.FromMinutes(required ? 15 : 120))) return true;
            if (_account.RefreshToken == null) return _account.TokenValid(TimeSpan.Zero);
            try
            {
                var fresh = await new MicrosoftAuth(_http, _settings.ClientId).RefreshAsync(_account.RefreshToken);
                _account = fresh;
                SaveAccount();
                UpdateAccountUi();
                return true;
            }
            catch (SessionExpiredException ex)
            {
                // Vraiment expiré (mot de passe changé, accès retiré...) : il faut se reconnecter
                _account = null;
                _settings.SaveAccount(null);
                UpdateAccountUi();
                StatusText.Text = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                Log.Write("Rafraîchissement du compte : " + ex.Message);
                if (!required) return true;
                bool ok = _account.TokenValid(TimeSpan.Zero);
                if (!ok) StatusText.Text = "Impossible de joindre Microsoft : " + ex.Message;
                return ok;
            }
        }

        private void UpdateAccountUi()
        {
            if (_account != null) SetAccountUi(_account.Name, _account.Uuid, "Connecté");
            else SetAccountUi(null, null, "Compte Microsoft");
        }

        private void SetAccountUi(string name, string uuid, string hint)
        {
            AccountName.Text = string.IsNullOrEmpty(name) ? "Se connecter" : name;
            AccountHint.Text = hint;
            AvatarLetter.Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            AvatarImage.Source = null;
            if (!string.IsNullOrEmpty(uuid))
            {
                try { AvatarImage.Source = new BitmapImage(new Uri($"https://mc-heads.net/avatar/{uuid}/64")); }
                catch { }
            }
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            try { if (_pendingCode != null) Clipboard.SetText(_pendingCode); } catch { }
        }

        private void CancelLogin_Click(object sender, RoutedEventArgs e) => _loginCts?.Cancel();

        // ------------------------------------------------------------- jouer

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_account == null) { Account_Click(sender, e); return; }
            if (VersionCombo.SelectedItem is not McVersion version) return;

            SetBusy(true);
            _settings.LastVersion = version.Id;
            _settings.LastPack = version.PackId;
            SaveHomeSwitches();
            var pack = version.PackId == null ? null : _settings.Modpacks.FirstOrDefault(m => m.Id == version.PackId);
            var fabric = pack != null || _settings.UseFabric;
            var joinServer = _joinServer;
            _joinServer = null;

            try
            {
                // Jeton Minecraft encore bon ? sinon on le renouvelle
                if (!await EnsureFreshTokenAsync(required: true))
                {
                    if (_account == null) MessageBox.Show("Ta session a expiré, reconnecte-toi.", "Compte",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var ct = CancellationToken.None;
                var launchId = await Task.Run(() => _game.InstallAsync(version, fabric, ct, pack?.LoaderVersion));
                Action<string> status = msg => Dispatcher.Invoke(() => StatusText.Text = msg);

                if (pack != null)
                {
                    // Modpack : on ne touche pas à ses mods, on ajoute juste les modules Velox si possible
                    if (_settings.ForpakClient && ModManager.SupportsForpakClient(version.Id))
                    {
                        try
                        {
                            var packMods = Path.Combine(pack.Dir, "mods");
                            await Task.Run(() => _mods.EnsureForpakClientAsync(version.Id, _remoteVersion, status, packMods));
                        }
                        catch (Exception ex) { Log.Write("Velox dans le modpack : " + ex.Message); }
                    }
                }
                else if (fabric)
                {
                    SetProgress(-1);
                    // Pack FPS
                    if (_settings.PerformanceMode)
                        await Task.Run(() => _mods.InstallPerformancePackAsync(version.Id, status));
                    else
                        _mods.RemovePerformancePack(version.Id);

                    // Modules PvP (Forpak Client)
                    if (ModManager.SupportsForpakClient(version.Id))
                    {
                        if (_settings.ForpakClient)
                        {
                            try { await Task.Run(() => _mods.EnsureForpakClientAsync(version.Id, _remoteVersion, status)); }
                            catch (Exception ex)
                            {
                                Log.Write("Forpak Client : " + ex.Message);
                                StatusText.Text = "Modules Velox indisponibles, lancement sans eux.";
                            }
                        }
                        else ModManager.RemoveForpakClient(version.Id);
                    }
                }

                var java = string.IsNullOrWhiteSpace(_settings.JavaPath)
                    ? await Task.Run(() => _game.EnsureJavaAsync(launchId, ct))
                    : _settings.JavaPath;

                StatusText.Text = "Lancement de Minecraft " + launchId + "...";
                SetProgress(-1);
                var gameDir = pack != null ? pack.Dir : ModManager.GameDir(version.Id, fabric);
                var launchStart = DateTime.Now;
                var proc = _game.Launch(launchId, _account, _settings, java, gameDir, joinServer);
                _gameRunning = true;
                _discord.Set("Joue à Minecraft " + version.Id,
                    joinServer != null ? (_settings.StreamerMode ? "Sur un serveur" : "Sur " + joinServer)
                    : pack != null ? "Modpack " + pack.Name
                    : fabric && _settings.ForpakClient ? "Avec les modules PvP Velox" : "Minecraft Java",
                    resetTimer: true);
                StopBackground(); // le launcher ne consomme rien pendant que tu joues
                proc.Exited += (_, _) => Dispatcher.Invoke(() =>
                {
                    _gameRunning = false;
                    _discord.Set("Dans le launcher", "Vient de finir une partie", resetTimer: true);
                    if (_settings.AnimatedBackground) StartBackground();
                    StatusText.Text = proc.ExitCode == 0 ? "Partie terminée." :
                        $"Le jeu s'est arrêté (code {proc.ExitCode}). Détails : {Paths.LogFile}";
                    if (!IsVisible) { Show(); Activate(); }
                    if (proc.ExitCode != 0) OfferCrashReport(gameDir, launchStart);
                });

                await Task.Delay(2500);
                StatusText.Text = "Minecraft est lancé. Bon jeu !";
                if (_settings.CloseOnLaunch) Hide();
            }
            catch (Exception ex)
            {
                Log.Write("ERREUR : " + ex);
                StatusText.Text = "Erreur : " + ex.Message;
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            PlayButton.IsEnabled = !busy;
            VersionCombo.IsEnabled = !busy;
            FabricSwitch.IsEnabled = !busy;
            PerfSwitch.IsEnabled = !busy;
            ClientSwitch.IsEnabled = !busy;
            AccountButton.IsEnabled = !busy;
            NavMods.IsEnabled = !busy;
            NavSettings.IsEnabled = !busy;
            PlayButton.Content = busy ? "..." : "JOUER";
            if (!busy) { Bar.Visibility = Visibility.Hidden; PercentText.Text = ""; }
        }

        private void SetProgress(double v)
        {
            Bar.Visibility = Visibility.Visible;
            if (v < 0) { Bar.IsIndeterminate = true; PercentText.Text = ""; return; }
            Bar.IsIndeterminate = false;
            Bar.Value = v;
            PercentText.Text = $"{v * 100:0}%";
        }

        // ------------------------------------------------------------- paramètres

        private void LoadSettingsPage()
        {
            RamSlider.Value = _settings.RamMb;
            RamText.Text = $"{_settings.RamMb / 1024.0:0.#} Go";
            JavaBox.Text = _settings.JavaPath;
            JvmBox.Text = _settings.ExtraJvmArgs;
            SnapshotSwitch.IsChecked = _settings.ShowSnapshots;
            CloseSwitch.IsChecked = _settings.CloseOnLaunch;
            ForpakClientSwitch.IsChecked = _settings.ForpakClient;
            PerfSettingSwitch.IsChecked = _settings.PerformanceMode;
            AnimSwitch.IsChecked = _settings.AnimatedBackground;
            WeatherSnow.IsChecked = _settings.BgWeather == "snow";
            WeatherRain.IsChecked = _settings.BgWeather == "rain";
            WeatherNone.IsChecked = _settings.BgWeather == "none";
            DiscordSwitch.IsChecked = _settings.DiscordStatus;
            BoostSwitch.IsChecked = _settings.LauncherBoost;
            StreamerSwitch.IsChecked = _settings.StreamerMode;
            CfKeyBox.Text = _settings.CurseForgeApiKey;
            CfKeyHint.Text = CurseForge.BuiltInKey != null
                ? "Une clé est déjà intégrée au launcher : laisse vide, ou mets la tienne pour la remplacer."
                : "À créer sur console.curseforge.com. Sans clé, seul Modrinth marche.";
            SavedText.Visibility = Visibility.Collapsed;
        }

        private void BgWeather_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || sender is not RadioButton rb) return;
            var mode = (string)rb.Tag;
            if (_settings.BgWeather == mode) return;
            _settings.BgWeather = mode;
            _settings.Save();
            // On relance le fond pour voir le changement tout de suite
            if (_bgRunning) { StopBackground(); StartBackground(); }
        }

        private void Ram_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RamText != null) RamText.Text = $"{e.NewValue / 1024.0:0.#} Go";
        }

        private void BrowseJava_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Java (javaw.exe)|javaw.exe;java.exe", Title = "Choisir Java" };
            if (dlg.ShowDialog() == true) JavaBox.Text = dlg.FileName;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings.RamMb = (int)RamSlider.Value;
            _settings.JavaPath = JavaBox.Text.Trim();
            _settings.ExtraJvmArgs = JvmBox.Text.Trim();
            _settings.ShowSnapshots = SnapshotSwitch.IsChecked == true;
            _settings.CloseOnLaunch = CloseSwitch.IsChecked == true;
            _settings.AnimatedBackground = AnimSwitch.IsChecked == true;
            _settings.DiscordStatus = DiscordSwitch.IsChecked == true;
            _settings.LauncherBoost = BoostSwitch.IsChecked == true;
            _settings.StreamerMode = StreamerSwitch.IsChecked == true;
            ForpakLauncher.Core.VeloxApi.WriteShared(_settings);
            _discord.Enabled = _settings.DiscordStatus;
            if (_discord.Enabled) _discord.Set("Dans le launcher", "Prépare sa partie"); else _discord.Clear();
            if (_settings.CurseForgeApiKey != CfKeyBox.Text.Trim())
            {
                _settings.CurseForgeApiKey = CfKeyBox.Text.Trim();
                _cf.ApiKey = _settings.CurseForgeApiKey;
                _chipsFor = null;         // recharge les catégories CurseForge
                _modsLoadedFor = null;
            }
            _settings.Save();
            if (_settings.AnimatedBackground && !_gameRunning) StartBackground(); else StopBackground();

            // Les interrupteurs de l'accueil suivent
            ClientSwitch.IsChecked = ForpakClientSwitch.IsChecked == true;
            PerfSwitch.IsChecked = PerfSettingSwitch.IsChecked == true;
            SaveHomeSwitches();

            RefreshVersionList();
            SavedText.Visibility = Visibility.Visible;
            Animate(SavedText);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Paths.Root);
            OpenUrl(Paths.Root);
        }

        private void OpenMods_Click(object sender, RoutedEventArgs e)
        {
            if (VersionCombo.SelectedItem is not McVersion v) return;
            var mods = ModManager.ModsDir(v.Id);
            Directory.CreateDirectory(mods);
            OpenUrl(mods);
        }

        // ------------------------------------------------------------- mods (pages façon Modrinth)

        private string ModsMc => (VersionCombo.SelectedItem as McVersion)?.Id;
        private string _modSort = "relevance";
        private string _modCategory = "";
        private int _modPage;
        private int _modTotal;
        private string _modQuery = "";
        private string _modsLoadedFor;
        private string _chipsFor;
        private string _modSource = "modrinth";
        private const string Curated = "__curated";
        private bool IsCf => _modSource == "curseforge";
        private ModHit _detailHit;
        private ModProject _detailProject;

        private async Task BuildCategoryChipsAsync()
        {
            if (_chipsFor == _modSource) return;
            _chipsFor = _modSource;
            CategoryChips.Children.Clear();
            _modCategory = IsCf ? "" : Curated;

            var cats = new List<(string Id, string Name)>();
            if (IsCf)
            {
                cats.Add(("", "Tout"));
                if (_cf.HasKey)
                {
                    try { cats.AddRange((await _cf.GetCategoriesAsync()).Take(14)); } catch { }
                }
            }
            else
            {
                cats.Add((Curated, "★ Sélection Velox"));
                cats.AddRange(ModManager.Categories.Select(c => (c.Slug, c.Name)));
            }

            foreach (var (id, name) in cats)
            {
                var rb = new RadioButton
                {
                    Content = name, Tag = id, GroupName = "modcat",
                    Style = (Style)FindResource("Pill")
                };
                CategoryChips.Children.Add(rb);
                rb.IsChecked = id == _modCategory;   // avant l'événement : pas de chargement en double
                rb.Checked += async (s2, _) =>
                {
                    _modCategory = (string)((RadioButton)s2).Tag;
                    _modPage = 0;
                    await LoadModsAsync();
                };
            }
        }

        private async void Source_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || ModrinthLabel == null) return; // pendant InitializeComponent
            bool cf = SourceSwitch.IsChecked == true;
            _modSource = cf ? "curseforge" : "modrinth";
            ModrinthLabel.Foreground = (Brush)FindResource(cf ? "Muted" : "Text");
            CurseForgeLabel.Foreground = (Brush)FindResource(cf ? "Text" : "Muted");
            ModrinthLogo.Opacity = cf ? 0.45 : 1;
            CurseForgeLogo.Opacity = cf ? 1 : 0.45;
            _modsLoadedFor = null;
            ShowBrowse();
            await OpenModsPageAsync();
        }

        private void ModrinthSide_Click(object sender, MouseButtonEventArgs e) => SourceSwitch.IsChecked = false;
        private void CurseForgeSide_Click(object sender, MouseButtonEventArgs e) => SourceSwitch.IsChecked = true;

        private async Task OpenModsPageAsync()
        {
            if (ModsMc == null) return;
            if (FabricSwitch.IsChecked != true) FabricSwitch.IsChecked = true; // les mods ont besoin de Fabric
            await BuildCategoryChipsAsync();
            ModsVersionText.Text = "Fabric " + ModsMc;
            ShowBrowse();
            RefreshInstalled();
            if (_modsLoadedFor != ModsMc + _modSource) { _modPage = 0; await LoadModsAsync(); }
        }

        private int _loadId;

        private async Task LoadModsAsync()
        {
            var mc = ModsMc;
            if (mc == null) return;
            int myLoad = ++_loadId;   // si une autre recherche part entre-temps, celle-ci est ignorée
            ModsEmptyText.Text = "Chargement...";
            CfKeyPanel.Visibility = Visibility.Collapsed;
            ModResults.ItemsSource = null;
            PrevPageButton.IsEnabled = NextPageButton.IsEnabled = false;

            if (IsCf && !_cf.HasKey)
            {
                ModsEmptyText.Text = "CurseForge n'est pas encore activé dans cette version du launcher.\n" +
                    "Mets une clé dans les paramètres, ou attends la prochaine mise à jour.";
                CfKeyPanel.Visibility = Visibility.Visible;
                PageText.Text = "Page 1";
                return;
            }
            try
            {
                List<ModHit> hits;
                int total;
                if (IsCf)
                    (hits, total) = await _cf.SearchAsync(_modQuery, mc, _modSort, _modCategory, _modPage * ModManager.PageSize);
                else if (_modCategory == Curated && string.IsNullOrEmpty(_modQuery))
                {
                    hits = await _mods.GetCuratedAsync(mc);
                    total = hits.Count;
                }
                else
                {
                    var cat = _modCategory == Curated ? "" : _modCategory;
                    (hits, total) = await _mods.SearchAsync(_modQuery, mc, _modSort, cat, _modPage * ModManager.PageSize);
                }
                if (myLoad != _loadId) return;
                foreach (var h in hits)
                    if (_mods.IsInstalled(h.IndexKey, mc)) h.InstallLabel = "Installé ✓";
                _modTotal = total;
                _modsLoadedFor = mc + _modSource;
                ModResults.ItemsSource = hits;
                ModScroll.ScrollToTop();
                ModsEmptyText.Text = hits.Count == 0 ? "Aucun mod trouvé pour " + mc + "." : "";

                int pages = Math.Max(1, (int)Math.Ceiling(total / (double)ModManager.PageSize));
                PageText.Text = $"Page {_modPage + 1} sur {pages}";
                PrevPageButton.IsEnabled = _modPage > 0;
                NextPageButton.IsEnabled = _modPage + 1 < pages;
            }
            catch (Exception ex)
            {
                ModsEmptyText.Text = (IsCf ? "CurseForge" : "Modrinth") + " ne répond pas : " + ex.Message;
            }
        }

        private async void ModSearch_Click(object sender, RoutedEventArgs e) => await NewSearchAsync();

        private async void ModSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await NewSearchAsync();
        }

        private async Task NewSearchAsync()
        {
            _modQuery = ModSearchBox.Text.Trim();
            _modPage = 0;
            ModsTabBrowse.IsChecked = true;
            await LoadModsAsync();
        }

        private async void Sort_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
            _modSort = (string)item.Tag;
            if (_modsLoadedFor == null) return;
            _modSort = (string)item.Tag;
            _modPage = 0;
            await LoadModsAsync();
        }

        private async void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_modPage == 0) return;
            _modPage--;
            await LoadModsAsync();
        }

        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            _modPage++;
            await LoadModsAsync();
        }

        private void ModsTab_Checked(object sender, RoutedEventArgs e)
        {
            if (InstalledView == null) return; // pendant InitializeComponent
            bool installed = ModsTabInstalled.IsChecked == true;
            InstalledView.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            ModScroll.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            Pager.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            SearchRow.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            CategoryChips.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            ModsEmptyText.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            if (installed) RefreshInstalled();
            Animate(installed ? InstalledView : ModScroll);
        }

        private async void InstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ModHit hit || ModsMc == null) return;
            await InstallHitAsync(hit, b);
        }

        private async Task InstallHitAsync(ModHit hit, Button b)
        {
            if (hit.InstallLabel != "Installer") return;
            b.IsEnabled = false;
            b.Content = "...";
            try
            {
                var mc = ModsMc;
                if (hit.Source == "curseforge")
                    await Task.Run(() => _cf.InstallAsync(hit.ProjectId, mc, _mods));
                else
                    await Task.Run(() => _mods.InstallAsync(hit.ProjectId, mc));
                hit.InstallLabel = "Installé ✓";
                b.Content = hit.InstallLabel;
                RefreshInstalled();
            }
            catch (Exception ex)
            {
                b.Content = "Installer";
                MessageBox.Show(ex.Message, "Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { b.IsEnabled = true; }
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not InstalledMod mod || ModsMc == null) return;
            try { _mods.Remove(ModsMc, mod); } catch (Exception ex) { MessageBox.Show(ex.Message); }
            RefreshInstalled();
            _modsLoadedFor = null; // les boutons "Installé" seront recalculés
        }

        private void RefreshInstalled()
        {
            if (ModsMc == null) return;
            var list = _mods.GetInstalled(ModsMc);
            InstalledList.ItemsSource = list;
            InstalledCount.Text = list.Count + (list.Count > 1 ? " mods installés" : " mod installé") + " · " + ModsMc;
            ModsTabInstalled.Content = $"Installés ({list.Count})";
        }

        // ---- page d'un mod

        private void ShowBrowse()
        {
            DetailView.Visibility = Visibility.Collapsed;
            BrowseView.Visibility = Visibility.Visible;
        }

        private async void ModCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not ModHit hit) return;
            await OpenProjectAsync(hit);
        }

        private void DetailBack_Click(object sender, RoutedEventArgs e)
        {
            ShowBrowse();
            Animate(BrowseView);
        }

        private async Task OpenProjectAsync(ModHit hit)
        {
            _detailHit = hit;
            _detailProject = null;
            BrowseView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Visible;
            Animate(DetailView);

            // Ce qu'on connaît déjà (affichage immédiat)
            DetailTitle.Text = hit.Title;
            DetailDesc.Text = hit.Description;
            DetailIcon.Source = hit.IconUrl != null ? new BitmapImage(new Uri(hit.IconUrl)) : null;
            DetailInstall.Content = hit.InstallLabel;
            DetailWebButton.Content = hit.Source == "curseforge" ? "Voir sur CurseForge" : "Voir sur Modrinth";
            DetailStats.Children.Clear();
            DetailGallery.Children.Clear();
            DetailInfo.Children.Clear();
            DetailLinks.Children.Clear();
            DetailVersions.ItemsSource = null;
            DetailBody.Children.Clear();
            DetailBody.Children.Add(new TextBlock { Text = "Chargement de la description...", Foreground = (Brush)FindResource("Muted") });
            DetailScroll.ScrollToTop();

            try
            {
                var mc = ModsMc;
                bool cf = hit.Source == "curseforge";
                var projectTask = cf ? _cf.GetProjectAsync(hit.ProjectId) : _mods.GetProjectAsync(hit.ProjectId);
                var versionsTask = cf ? _cf.GetVersionsAsync(hit.ProjectId, mc) : _mods.GetVersionsAsync(hit.ProjectId, mc);
                var p = await projectTask;
                if (_detailHit != hit) return; // l'utilisateur a changé de mod entre-temps
                _detailProject = p;

                AddStat("", p.Downloads + " téléchargements");
                AddStat("", p.Followers + " abonnés");
                AddStat("", "Mis à jour " + p.Updated);

                foreach (var url in p.Gallery)
                {
                    var img = new Image { Source = new BitmapImage(new Uri(url)), Stretch = Stretch.UniformToFill };
                    DetailGallery.Children.Add(new Border
                    {
                        Width = 220, Height = 124, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 10, 10),
                        Background = (Brush)FindResource("Stroke"), ClipToBounds = true, Child = img
                    });
                }

                Markdown.Render(DetailBody, p.Body);

                AddInfo("Auteur", hit.Author);
                AddInfo("Licence", p.License);
                AddInfo("Publié", p.Published);
                AddInfo("Catégories", string.Join(", ", p.Tags));
                AddLink("Code source", p.SourceUrl);
                AddLink("Wiki", p.WikiUrl);
                AddLink("Discord", p.DiscordUrl);
                AddLink("Signaler un bug", p.IssuesUrl);

                var versions = await versionsTask;
                if (_detailHit == hit) DetailVersions.ItemsSource = versions;
            }
            catch (Exception ex)
            {
                DetailBody.Children.Clear();
                DetailBody.Children.Add(new TextBlock { Text = "Impossible de charger ce mod : " + ex.Message,
                    Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            }
        }

        private void AddStat(string icon, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Foreground = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Margin = new Thickness(6, 0, 0, 0), FontWeight = FontWeights.SemiBold });
            DetailStats.Children.Add(sp);
        }

        private void AddInfo(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            g.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)FindResource("Muted") });
            g.Children.Add(new TextBlock
            {
                Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(80, 0, 0, 0)
            });
            DetailInfo.Children.Add(g);
        }

        private void AddLink(string label, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var b = new Button
            {
                Content = label, Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 6), FontSize = 11.5, Tag = url
            };
            b.Click += (_, _) => OpenUrl(url);
            DetailLinks.Children.Add(b);
        }

        private async void DetailInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_detailHit == null) return;
            await InstallHitAsync(_detailHit, DetailInstall);
        }

        private void DetailWeb_Click(object sender, RoutedEventArgs e)
        {
            if (_detailHit?.Source == "curseforge")
            {
                var web = _detailProject?.WebUrl ?? _detailHit.WebUrl;
                if (web == null && _detailHit.Slug != null) web = "https://www.curseforge.com/minecraft/mc-mods/" + _detailHit.Slug;
                if (web != null) OpenUrl(web);
                return;
            }
            var slug = _detailProject?.Slug ?? _detailHit?.Slug ?? _detailHit?.ProjectId;
            if (slug != null) OpenUrl("https://modrinth.com/mod/" + slug);
        }

        private void CfKeyGoSettings_Click(object sender, RoutedEventArgs e)
        {
            NavSettings.IsChecked = true;
            CfKeyBox.Focus();
        }

        private void CfKeyWeb_Click(object sender, RoutedEventArgs e) => OpenUrl("https://console.curseforge.com/");

        // ------------------------------------------------------------- modpacks

        private int _packPage;
        private string _packQuery = "";
        private bool _packsLoaded;
        private bool _packBusy;

        private async Task OpenPacksPageAsync()
        {
            RefreshInstalledPacks();
            if (!_packsLoaded) await LoadPacksAsync();
        }

        private async Task LoadPacksAsync()
        {
            PacksEmptyText.Text = "Chargement...";
            PackResults.ItemsSource = null;
            PackPrevButton.IsEnabled = PackNextButton.IsEnabled = false;
            try
            {
                var (hits, total) = await _packs.SearchAsync(_packQuery, _packPage * ModManager.PageSize);
                foreach (var h in hits)
                    if (_settings.Modpacks.Any(m => m.Id == SafePackId(h))) h.InstallLabel = "Installé ✓";
                PackResults.ItemsSource = hits;
                PackScroll.ScrollToTop();
                _packsLoaded = true;
                PacksEmptyText.Text = hits.Count == 0 ? "Aucun modpack trouvé." : "";
                int pages = Math.Max(1, (int)Math.Ceiling(total / (double)ModManager.PageSize));
                PackPageText.Text = $"Page {_packPage + 1} sur {pages}";
                PackPrevButton.IsEnabled = _packPage > 0;
                PackNextButton.IsEnabled = _packPage + 1 < pages;
            }
            catch (Exception ex) { PacksEmptyText.Text = "Modrinth ne répond pas : " + ex.Message; }
        }

        private static string SafePackId(ModHit h) => (h.Slug ?? h.ProjectId ?? "").ToLowerInvariant();

        private async void PackSearch_Click(object sender, RoutedEventArgs e) => await NewPackSearchAsync();

        private async void PackSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await NewPackSearchAsync();
        }

        private async Task NewPackSearchAsync()
        {
            _packQuery = PackSearchBox.Text.Trim();
            _packPage = 0;
            PacksTabBrowse.IsChecked = true;
            await LoadPacksAsync();
        }

        private async void PackPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_packPage == 0) return;
            _packPage--;
            await LoadPacksAsync();
        }

        private async void PackNext_Click(object sender, RoutedEventArgs e)
        {
            _packPage++;
            await LoadPacksAsync();
        }

        private void PacksTab_Checked(object sender, RoutedEventArgs e)
        {
            if (InstalledPacksView == null) return;
            bool installed = PacksTabInstalled.IsChecked == true;
            InstalledPacksView.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            PackScroll.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            PackPager.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            PackSearchRow.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            if (installed) RefreshInstalledPacks();
            else PacksEmptyText.Text = PackResults.Items.Count == 0 ? PacksEmptyText.Text : "";
            Animate(installed ? InstalledPacksView : PackScroll);
        }

        private void RefreshInstalledPacks()
        {
            InstalledPacks.ItemsSource = null;
            InstalledPacks.ItemsSource = _settings.Modpacks.ToList();
            PacksTabInstalled.Content = $"Installés ({_settings.Modpacks.Count})";
            if (PacksTabInstalled.IsChecked == true)
                PacksEmptyText.Text = _settings.Modpacks.Count == 0
                    ? "Aucun modpack installé. Trouve-en un dans Découvrir, ou importe un fichier .mrpack." : "";
        }

        private void PackCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ModHit h && h.Slug != null)
                OpenUrl("https://modrinth.com/modpack/" + h.Slug);
        }

        private async void InstallPack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ModHit hit || hit.InstallLabel != "Installer le pack") return;
            if (_packBusy) { MessageBox.Show("Un modpack est déjà en cours d'installation."); return; }
            await InstallPackAsync(b, async (status, progress) => await _packs.InstallFromModrinthAsync(hit, status, progress), hit);
        }

        private async void ImportPack_Click(object sender, RoutedEventArgs e)
        {
            if (_packBusy) { MessageBox.Show("Un modpack est déjà en cours d'installation."); return; }
            var dlg = new OpenFileDialog { Filter = "Modpack Modrinth (*.mrpack)|*.mrpack", Title = "Importer un modpack" };
            if (dlg.ShowDialog() != true) return;
            var file = dlg.FileName;
            await InstallPackAsync(null, async (status, progress) =>
                await _packs.ImportAsync(file, System.IO.Path.GetFileNameWithoutExtension(file), null, status, progress), null);
        }

        private async Task InstallPackAsync(Button b, Func<Action<string>, IProgress<(int, int)>, Task<ModpackInstance>> work, ModHit hit)
        {
            _packBusy = true;
            if (b != null) { b.IsEnabled = false; b.Content = "..."; }
            PackProgressRow.Visibility = Visibility.Visible;
            PackBar.IsIndeterminate = true;
            Action<string> status = msg => Dispatcher.Invoke(() => PackStatusText.Text = msg);
            var progress = new Progress<(int done, int total)>(p =>
            {
                PackBar.IsIndeterminate = false;
                PackBar.Value = p.total == 0 ? 1 : (double)p.done / p.total;
                PackStatusText.Text = $"Téléchargement des fichiers : {p.done} / {p.total}";
            });
            try
            {
                var pack = await Task.Run(() => work(status, progress));
                _settings.Modpacks.RemoveAll(m => m.Id == pack.Id);
                _settings.Modpacks.Add(pack);
                _settings.Save();
                if (hit != null) hit.InstallLabel = "Installé ✓";
                if (b != null) b.Content = "Installé ✓";
                RefreshVersionList();
                RefreshInstalledPacks();
                // On sélectionne directement le modpack dans l'accueil
                var item = (VersionCombo.ItemsSource as IEnumerable<McVersion>)?.FirstOrDefault(v => v.PackId == pack.Id);
                if (item != null) VersionCombo.SelectedItem = item;
                PackStatusText.Text = $"{pack.Name} est installé et sélectionné ! Va dans Accueil et clique sur JOUER.";
                if (pack.FailedFiles.Count > 0)
                    MessageBox.Show(
                        $"Le modpack est installé, mais {pack.FailedFiles.Count} fichier(s) n'ont pas pu être téléchargés :\n\n" +
                        string.Join("\n", pack.FailedFiles.Take(10)) +
                        "\n\nSouvent c'est l'antivirus qui bloque le fichier. Tu peux réessayer l'import plus tard " +
                        "(les fichiers déjà là ne sont pas retéléchargés).",
                        "Modpack", MessageBoxButton.OK, MessageBoxImage.Information);
                PackBar.IsIndeterminate = false;
                PackBar.Value = 1;
            }
            catch (Exception ex)
            {
                if (b != null) b.Content = "Installer le pack";
                PackProgressRow.Visibility = Visibility.Collapsed;
                Log.Write("Modpack : " + ex);
                MessageBox.Show(ex.Message, "Modpack", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _packBusy = false;
                if (b != null) b.IsEnabled = true;
            }
        }

        private void PlayPack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ModpackInstance pack) return;
            var item = (VersionCombo.ItemsSource as IEnumerable<McVersion>)?.FirstOrDefault(v => v.PackId == pack.Id);
            if (item == null) { MessageBox.Show("Version de Minecraft introuvable pour ce modpack."); return; }
            VersionCombo.SelectedItem = item;
            GoHome();
            Play_Click(sender, e);
        }

        private void PackFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ModpackInstance pack)
            {
                Directory.CreateDirectory(pack.Dir);
                OpenUrl(pack.Dir);
            }
        }

        private void DeletePack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ModpackInstance pack) return;
            var r = MessageBox.Show($"Supprimer le modpack « {pack.Name} » ?\nSes mondes et réglages seront effacés.",
                "Modpack", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            try { if (Directory.Exists(pack.Dir)) Directory.Delete(pack.Dir, true); }
            catch (Exception ex) { MessageBox.Show("Impossible de tout supprimer : " + ex.Message); }
            _settings.Modpacks.RemoveAll(m => m.Id == pack.Id);
            _settings.Save();
            RefreshVersionList();
            RefreshInstalledPacks();
            _packsLoaded = false;
        }

        // ------------------------------------------------------------- serveurs

        private async Task RefreshServersAsync()
        {
            var views = _settings.Servers.Select(s => new ServerView { Entry = s }).ToList();
            ServerList.ItemsSource = views;
            ServersEmptyText.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (views.Count == 0) return;

            await Task.WhenAll(views.Select(async v =>
            {
                try
                {
                    var st = await ServerPinger.PingAsync(v.Entry.Address);
                    v.Motd = string.IsNullOrWhiteSpace(st.Motd) ? st.Version : st.Motd;
                    v.PlayersText = $"{st.Players:N0} / {st.MaxPlayers:N0} joueurs".Replace(' ', ' ');
                    v.PingText = st.Ping + " ms";
                    v.StatusBrush = st.Ping < 80 ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
                        : st.Ping < 180 ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x3D))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
                    if (st.Favicon != null)
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.StreamSource = new MemoryStream(st.Favicon);
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.EndInit();
                        img.Freeze();
                        v.Favicon = img;
                    }
                }
                catch
                {
                    v.Motd = "Hors ligne ou injoignable";
                    v.PlayersText = "—";
                    v.PingText = "hors ligne";
                    v.StatusBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
                }
            }));
            if (ServerList.ItemsSource == views)
            {
                ServerList.ItemsSource = null;
                ServerList.ItemsSource = views;
            }
        }

        private async void RefreshServers_Click(object sender, RoutedEventArgs e) => await RefreshServersAsync();

        private async void AddServer_Click(object sender, RoutedEventArgs e) => await AddServerAsync();

        private async void ServerAddress_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await AddServerAsync();
        }

        private async Task AddServerAsync()
        {
            var address = ServerAddressBox.Text.Trim();
            if (address.Length == 0) return;
            var name = ServerNameBox.Text.Trim();
            if (name.Length == 0) name = address;
            _settings.Servers.Add(new ServerEntry { Name = name, Address = address });
            _settings.Save();
            ServerNameBox.Text = ServerAddressBox.Text = "";
            await RefreshServersAsync();
        }

        private async void RemoveServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ServerView v) return;
            _settings.Servers.Remove(v.Entry);
            _settings.Save();
            await RefreshServersAsync();
        }

        private void JoinServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ServerView v) return;
            _joinServer = v.Entry.Address;
            GoHome();
            StatusText.Text = "Lancement et connexion à " + v.Entry.Name + "...";
            Play_Click(sender, e);
        }

        // ------------------------------------------------------------- captures d'écran

        private void LoadShots()
        {
            var dirs = new List<string> { Path.Combine(Paths.Root, "screenshots") };
            var inst = Path.Combine(Paths.Root, "instances");
            if (Directory.Exists(inst))
                dirs.AddRange(Directory.GetDirectories(inst).Select(d => Path.Combine(d, "screenshots")));

            var files = dirs.Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.png"))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(120)
                .ToList();

            var views = new List<ShotView>();
            foreach (var f in files)
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.UriSource = new Uri(f.FullName);
                    img.DecodePixelWidth = 320;
                    img.CacheOption = BitmapCacheOption.OnLoad;   // ne bloque pas le fichier
                    img.EndInit();
                    img.Freeze();
                    views.Add(new ShotView { Path = f.FullName, Thumb = img, Label = f.LastWriteTime.ToString("dd/MM/yyyy HH:mm") });
                }
                catch { }
            }
            ShotsList.ItemsSource = views;
            ShotsCount.Text = views.Count + (views.Count > 1 ? " captures" : " capture");
            ShotsEmptyText.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshShots_Click(object sender, RoutedEventArgs e) => LoadShots();

        private void Shot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ShotView v) OpenUrl(v.Path);
        }

        private void Shot_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not ShotView v) return;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(v.Path);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                Clipboard.SetImage(img);
                StatusText.Text = "Capture copiée !";
                ShotsCount.Text = "Capture copiée dans le presse-papiers ✓";
            }
            catch { }
        }

        // ------------------------------------------------------------- joueurs Velox en ligne

        private const string VeloxApi = "https://velox.fforpakk.workers.dev";
        private System.Windows.Threading.DispatcherTimer _onlineTimer;

        private void StartOnlineCounter()
        {
            _onlineTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _onlineTimer.Tick += async (_, _) => { await RefreshOnlineAsync(); await RefreshFriendsAsync(); };
            _onlineTimer.Start();
            _ = RefreshOnlineAsync();
            _ = RefreshFriendsAsync();
            var fast = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            fast.Tick += async (_, _) =>
            {
                if (NavFriends.IsChecked == true && IsVisible) await RefreshFriendsAsync();
                await CheckInboxAsync();
            };
            fast.Start();
        }

        private async Task RefreshOnlineAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var json = await _http.GetStringAsync(VeloxApi + "/online", cts.Token);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                int n = doc.RootElement.GetProperty("users").GetArrayLength();
                OnlineText.Text = n == 0 ? "Aucun joueur Velox en jeu pour l'instant"
                                : n == 1 ? "1 joueur Velox en jeu" : $"{n} joueurs Velox en jeu";
                OnlinePill.Visibility = Visibility.Visible;
            }
            catch { OnlinePill.Visibility = Visibility.Collapsed; }
        }

        // ------------------------------------------------------------- thèmes et capes

        private static readonly (string id, string name, string accent, string dark, string g1, string g2)[] Themes =
        {
            ("aurore", "Aurore", "#3EE6A8", "#2BC48D", "#14A880", "#0C86AE"),
            ("violet", "Violet", "#B58CFF", "#9466F0", "#8B4FE0", "#5B3FD6"),
            ("rouge",  "Rouge",  "#FF6B7A", "#E5485A", "#E0404F", "#A8306A"),
            ("bleu",   "Bleu",   "#62BDFF", "#3F95E0", "#2F86E0", "#3550D0"),
            ("or",     "Or",     "#FFCB52", "#E6A92E", "#E0A12A", "#C9661C")
        };

        private static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);

        private static Color Lighter(Color c) =>
            Color.FromRgb((byte)Math.Min(255, c.R + 20), (byte)Math.Min(255, c.G + 20), (byte)Math.Min(255, c.B + 20));

        private void ApplyTheme(string id)
        {
            var t = Themes.FirstOrDefault(x => x.id == id);
            if (t.id == null) t = Themes[0];
            var res = Application.Current.Resources;
            void SetBrush(string key, Color c)
            {
                if (res[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
                else res[key] = new SolidColorBrush(c);
            }
            void SetGradient(string key, Color a, Color b)
            {
                if (res[key] is LinearGradientBrush g && !g.IsFrozen && g.GradientStops.Count >= 2)
                { g.GradientStops[0].Color = a; g.GradientStops[1].Color = b; }
                else res[key] = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
            }
            try
            {
                SetBrush("Accent", Hex(t.accent));
                SetBrush("AccentDark", Hex(t.dark));
                SetGradient("ButtonFill", Hex(t.g1), Hex(t.g2));
                SetGradient("ButtonFillHover", Lighter(Hex(t.g1)), Lighter(Hex(t.g2)));
                res["AccentColor"] = Hex(t.accent);
            }
            catch (Exception ex) { Log.Write("Thème : " + ex.Message); }
        }

        private void BuildThemePanel()
        {
            ThemePanel.Children.Clear();
            foreach (var t in Themes)
            {
                var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Hex(t.accent)), Margin = new Thickness(0, 0, 7, 0) };
                var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = t.name } } };
                var rb = new RadioButton { Style = (Style)FindResource("Pill"), GroupName = "theme", Content = content, Margin = new Thickness(0, 0, 8, 8), IsChecked = _settings.Theme == t.id, Tag = t.id };
                rb.Checked += (s, _) =>
                {
                    _settings.Theme = (string)((RadioButton)s).Tag;
                    ApplyTheme(_settings.Theme);
                    _settings.Save();
                };
                ThemePanel.Children.Add(rb);
            }
        }

        private static readonly (string id, string name)[] CapeList =
            { ("velox", "Velox"), ("aurora", "Aurore"), ("neige", "Neige"), ("flamme", "Flamme"), ("nuit", "Nuit") };

        private void BuildCapePanel()
        {
            CapePanel.Children.Clear();
            foreach (var c in CapeList)
            {
                Image img = new Image { Width = 30, Height = 48, Margin = new Thickness(0, 0, 0, 4) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                try
                {
                    var src = new BitmapImage(new Uri($"pack://application:,,,/Assets/capes/{c.id}.png"));
                    img.Source = new CroppedBitmap(src, new Int32Rect(1, 1, 10, 16));   // dos de la cape
                }
                catch { }
                var content = new StackPanel { Children = { img, new TextBlock { Text = c.name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 } } };
                var rb = new RadioButton { Style = (Style)FindResource("Pill"), GroupName = "cape", Content = content, Margin = new Thickness(0, 0, 8, 8), IsChecked = (_settings.Cape ?? "velox") == c.id, Tag = c.id };
                rb.Checked += (s, _) =>
                {
                    _settings.Cape = (string)((RadioButton)s).Tag;
                    _settings.Save();
                    ForpakLauncher.Core.VeloxApi.WriteShared(_settings);
                    if (SkinStatus != null) SkinStatus.Text = "Cape choisie ✓  Elle s'affichera en jeu (et pour tes amis Velox).";
                };
                CapePanel.Children.Add(rb);
            }
        }

        // ------------------------------------------------------------- amis

        private bool _friendsLoading;
        private readonly Dictionary<string, bool> _friendOnline = new();
        private bool _friendsBaseline;
        private readonly HashSet<long> _seenInvites = new();
        private DateTime _lastTokenTry = DateTime.MinValue;

        private static string Ago(long ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            if (t.TotalMinutes < 1) return "à l'instant";
            if (t.TotalHours < 1) return $"il y a {(int)t.TotalMinutes} min";
            if (t.TotalDays < 1) return $"il y a {(int)t.TotalHours} h";
            return $"il y a {(int)t.TotalDays} j";
        }

        private async Task RefreshFriendsAsync()
        {
            if (_friendsLoading) return;
            var names = _settings.Friends.ToList();
            FriendsEmptyText.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (names.Count == 0)
            {
                FriendsList.ItemsSource = null;
                FriendsCount.Text = "";
                NavFriendsSub.Text = "Tes potes Velox";
                return;
            }
            _friendsLoading = true;
            try
            {
                var data = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
                long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                bool ok = false;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var json = await _http.GetStringAsync(VeloxApi + "/friends?names=" + Uri.EscapeDataString(string.Join(",", names)), cts.Token);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("now", out var n)) serverNow = n.GetInt64();
                    foreach (var f in doc.RootElement.GetProperty("friends").EnumerateArray())
                        data[f.GetProperty("name").GetString()] = f.Clone();
                    ok = true;
                }
                catch (Exception ex) { Log.Write("Amis : " + ex.Message); }

                var green = new SolidColorBrush(Color.FromRgb(0x3E, 0xE6, 0xA8));
                var grey = new SolidColorBrush(Color.FromRgb(0x5A, 0x66, 0x7E));
                var views = new List<FriendView>();
                foreach (var name in names)
                {
                    var v = new FriendView { Name = name, Dot = grey, StatusBrush = (Brush)FindResource("Muted") };
                    try { v.Avatar = new BitmapImage(new Uri($"https://mc-heads.net/avatar/{name}/64")); } catch { }
                    if (!ok) v.Status = "Statut indisponible";
                    else if (!data.TryGetValue(name, out var f)) v.Status = "Pas encore sur Velox Client";
                    else
                    {
                        v.Name = f.GetProperty("name").GetString() ?? name;
                        bool online = f.GetProperty("online").GetBoolean();
                        string server = f.GetProperty("server").GetString() ?? "";
                        long last = f.TryGetProperty("last", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.Number ? l.GetInt64() : 0;
                        v.Velox = true;
                        if (online)
                        {
                            v.Online = true;
                            v.Dot = green;
                            v.StatusBrush = green;
                            v.Status = server switch
                            {
                                "menu" => "En jeu · dans les menus",
                                "solo" => "En jeu · en solo",
                                "private" => "En jeu · sur un serveur privé",
                                "hidden" or "" => "En jeu",
                                _ => "En jeu sur " + server
                            };
                            if (server is not ("menu" or "solo" or "private" or "hidden" or ""))
                            {
                                v.Server = server;
                                v.JoinVis = Visibility.Visible;
                            }
                        }
                        else v.Status = "Hors ligne · vu " + Ago(serverNow - last);
                    }
                    views.Add(v);
                }
                // Notification quand un ami se connecte (dans le jeu, c'est le mod qui prévient)
                foreach (var v in views)
                {
                    bool was = _friendOnline.TryGetValue(v.Name.ToLowerInvariant(), out var w) && w;
                    if (ok && _friendsBaseline && v.Online && !was && !_gameRunning)
                        VeloxToast.Show("● " + v.Name + " est en jeu", v.Status.Replace("En jeu · ", "").Replace("En jeu sur ", "Sur "),
                            v.Server != null ? "Rejoindre" : null,
                            v.Server != null ? () => { _joinServer = v.Server; GoHome(); Play_Click(this, new RoutedEventArgs()); } : null);
                    if (ok) _friendOnline[v.Name.ToLowerInvariant()] = v.Online;
                    v.InviteVis = v.Velox && _gameRunning ? Visibility.Visible : Visibility.Collapsed;
                }
                if (ok) _friendsBaseline = true;
                views = views.OrderByDescending(v => v.Online).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
                FriendsList.ItemsSource = views;
                int on = views.Count(v => v.Online);
                FriendsCount.Text = $"{on} en jeu sur {views.Count}";
                NavFriendsSub.Text = on == 0 ? "Tes potes Velox" : on == 1 ? "1 ami en jeu" : $"{on} amis en jeu";
                await RefreshBoardAsync(names);
            }
            finally { _friendsLoading = false; }
        }

        private async Task RefreshBoardAsync(List<string> friends)
        {
            try
            {
                var names = friends.ToList();
                if (_account != null) names.Add(_account.Name);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var json = await _http.GetStringAsync(ForpakLauncher.Core.VeloxApi.Base + "/leaderboard?names=" + Uri.EscapeDataString(string.Join(",", names)), cts.Token);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var rows = doc.RootElement.GetProperty("players").EnumerateArray().Select(p => new
                {
                    Name = p.GetProperty("name").GetString(),
                    K = p.GetProperty("kills").GetInt32(),
                    P = p.GetProperty("pops").GetInt32(),
                    C = p.GetProperty("crystals").GetInt32()
                }).OrderByDescending(r => r.K).ThenByDescending(r => r.P).ThenByDescending(r => r.C).Take(5).ToList();
                if (rows.Count == 0) { BoardCard.Visibility = Visibility.Collapsed; return; }
                var gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x4D));
                var silver = new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xE3));
                var bronze = new SolidColorBrush(Color.FromRgb(0xD9, 0x8C, 0x5F));
                BoardList.ItemsSource = rows.Select((r, i) => new
                {
                    Rank = "#" + (i + 1),
                    RankBrush = i == 0 ? gold : i == 1 ? silver : i == 2 ? bronze : (Brush)FindResource("Muted"),
                    r.Name,
                    Weight = _account != null && r.Name.Equals(_account.Name, StringComparison.OrdinalIgnoreCase) ? FontWeights.Bold : FontWeights.Normal,
                    Score = $"{r.K} · {r.P} · {r.C}"
                }).ToList();
                BoardCard.Visibility = Visibility.Visible;
            }
            catch { BoardCard.Visibility = Visibility.Collapsed; }
        }

        private async Task<bool> VeloxTokenAsync()
        {
            if (_velox.HasToken) return true;
            if (_account == null || DateTime.Now - _lastTokenTry < TimeSpan.FromMinutes(5)) return false;
            _lastTokenTry = DateTime.Now;
            if (!await EnsureFreshTokenAsync(required: false)) return false;
            return await _velox.EnsureTokenAsync(_account);
        }

        private async void InviteFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not FriendView v) return;
            _lastTokenTry = DateTime.MinValue;
            if (!await VeloxTokenAsync()) { FriendsCount.Text = "Connexion au serveur Velox impossible, réessaie."; return; }
            var res = await _velox.PostAsync("/invite", new System.Text.Json.Nodes.JsonObject { ["to"] = v.Name });
            if (res is not { } r) { FriendsCount.Text = "Invitation impossible, réessaie."; return; }
            if (r.GetProperty("ok").GetBoolean())
                FriendsCount.Text = $"Invitation envoyée à {v.Name} ✓";
            else
                FriendsCount.Text = r.TryGetProperty("error", out var err) && err.GetString() == "too_fast"
                    ? "Attends quelques secondes avant de le réinviter."
                    : "Rejoins un serveur en jeu pour pouvoir inviter.";
        }

        /// <summary>Invitations reçues pendant que le jeu est fermé : notification avec « Rejoindre ».</summary>
        private async Task CheckInboxAsync()
        {
            if (_gameRunning || _account == null) return;
            if (!await VeloxTokenAsync()) return;
            try
            {
                var res = await _velox.PostAsync("/inbox", new System.Text.Json.Nodes.JsonObject());
                if (res is not { } r) return;
                foreach (var i in r.GetProperty("invites").EnumerateArray())
                {
                    long id = i.GetProperty("id").GetInt64();
                    if (!_seenInvites.Add(id)) continue;
                    string from = i.GetProperty("from").GetString(), server = i.GetProperty("server").GetString();
                    VeloxToast.Show("✉ " + from + " t'invite", "sur " + server, "Rejoindre", () =>
                    {
                        if (_gameRunning) return;
                        _joinServer = server;
                        GoHome();
                        StatusText.Text = "Lancement et connexion à " + server + " (invité par " + from + ")...";
                        Play_Click(this, new RoutedEventArgs());
                    }, 25);
                }
            }
            catch { }
        }

        private async void AddFriend_Click(object sender, RoutedEventArgs e)
        {
            var name = FriendBox.Text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
            {
                FriendsCount.Text = "Pseudo Minecraft invalide.";
                return;
            }
            if (_settings.Friends.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                FriendsCount.Text = name + " est déjà dans tes amis.";
                return;
            }
            if (_settings.Friends.Count >= 50) { FriendsCount.Text = "50 amis maximum."; return; }
            _settings.Friends.Add(name);
            _settings.Save();
            ForpakLauncher.Core.VeloxApi.WriteShared(_settings);
            FriendBox.Text = "";
            await RefreshFriendsAsync();
        }

        private void FriendBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddFriend_Click(sender, e);
        }

        private async void RemoveFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not FriendView v) return;
            _settings.Friends.RemoveAll(f => f.Equals(v.Name, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            ForpakLauncher.Core.VeloxApi.WriteShared(_settings);
            await RefreshFriendsAsync();
        }

        private void JoinFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not FriendView v || string.IsNullOrEmpty(v.Server)) return;
            if (_gameRunning) { FriendsCount.Text = "Minecraft est déjà lancé : rejoins " + v.Server + " depuis le jeu."; return; }
            _joinServer = v.Server;
            GoHome();
            StatusText.Text = "Lancement et connexion à " + v.Server + " (avec " + v.Name + ")...";
            Play_Click(sender, e);
        }

        // ------------------------------------------------------------- skin

        private string _skinFile;

        private void LoadSkinPage()
        {
            if (_skinFile != null) return;   // garde l'aperçu du fichier choisi
            SkinPreviewLabel.Text = "Ton skin actuel";
            if (_account == null) { SkinStatus.Text = "Connecte-toi pour changer ton skin."; SkinPreview.Source = null; return; }
            try
            {
                SkinPreview.Source = new BitmapImage(new Uri($"https://mc-heads.net/body/{_account.Uuid}/240?t={DateTime.Now.Ticks}"));
            }
            catch { }
        }

        private void PickSkin_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Skin Minecraft (*.png)|*.png", Title = "Choisis ton skin" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                SkinPreview.Source = RenderSkinFront(dlg.FileName, SkinSlim.IsChecked == true);
                _skinFile = dlg.FileName;
                SkinFileText.Text = Path.GetFileName(dlg.FileName);
                SkinPreviewLabel.Text = "Aperçu (de face)";
                ApplySkinButton.IsEnabled = _account != null;
                SkinStatus.Text = _account == null ? "Connecte-toi pour l'appliquer." : "Clique sur « Appliquer le skin ».";
            }
            catch (Exception ex) { SkinStatus.Text = ex.Message; }
        }

        private void SkinModel_Checked(object sender, RoutedEventArgs e)
        {
            if (_skinFile == null || SkinPreview == null) return;
            try { SkinPreview.Source = RenderSkinFront(_skinFile, SkinSlim.IsChecked == true); } catch { }
        }

        private void NameMc_Click(object sender, RoutedEventArgs e) => OpenUrl("https://namemc.com/minecraft-skins");

        private async void ApplySkin_Click(object sender, RoutedEventArgs e)
        {
            if (_skinFile == null || _account == null) return;
            ApplySkinButton.IsEnabled = false;
            SkinStatus.Text = "Envoi du skin à Minecraft...";
            try
            {
                if (!await EnsureFreshTokenAsync(required: true)) { SkinStatus.Text = "Ta session a expiré, reconnecte-toi."; return; }
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(SkinSlim.IsChecked == true ? "slim" : "classic"), "variant");
                var file = new ByteArrayContent(await File.ReadAllBytesAsync(_skinFile));
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                form.Add(file, "file", "skin.png");
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins") { Content = form };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _account.AccessToken);
                using var res = await _http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    SkinStatus.Text = "Skin changé ✓  Il apparaîtra en jeu à ta prochaine connexion.";
                    try { AvatarImage.Source = new BitmapImage(new Uri($"https://mc-heads.net/avatar/{_account.Uuid}/64?t={DateTime.Now.Ticks}")); } catch { }
                }
                else
                {
                    var body = await res.Content.ReadAsStringAsync();
                    Log.Write($"Skin : {(int)res.StatusCode} {body}");
                    SkinStatus.Text = (int)res.StatusCode == 429 ? "Trop d'essais, attends une minute et réessaie."
                        : $"Minecraft a refusé le skin (erreur {(int)res.StatusCode}).";
                }
            }
            catch (Exception ex) { SkinStatus.Text = "Erreur : " + ex.Message; }
            finally { ApplySkinButton.IsEnabled = true; }
        }

        /// <summary>Dessine le skin vu de face (tête, corps, bras, jambes et leurs couches).</summary>
        private static BitmapSource RenderSkinFront(string path, bool slim)
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(path);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            src.Freeze();
            if (src.PixelWidth != 64 || (src.PixelHeight != 64 && src.PixelHeight != 32))
                throw new InvalidDataException("Ce n'est pas un skin : l'image doit faire 64x64 pixels.");
            bool legacy = src.PixelHeight == 32;
            int aw = slim ? 3 : 4;
            const int S = 12;

            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
            using (var dc = dv.RenderOpen())
            {
                void P(int sx, int sy, int w, int h, int dx, int dy, bool flip = false)
                {
                    BitmapSource b = new CroppedBitmap(src, new Int32Rect(sx, sy, w, h));
                    if (flip) b = new TransformedBitmap(b, new ScaleTransform(-1, 1));
                    dc.DrawImage(b, new Rect(dx * S, dy * S, w * S, h * S));
                }
                P(8, 8, 8, 8, 4, 0);                               // tête
                P(20, 20, 8, 12, 4, 8);                            // corps
                P(44, 20, aw, 12, 4 - aw, 8);                      // bras droit
                if (legacy) P(44, 20, aw, 12, 12, 8, true); else P(36, 52, aw, 12, 12, 8);   // bras gauche
                P(4, 20, 4, 12, 4, 20);                            // jambe droite
                if (legacy) P(4, 20, 4, 12, 8, 20, true); else P(20, 52, 4, 12, 8, 20);      // jambe gauche
                P(40, 8, 8, 8, 4, 0);                              // chapeau
                if (!legacy)
                {
                    P(20, 36, 8, 12, 4, 8);                        // veste
                    P(44, 36, aw, 12, 4 - aw, 8);                  // manche droite
                    P(52, 52, aw, 12, 12, 8);                      // manche gauche
                    P(4, 36, 4, 12, 4, 20);                        // pantalon droit
                    P(4, 52, 4, 12, 8, 20);                        // pantalon gauche
                }
            }
            var rtb = new RenderTargetBitmap(16 * S, 32 * S, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // ------------------------------------------------------------- logs

        private LogView _currentLog;
        private string _pendingLog;
        private static readonly Brush LogRed = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
        private static readonly Brush LogGreen = new SolidColorBrush(Color.FromRgb(0x3E, 0xE6, 0xA8));
        private static readonly Brush LogBlue = new SolidColorBrush(Color.FromRgb(0x5C, 0x9D, 0xFF));

        private string InstanceLabel(string dir)
        {
            if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\', '/'), Path.GetFullPath(Paths.Root).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase)) return "Vanilla";
            var pack = _settings.Modpacks?.FirstOrDefault(p =>
                string.Equals(Path.GetFullPath(p.Dir), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
            if (pack != null) return "Modpack " + pack.Name;
            var n = Path.GetFileName(dir);
            return n.EndsWith("-fabric") ? "Fabric " + n[..^7] : n;
        }

        private List<string> GameDirs()
        {
            var dirs = new List<string> { Paths.Root };
            var inst = Path.Combine(Paths.Root, "instances");
            if (Directory.Exists(inst)) dirs.AddRange(Directory.GetDirectories(inst));
            return dirs;
        }

        /// <summary>Lit un fichier même s'il est ouvert par le jeu (garde la fin si énorme).</summary>
        private static string ReadShared(string path, int maxBytes = 600_000)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool cut = fs.Length > maxBytes;
            if (cut) fs.Seek(-maxBytes, SeekOrigin.End);
            using var sr = new StreamReader(fs);
            var text = sr.ReadToEnd();
            return cut ? "... (début du fichier coupé, il est très long)\n" + text : text;
        }

        /// <summary>Trouve la cause d'un crash : l'erreur Java après « Description: ».</summary>
        private static string CrashReason(string path)
        {
            try
            {
                var lines = ReadShared(path, 40_000).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                if (Path.GetFileName(path).StartsWith("hs_err"))
                    return "Crash de Java (" + (lines.FirstOrDefault(l => l.Contains("EXCEPTION_"))?.Trim('#', ' ') ?? "JVM") + ")";
                int d = lines.FindIndex(l => l.StartsWith("Description:"));
                if (d < 0) return "";
                var err = lines.Skip(d + 1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
                int colon = err.IndexOf(':');
                var cls = colon > 0 ? err[..colon] : err;
                var msg = colon > 0 ? err[(colon + 1)..].Trim() : "";
                cls = cls.Contains('.') ? cls[(cls.LastIndexOf('.') + 1)..] : cls;
                return msg.Length > 0 ? cls + " : " + msg : cls;
            }
            catch { return ""; }
        }

        private void LoadLogs()
        {
            var items = new List<LogView>();
            foreach (var dir in GameDirs())
            {
                var label = InstanceLabel(dir);
                try
                {
                    var crashDir = Path.Combine(dir, "crash-reports");
                    if (Directory.Exists(crashDir))
                        foreach (var f in Directory.GetFiles(crashDir, "*.txt"))
                            items.Add(new LogView { Path = f, Kind = "crash", Instance = label, Date = File.GetLastWriteTime(f) });
                    foreach (var f in Directory.GetFiles(dir, "hs_err_pid*.log"))
                        items.Add(new LogView { Path = f, Kind = "crash", Instance = label, Date = File.GetLastWriteTime(f) });
                    var latest = Path.Combine(dir, "logs", "latest.log");
                    if (File.Exists(latest))
                        items.Add(new LogView { Path = latest, Kind = "game", Instance = label, Date = File.GetLastWriteTime(latest) });
                }
                catch { }
            }
            if (File.Exists(Paths.LogFile))
                items.Add(new LogView { Path = Paths.LogFile, Kind = "launcher", Instance = "Velox Client", Date = File.GetLastWriteTime(Paths.LogFile) });

            items = items.OrderByDescending(i => i.Date).Take(150).ToList();
            foreach (var i in items)
            {
                i.Title = i.Kind switch { "crash" => "Crash · " + i.Instance, "game" => "Journal · " + i.Instance, _ => "Journal du launcher" };
                i.Sub = i.Date.ToString("dd/MM/yyyy HH:mm") + " · " + Path.GetFileName(i.Path);
                i.Dot = i.Kind switch { "crash" => LogRed, "game" => LogGreen, _ => LogBlue };
                if (i.Kind == "crash") i.Reason = CrashReason(i.Path);
            }

            LogsList.ItemsSource = items;
            int crashes = items.Count(i => i.Kind == "crash");
            LogsCount.Text = crashes == 0 ? "Aucun crash 👌" : crashes + (crashes > 1 ? " crashs" : " crash");
            LogsEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var want = _pendingLog ?? _currentLog?.Path;
            _pendingLog = null;
            var sel = items.FirstOrDefault(i => string.Equals(i.Path, want, StringComparison.OrdinalIgnoreCase))
                      ?? items.FirstOrDefault(i => i.Kind == "crash") ?? items.FirstOrDefault();
            ShowLog(sel);
        }

        private void ShowLog(LogView v)
        {
            if (_currentLog != null) _currentLog.Selected = false;
            _currentLog = v;
            LogCopyButton.IsEnabled = LogFolderButton.IsEnabled = v != null;
            if (v == null)
            {
                LogViewTitle.Text = "Choisis un log à gauche";
                LogViewPath.Text = "";
                LogViewer.Text = "";
                return;
            }
            v.Selected = true;
            LogViewTitle.Text = v.Title + (string.IsNullOrEmpty(v.Reason) ? "" : "  —  " + v.Reason);
            LogViewPath.Text = v.Path;
            try
            {
                LogViewer.Text = ReadShared(v.Path);
                if (v.Kind == "game") { LogViewer.ScrollToEnd(); }   // la fin du journal est la plus utile
                else LogViewer.ScrollToHome();
            }
            catch (Exception ex) { LogViewer.Text = "Impossible de lire ce fichier : " + ex.Message; }
        }

        private void LogItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return; // clic sur le X
            if (sender is FrameworkElement fe && fe.Tag is LogView v) ShowLog(v);
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        private void DeleteLog_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not FrameworkElement fe || fe.Tag is not LogView v) return;
            try
            {
                File.Delete(v.Path);
                if (_currentLog == v) _currentLog = null;
                LoadLogs();
            }
            catch (IOException)
            {
                LogsCount.Text = "Ce fichier est utilisé (le jeu est encore ouvert ?)";
            }
            catch (Exception ex) { LogsCount.Text = "Suppression impossible : " + ex.Message; }
        }

        private void ClearCrashes_Click(object sender, RoutedEventArgs e)
        {
            if (LogsList.ItemsSource is not IEnumerable<LogView> items) return;
            var crashes = items.Where(i => i.Kind == "crash").ToList();
            if (crashes.Count == 0) return;
            if (MessageBox.Show($"Supprimer les {crashes.Count} rapports de crash ?", "Logs",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var c in crashes) { try { File.Delete(c.Path); } catch { } }
            _currentLog = null;
            LoadLogs();
        }

        private void RefreshLogs_Click(object sender, RoutedEventArgs e) => LoadLogs();

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LogViewer.Text);
                LogCopyButton.Content = "Copié ✓";
                _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => LogCopyButton.Content = "Copier"));
            }
            catch { }
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLog == null) return;
            try { Process.Start("explorer.exe", $"/select,\"{_currentLog.Path}\""); } catch { }
        }

        /// <summary>Appelé quand Minecraft se ferme avec une erreur : propose de voir le rapport.</summary>
        private void OfferCrashReport(string gameDir, DateTime since)
        {
            try
            {
                var candidates = new List<string>();
                var crashDir = Path.Combine(gameDir, "crash-reports");
                if (Directory.Exists(crashDir)) candidates.AddRange(Directory.GetFiles(crashDir, "*.txt"));
                candidates.AddRange(Directory.GetFiles(gameDir, "hs_err_pid*.log"));
                var report = candidates.Where(f => File.GetLastWriteTime(f) >= since.AddSeconds(-5))
                                       .OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
                var target = report ?? Path.Combine(gameDir, "logs", "latest.log");
                if (!File.Exists(target)) return;

                var reason = report != null ? CrashReason(report) : "";
                var msg = report != null
                    ? "Minecraft a planté." + (reason.Length > 0 ? "\n\nCause : " + reason : "") + "\n\nVoir le rapport de crash ?"
                    : "Minecraft s'est fermé avec une erreur.\n\nVoir le journal du jeu ?";
                if (MessageBox.Show(msg, "Velox Client", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

                _pendingLog = target;
                if (NavLogs.IsChecked == true) LoadLogs(); else NavLogs.IsChecked = true;
            }
            catch (Exception ex) { Log.Write("OfferCrashReport : " + ex.Message); }
        }

        // ------------------------------------------------------------- mise à jour du launcher

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var r = MessageBox.Show($"Installer la version {_remoteVersion} ? Le launcher va redémarrer.",
                "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            GoHome();
            SetBusy(true);
            UpdateButton.IsEnabled = false;
            StatusText.Text = "Téléchargement de la mise à jour...";
            try
            {
                var progress = new Progress<double>(v => SetProgress(v));
                await _updater.UpdateLauncherAsync(progress);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Mise à jour impossible : " + ex.Message;
                UpdateButton.IsEnabled = true;
                SetBusy(false);
            }
        }

        // ------------------------------------------------------------- fenêtre

        private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            WindowClip.Rect = new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static void OpenUrl(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }
    }

    public class ServerView
    {
        public ServerEntry Entry { get; set; }
        public string Name => Entry.Name;
        public string Address => Entry.Address;
        public string Motd { get; set; } = "Connexion...";
        public string PlayersText { get; set; } = "";
        public string PingText { get; set; } = "";
        public Brush StatusBrush { get; set; } = Brushes.Gray;
        public ImageSource Favicon { get; set; }
    }

    public class ShotView
    {
        public string Path { get; set; }
        public ImageSource Thumb { get; set; }
        public string Label { get; set; }
    }

    public class LogView : System.ComponentModel.INotifyPropertyChanged
    {
        public string Path { get; set; }
        public string Kind { get; set; }
        public string Instance { get; set; }
        public DateTime Date { get; set; }
        public string Title { get; set; }
        public string Sub { get; set; }
        public string Reason { get; set; }
        public Visibility ReasonVis => string.IsNullOrEmpty(Reason) ? Visibility.Collapsed : Visibility.Visible;
        public Brush Dot { get; set; }

        private bool _selected;
        public bool Selected
        {
            get => _selected;
            set { _selected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Border))); }
        }
        public Brush Border => _selected ? SelBrush : NoBrush;
        private static readonly Brush SelBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0xE6, 0xA8));
        private static readonly Brush NoBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x30, 0x4A));

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public class FriendView
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string Server { get; set; }
        public bool Online { get; set; }
        public Brush Dot { get; set; }
        public Brush StatusBrush { get; set; }
        public ImageSource Avatar { get; set; }
        public Visibility JoinVis { get; set; } = Visibility.Collapsed;
        public Visibility InviteVis { get; set; } = Visibility.Collapsed;
        public bool Velox { get; set; }
    }
}

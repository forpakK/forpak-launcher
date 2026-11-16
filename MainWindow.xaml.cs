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
        private readonly Settings _settings;
        private readonly GameManager _game;
        private readonly ModManager _mods;
        private readonly Updater _updater;
        private readonly CurseForge _cf;
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
            VersionLabel.Text = "v" + Updater.CurrentVersionText;

            FabricSwitch.IsChecked = _settings.UseFabric;
            PerfSwitch.IsChecked = _settings.PerformanceMode;
            ClientSwitch.IsChecked = _settings.ForpakClient;
            _loading = false;
            UpdateSubtitle();

            Loaded += async (_, _) =>
            {
                if (_settings.AnimatedBackground) StartBackground();
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

            // Compte enregistré
            var refresh = _settings.GetRefreshToken();
            if (refresh != null)
            {
                SetAccountUi(_settings.AccountName, _settings.AccountUuid, "Reconnexion...");
                try
                {
                    _account = await new MicrosoftAuth(_http, _settings.ClientId).RefreshAsync(refresh);
                    SaveAccount();
                }
                catch (Exception ex)
                {
                    _account = null;
                    StatusText.Text = ex.Message;
                }
            }
            UpdateAccountUi();
        }

        private void RefreshVersionList()
        {
            var previous = (VersionCombo.SelectedItem as McVersion)?.Id ?? _settings.LastVersion;
            if (string.IsNullOrEmpty(previous)) previous = "1.21.11";
            var list = _allVersions
                .Where(v => v.Type == "release" || (_settings.ShowSnapshots && v.Type == "snapshot"))
                .ToList();
            VersionCombo.ItemsSource = list;
            VersionCombo.SelectedItem = list.FirstOrDefault(v => v.Id == previous) ?? list.FirstOrDefault();
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

            // Neige (pixel art) : les gros flocons tombent plus vite, comme s'ils étaient plus proches
            Particles.Children.Clear();
            double w = Math.Max(800, ActualWidth), h = Math.Max(500, ActualHeight);
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
            if (HomePage == null) return; // pendant InitializeComponent
            HomePage.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ModsPage.Visibility = NavMods.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            FrameworkElement shown = NavMods.IsChecked == true ? ModsPage
                : NavSettings.IsChecked == true ? SettingsPage : HomePage;
            Animate(shown);

            if (NavMods.IsChecked == true) _ = OpenModsPageAsync();
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
                    _settings.SetRefreshToken(null);
                    _settings.AccountName = _settings.AccountUuid = "";
                    _settings.Save();
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

        private void SaveAccount()
        {
            _settings.AccountName = _account.Name;
            _settings.AccountUuid = _account.Uuid;
            _settings.SetRefreshToken(_account.RefreshToken);
            _settings.Save();
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
            SaveHomeSwitches();
            var fabric = _settings.UseFabric;

            try
            {
                // Rafraîchit le token (il expire au bout de 24h)
                var refresh = _account.RefreshToken;
                if (refresh != null)
                {
                    try { _account = await new MicrosoftAuth(_http, _settings.ClientId).RefreshAsync(refresh); SaveAccount(); }
                    catch { /* on garde l'ancien token */ }
                }

                var ct = CancellationToken.None;
                var launchId = await Task.Run(() => _game.InstallAsync(version, fabric, ct));
                Action<string> status = msg => Dispatcher.Invoke(() => StatusText.Text = msg);

                if (fabric)
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
                var proc = _game.Launch(launchId, _account, _settings, java, ModManager.GameDir(version.Id, fabric));
                _gameRunning = true;
                StopBackground(); // le launcher ne consomme rien pendant que tu joues
                proc.Exited += (_, _) => Dispatcher.Invoke(() =>
                {
                    _gameRunning = false;
                    if (_settings.AnimatedBackground) StartBackground();
                    StatusText.Text = proc.ExitCode == 0 ? "Partie terminée." :
                        $"Le jeu s'est arrêté (code {proc.ExitCode}). Détails : {Paths.LogFile}";
                    if (!IsVisible) { Show(); Activate(); }
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
            SourcePanel.IsEnabled = !busy;
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
            CfKeyBox.Text = _settings.CurseForgeApiKey;
            SavedText.Visibility = Visibility.Collapsed;
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
                    Style = (Style)FindResource("Pill"), IsChecked = id == _modCategory
                };
                rb.Checked += async (s2, _) =>
                {
                    _modCategory = (string)((RadioButton)s2).Tag;
                    _modPage = 0;
                    await LoadModsAsync();
                };
                CategoryChips.Children.Add(rb);
            }
        }

        private async void Source_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || NavMods == null) return; // pendant InitializeComponent
            _modSource = SrcCurseForge.IsChecked == true ? "curseforge" : "modrinth";
            _modsLoadedFor = null;
            if (NavMods.IsChecked != true) NavMods.IsChecked = true; // ouvre la page Mods
            else await OpenModsPageAsync();
        }

        private async Task OpenModsPageAsync()
        {
            if (ModsMc == null) return;
            if (FabricSwitch.IsChecked != true) FabricSwitch.IsChecked = true; // les mods ont besoin de Fabric
            await BuildCategoryChipsAsync();
            ModsVersionText.Text = "Fabric " + ModsMc;
            ModsSourceText.Text = IsCf ? "via CurseForge" : "via Modrinth";
            ShowBrowse();
            RefreshInstalled();
            if (_modsLoadedFor != ModsMc + _modSource) { _modPage = 0; await LoadModsAsync(); }
        }

        private async Task LoadModsAsync()
        {
            var mc = ModsMc;
            if (mc == null) return;
            ModsEmptyText.Text = "Chargement...";
            CfKeyPanel.Visibility = Visibility.Collapsed;
            ModResults.ItemsSource = null;
            PrevPageButton.IsEnabled = NextPageButton.IsEnabled = false;

            if (IsCf && !_cf.HasKey)
            {
                ModsEmptyText.Text = "CurseForge demande une clé API gratuite.\n" +
                    "Crée-la sur console.curseforge.com (onglet API Keys), puis colle-la dans les paramètres.";
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
                ModsEmptyText.Text = "Modrinth ne répond pas : " + ex.Message;
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
}

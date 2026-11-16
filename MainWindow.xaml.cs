using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private string _remoteVersion;
        private List<McVersion> _allVersions = new();
        private McAccount _account;
        private CancellationTokenSource _loginCts;
        private string _pendingCode;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();

            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ForpakLauncher/1.0");
            _settings = Settings.Load();
            _game = new GameManager(_http)
            {
                Status = msg => Dispatcher.Invoke(() => StatusText.Text = msg),
                Progress = v => Dispatcher.Invoke(() => SetProgress(v))
            };

            _mods = new ModManager(_http);
            _updater = new Updater(_http);
            VersionLabel.Text = "v" + Updater.CurrentVersionText;

            FabricSwitch.IsChecked = _settings.UseFabric;
            Loaded += async (_, _) => await InitAsync();
        }

        // ------------------------------------------------------------- démarrage

        private async Task InitAsync()
        {
            // Versions
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

            // Mises à jour (launcher + Forpak Client)
            _remoteVersion = await _updater.GetRemoteVersionAsync();
            if (Updater.IsNewer(_remoteVersion))
            {
                UpdateButton.Content = "Mise à jour v" + _remoteVersion;
                UpdateButton.Visibility = Visibility.Visible;
            }

            // Compte enregistré
            var refresh = _settings.GetRefreshToken();
            if (refresh != null && HasClientId())
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
            var list = _allVersions
                .Where(v => v.Type == "release" || (_settings.ShowSnapshots && v.Type == "snapshot"))
                .ToList();
            VersionCombo.ItemsSource = list;
            VersionCombo.SelectedItem = list.FirstOrDefault(v => v.Id == previous) ?? list.FirstOrDefault();
        }

        // ------------------------------------------------------------- compte

        private bool HasClientId()
        {
            // Vide = ID du launcher officiel, donc toujours utilisable
            return true;
        }

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
            if (_account != null) SetAccountUi(_account.Name, _account.Uuid, "Compte Microsoft · se déconnecter");
            else
            {
                SetAccountUi(null, null, "Compte Microsoft");
                AccountName.Text = "Se connecter";
            }
        }

        private void SetAccountUi(string name, string uuid, string hint)
        {
            AccountName.Text = string.IsNullOrEmpty(name) ? "Se connecter" : name;
            AccountHint.Text = hint;
            AvatarLetter.Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            AvatarImage.Source = null;
            if (!string.IsNullOrEmpty(uuid))
            {
                try
                {
                    AvatarImage.Source = new BitmapImage(new Uri($"https://mc-heads.net/avatar/{uuid}/68"));
                }
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
            _settings.UseFabric = FabricSwitch.IsChecked == true;
            _settings.Save();
            var fabric = _settings.UseFabric;

            try
            {
                // Rafraîchit le token si besoin (il expire au bout de 24h)
                var refresh = _account.RefreshToken;
                if (refresh != null)
                {
                    try { _account = await new MicrosoftAuth(_http, _settings.ClientId).RefreshAsync(refresh); SaveAccount(); }
                    catch { /* on garde l'ancien token */ }
                }

                var ct = CancellationToken.None;
                var launchId = await Task.Run(() => _game.InstallAsync(version, fabric, ct));

                // Forpak Client (modules) : installé / mis à jour automatiquement
                if (fabric && ModManager.SupportsForpakClient(version.Id))
                {
                    if (_settings.ForpakClient)
                    {
                        try
                        {
                            await Task.Run(() => _mods.EnsureForpakClientAsync(version.Id, _remoteVersion,
                                msg => Dispatcher.Invoke(() => StatusText.Text = msg)));
                        }
                        catch (Exception ex)
                        {
                            Log.Write("Forpak Client : " + ex.Message);
                            StatusText.Text = "Forpak Client indisponible, lancement sans les modules.";
                        }
                    }
                    else ModManager.RemoveForpakClient(version.Id);
                }
                var java = string.IsNullOrWhiteSpace(_settings.JavaPath)
                    ? await Task.Run(() => _game.EnsureJavaAsync(launchId, ct))
                    : _settings.JavaPath;

                StatusText.Text = "Lancement de Minecraft " + launchId + "...";
                SetProgress(-1);
                var proc = _game.Launch(launchId, _account, _settings, java, ModManager.GameDir(version.Id, fabric));
                proc.Exited += (_, _) => Dispatcher.Invoke(() =>
                {
                    StatusText.Text = proc.ExitCode == 0 ? "Partie terminée." :
                        $"Le jeu s'est arrêté (code {proc.ExitCode}). Détails : {Paths.LogFile}";
                    if (!IsVisible) Close();
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
            AccountButton.IsEnabled = !busy;
            ModsButton.IsEnabled = !busy;
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

        private void Fabric_Changed(object sender, RoutedEventArgs e)
        {
            if (SubtitleText == null) return;
            SubtitleText.Text = FabricSwitch.IsChecked == true
                ? "Fabric activé : ajoute des mods avec le bouton « Mods »."
                : "Choisis ta version et lance le jeu.";
        }

        // ------------------------------------------------------------- paramètres

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            RamSlider.Value = _settings.RamMb;
            RamText.Text = $"{_settings.RamMb / 1024.0:0.#} Go";
            JavaBox.Text = _settings.JavaPath;
            JvmBox.Text = _settings.ExtraJvmArgs;
            SnapshotSwitch.IsChecked = _settings.ShowSnapshots;
            CloseSwitch.IsChecked = _settings.CloseOnLaunch;
            ForpakClientSwitch.IsChecked = _settings.ForpakClient;
            SettingsOverlay.Visibility = Visibility.Visible;
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
            _settings.ForpakClient = ForpakClientSwitch.IsChecked == true;
            _settings.Save();
            RefreshVersionList();
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;

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

        // ------------------------------------------------------------- mods (Modrinth)

        private string ModsMc => (VersionCombo.SelectedItem as McVersion)?.Id;

        private async void OpenMods_Overlay_Click(object sender, RoutedEventArgs e)
        {
            if (ModsMc == null) return;
            if (FabricSwitch.IsChecked != true) FabricSwitch.IsChecked = true; // les mods ont besoin de Fabric
            ModsVersionText.Text = "Fabric " + ModsMc;
            ModsOverlay.Visibility = Visibility.Visible;
            RefreshInstalled();
            if (ModResults.Items.Count == 0 || (string)ModResults.Tag != ModsMc)
                await SearchModsAsync(ModSearchBox.Text);
        }

        private void CloseMods_Click(object sender, RoutedEventArgs e) => ModsOverlay.Visibility = Visibility.Collapsed;

        private async void ModSearch_Click(object sender, RoutedEventArgs e) => await SearchModsAsync(ModSearchBox.Text);

        private async void ModSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await SearchModsAsync(ModSearchBox.Text);
        }

        private async Task SearchModsAsync(string query)
        {
            var mc = ModsMc;
            if (mc == null) return;
            ModsEmptyText.Text = "Recherche...";
            ModResults.ItemsSource = null;
            try
            {
                var hits = await _mods.SearchAsync(query, mc);
                ModResults.ItemsSource = hits;
                ModResults.Tag = mc;
                ModsEmptyText.Text = hits.Count == 0 ? "Aucun mod trouvé pour " + mc + "." : "";
            }
            catch (Exception ex)
            {
                ModsEmptyText.Text = "Modrinth ne répond pas : " + ex.Message;
            }
        }

        private async void InstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not ModHit hit || ModsMc == null) return;
            b.IsEnabled = false;
            b.Content = "...";
            try
            {
                var mc = ModsMc;
                await Task.Run(() => _mods.InstallAsync(hit.ProjectId, mc));
                b.Content = "Installé ✓";
                RefreshInstalled();
            }
            catch (Exception ex)
            {
                b.Content = "Installer";
                b.IsEnabled = true;
                MessageBox.Show(ex.Message, "Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not InstalledMod mod || ModsMc == null) return;
            try { _mods.Remove(ModsMc, mod); } catch (Exception ex) { MessageBox.Show(ex.Message); }
            RefreshInstalled();
        }

        private void RefreshInstalled()
        {
            if (ModsMc == null) return;
            InstalledList.ItemsSource = _mods.GetInstalled(ModsMc);
        }

        // ------------------------------------------------------------- mise à jour du launcher

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var r = MessageBox.Show($"Installer la version {_remoteVersion} ? Le launcher va redémarrer.",
                "Mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

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

# Velox Client

Launcher Minecraft Java épuré pour Windows (C# / WPF, .NET 8), livré en un seul `.exe`.

## Fonctionnalités
- Connexion **compte Microsoft** (le jeu doit être acheté), session gardée chiffrée (DPAPI)
- Choix de la **version** (releases, + snapshots en option), téléchargement automatique
- **Fabric** en un clic + dossier `mods`
- Réglage de la **RAM**, chemin **Java** perso (sinon Java est téléchargé tout seul depuis Mojang), arguments JVM
- Tout est rangé dans `%APPDATA%\.forpaklauncher` (séparé du launcher officiel)

## Velox Client (mod) (modules PvP)
Mod Fabric installé et mis à jour automatiquement par le launcher (Minecraft **1.21.1** et **1.21.11**, Fabric activé).
- **Shift droit** : menu des modules (ON/OFF, sauvegardé dans `config/forpakclient.json`)
- FPS, CPS (gauche | droit), coordonnées + direction
- Keystrokes (suivent tes touches, ZQSD en AZERTY), clics + barre espace
- Zoom : maintenir **C**
- Armure (durabilité en couleur) et potions (temps restant)
- Compteur crystal PvP : crystals, obsidienne, totems, ancres, glowstone, pommes, fioles d'XP
- Crystal Optimizer : le crystal disparaît dès que tu le tapes, pour replacer plus vite.
  Vérifie que c'est autorisé sur ton serveur.

Code : `mod/common/java` (commun) + `mod/v1_21_1` et `mod/v1_21_11` (ce qui change entre les versions).

## Mods (Modrinth)
Bouton **Mods** : recherche et installe des mods Fabric depuis Modrinth (avec leurs dépendances).
Chaque version a son propre dossier : `%APPDATA%\.forpaklauncher\instances\<version>-fabric`.

## Mises à jour automatiques
À chaque push sur `main`, GitHub compile le launcher et les mods et remplace la release `latest`.
Le launcher vérifie `version.txt` au démarrage : un bouton **Mise à jour** apparaît en haut,
et Velox Client (mod) se met à jour tout seul au lancement du jeu.

⚠️ Le dépôt doit s'appeler comme dans `Core/Updater.cs` → `Repo = "forpakK/forpak-launcher"` (change-le sinon)
et être **public** (sinon le launcher ne peut pas télécharger les fichiers).

## Obtenir le .exe
Chaque push sur `main` lance GitHub Actions qui compile le launcher.
Le fichier est dans **Releases → latest → VeloxClient.exe** (ou dans l'onglet *Actions*, artefact `ForpakLauncher`).

Pour compiler sur ton PC : installe le SDK .NET 8 puis
```
dotnet publish ForpakLauncher.csproj -c Release -o out
```

## Connexion Microsoft
Par défaut, le launcher utilise le Client ID du launcher officiel Minecraft (`00000000402B5328`,
même méthode que YuyuFrame et d'autres launchers). Rien à configurer : clique sur « Se connecter ».

⚠️ Ce n'est pas l'usage prévu par Microsoft : ça peut arrêter de marcher si Microsoft/Mojang le bloquent.
Solution durable : crée ton propre Client ID sur Azure (App registration, « Personal Microsoft accounts only »,
« Allow public client flows » activé, puis demande d'accès via https://aka.ms/mce-reviewappid) et colle-le dans
`"ClientId"` de `%APPDATA%\.forpaklauncher\launcher_settings.json`.

## Fichiers
- `Core/MicrosoftAuth.cs` – connexion Microsoft → Xbox → Minecraft
- `Core/GameManager.cs` – versions, librairies, assets, Java, Fabric, lancement
- `Core/Downloader.cs` – téléchargements parallèles avec vérif SHA1
- `Core/Settings.cs` – paramètres et chemins
- `MainWindow.xaml` – l'interface (couleurs dans `App.xaml`)

Logs : `%APPDATA%\.forpaklauncher\launcher_log.txt`

# Mareya USB Fix

**Logiciel gratuit contre le virus « raccourci » des clés USB et disques durs.**

Le virus cache vos fichiers et les remplace par des raccourcis qui le relancent à chaque clic.
Mareya USB Fix supprime le virus, réaffiche vos fichiers sans rien perdre, nettoie l'ordinateur
qui réinfecte les clés, et peut protéger automatiquement chaque clé branchée.

## Télécharger

➡️ **[Télécharger MareyaUSBFix.exe](https://github.com/barryboubacar387-cell/mareya-usb-fix/releases/latest)**

Un seul fichier d'environ 120 Ko, sans installation. Il fonctionne sur Windows 7, 8, 10 et 11.

📖 [Guide d'utilisation](docs/GUIDE.md)

## Fonctionnalités

- Analyse et réparation d'une clé USB ou d'un disque externe, en un clic.
- Suppression des raccourcis piégés, des scripts cachés, des fichiers `autorun.inf` et des
  dossiers du virus.
- Réaffichage des fichiers cachés. Vos fichiers ne sont jamais supprimés.
- Nettoyage de l'ordinateur : processus du virus, démarrage automatique, tâches planifiées,
  réglages bloqués.
- Vaccination des clés.
- Protection automatique : chaque clé branchée est nettoyée.
- Mode ligne de commande pour les techniciens.

## Pour les développeurs

Le projet est écrit en C# avec WinForms et cible .NET Framework 4.8, déjà présent dans Windows.

```
dotnet build src/MareyaUSBFix/MareyaUSBFix.csproj -c Release
```

À chaque envoi de code, GitHub Actions compile le logiciel sur Windows et lance
`tests/simulate-infection.ps1`. Ce test crée une infection simulée et inoffensive sur un faux
lecteur, la répare, puis vérifie chaque point.

Pour publier une nouvelle version, créez un tag `vX.Y.Z` : le fichier `.exe` est alors ajouté
automatiquement à la page des versions.

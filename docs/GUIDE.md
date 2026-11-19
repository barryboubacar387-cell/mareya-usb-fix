# Guide d'utilisation — Mareya USB Fix

Mareya USB Fix supprime le **virus « raccourci »** : celui qui cache vos fichiers sur une clé USB
ou un disque dur et les remplace par des raccourcis.

## 1. Télécharger

Téléchargez **`MareyaUSBFix.exe`** depuis la page des versions :
https://github.com/mareya-group/mareya-usb-fix/releases/latest

Il n'y a rien à installer. Le logiciel fonctionne sur Windows 7, 8, 10 et 11.

## 2. Premier lancement : l'avertissement de Windows

Le logiciel n'est pas encore signé numériquement. Au premier lancement, Windows peut afficher
**« Windows a protégé votre ordinateur »** :

1. Cliquez sur **Informations complémentaires**.
2. Cliquez sur **Exécuter quand même**.

Windows demande ensuite l'autorisation administrateur : répondez **Oui**. Elle est nécessaire
pour nettoyer l'ordinateur.

## 3. Réparer une clé

1. Branchez la clé USB (ou le disque dur).
2. Choisissez-la dans la liste **Lecteur à réparer**.
3. Laissez cochée l'option **Nettoyer aussi l'ordinateur**. C'est l'ordinateur infecté qui
   réinfecte chaque clé : si on ne nettoie que la clé, le virus revient.
4. Cliquez sur **Analyser**.
5. La liste affiche ce qui a été trouvé :
   - **en rouge, cochés** : éléments du virus, ils seront supprimés ;
   - **en orange, non cochés** : éléments à vérifier. Cochez-les seulement si vous ne les
     reconnaissez pas.
6. Cliquez sur **Réparer la sélection**.

Vos fichiers réapparaissent. **Ils ne sont jamais supprimés** : le virus les avait seulement cachés.
Si le virus les avait rangés dans un dossier caché sans nom, ils sont remis à leur place et ce
dossier est supprimé. Si un fichier du même nom existait déjà, il est conservé sous le nom
« nom (2) » : rien n'est écrasé.

### Dossiers « found.000 »

Ces dossiers (`found.000`, `found.001`…) sont créés par **Windows** quand il répare un disque :
ils contiennent des morceaux de fichiers abîmés (`.CHK`), rarement utiles. Ils appartiennent au
système, c'est pourquoi Windows refuse de les supprimer. Mareya USB Fix les affiche **en orange,
non cochés** : cochez-les puis cliquez sur **Réparer la sélection** pour les supprimer.

### Quarantaine : rien n'est supprimé définitivement

Les éléments retirés de la clé (raccourcis piégés, scripts du virus…) ne sont pas effacés : ils
sont déplacés dans le dossier **« Quarantaine Mareya USB Fix »** à la racine, rangés par date et
rendus inoffensifs (extension `.quarantaine`). Si un élément vous appartenait, déplacez-le hors de
ce dossier et retirez `.quarantaine` de son nom. Quand tout va bien, vous pouvez supprimer ce dossier.

Un dossier ou un programme (.exe) n'est **jamais** retiré d'office parce qu'il est caché : vos
installateurs sont simplement réaffichés.

## 4. Vacciner la clé (conseillé)

Après la réparation, le logiciel propose de **vacciner** la clé. Il y crée un dossier protégé
`autorun.inf` qui empêche de nombreux virus de s'installer sur la clé. Ce dossier est caché et ne
prend aucune place.

## 5. Protection automatique

Cochez **Protection automatique** en bas de la fenêtre :

- le logiciel démarre avec Windows (icône près de l'horloge) ;
- chaque clé branchée est analysée et nettoyée automatiquement ;
- une notification indique le résultat.

Idéal pour un cybercafé, une école, un bureau ou un photocopieur où beaucoup de clés passent.
Pour l'arrêter : clic droit sur l'icône, puis **Désactiver la protection automatique**.

## Ce que le logiciel nettoie

**Sur la clé ou le disque**

- les raccourcis piégés qui lancent un script ;
- les scripts cachés (`.vbs`, `.vbe`, `.js`, `.wsf`…) et les programmes cachés à la racine ;
- les fichiers `autorun.inf` ;
- les dossiers cachés du virus (par exemple `WindowsServices`) ;
- les fichiers et dossiers rendus invisibles, qui sont réaffichés.

**Sur l'ordinateur**

- les scripts du virus en cours d'exécution (`wscript`, `cscript`, `mshta`) ;
- les lancements automatiques au démarrage (registre et dossier « Démarrage ») ;
- les tâches planifiées qui lancent un script ;
- les réglages bloqués par le virus : Gestionnaire des tâches, Éditeur du registre, option
  « Afficher les fichiers cachés ».

Le disque où Windows est installé (en général `C:`) n'est jamais traité comme une clé.

## Bon à savoir

- Mareya USB Fix cible le virus raccourci. Il ne remplace pas un antivirus : faites aussi une
  analyse complète avec **Windows Defender** (ou Malwarebytes, gratuit).
- Si un élément ne peut pas être supprimé, fermez les fichiers ouverts depuis la clé, puis
  relancez l'analyse.

## Pour les techniciens : ligne de commande

```
MareyaUSBFix.exe /scan E:\ [/system] [/repair] [/vaccinate] [/log rapport.txt]
```

| Option | Rôle |
| --- | --- |
| `/scan E:\` | lecteur à analyser |
| `/system` | analyse aussi l'ordinateur |
| `/repair` | applique les corrections (éléments du virus uniquement) |
| `/all` | avec `/repair` : inclut aussi les éléments « à vérifier » |
| `/vaccinate` | vaccine le lecteur |
| `/log fichier` | enregistre le rapport |

Code de retour : nombre d'éléments du virus restants (`0` = propre, `-1` = erreur).

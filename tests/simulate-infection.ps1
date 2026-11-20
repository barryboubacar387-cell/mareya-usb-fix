# Test de bout en bout sur Windows : simule une infection « virus raccourci »
# (inoffensive) sur un faux lecteur, lance Mareya USB Fix et vérifie le résultat.
#
#   pwsh tests/simulate-infection.ps1 -Exe chemin\MareyaUSBFix.exe
#
# À lancer en administrateur. Utilisé par l'intégration continue GitHub Actions.

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Letter = "T"
)

$ErrorActionPreference = "Stop"
$failures = [System.Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
    if ($condition) { Write-Host "  OK    $message" -ForegroundColor Green }
    else { Write-Host "  ÉCHEC $message" -ForegroundColor Red; $failures.Add($message) }
}
function IsHidden($path) {
    $a = (Get-Item -LiteralPath $path -Force).Attributes
    return ($a -band [IO.FileAttributes]::Hidden) -or ($a -band [IO.FileAttributes]::System)
}
function NewShortcut($path, $target, $arguments) {
    $shell = New-Object -ComObject WScript.Shell
    $s = $shell.CreateShortcut($path)
    $s.TargetPath = $target
    $s.Arguments = $arguments
    $s.Save()
}

$Exe = (Resolve-Path $Exe).Path
$backing = Join-Path $env:RUNNER_TEMP "fake-usb"
if (-not $env:RUNNER_TEMP) { $backing = Join-Path $env:TEMP "fake-usb" }
Remove-Item $backing -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $backing | Out-Null
subst "$($Letter):" /D 2>$null | Out-Null
subst "$($Letter):" $backing
$root = "$($Letter):\"

Write-Host "== Préparation d'une clé infectée simulée sur $root"

# Fichiers de l'utilisateur
New-Item -ItemType Directory "$root\Documents", "$root\Photos", "$root\Projet" | Out-Null
Set-Content "$root\Documents\rapport.txt" "Rapport annuel"
Set-Content "$root\Photos\photo1.jpg" "image"
Set-Content "$root\notes.txt" "mes notes"
Set-Content "$root\Projet\app.js" "console.log('projet légitime');"   # script légitime, visible : à conserver
Set-Content "$root\Photos\desktop.ini" "[.ShellClassInfo]"
attrib +h +s "$root\Photos\desktop.ini"
NewShortcut "$root\Lien bloc-notes.lnk" "C:\Windows\notepad.exe" ""          # raccourci légitime : à conserver
# installateurs de l'utilisateur, cachés par le virus (cas réel « SETUPS ») : ne doivent JAMAIS être supprimés
New-Item -ItemType Directory "$root\Installateurs" | Out-Null
Set-Content "$root\Installateurs\setup1.exe" "installateur 1"
Set-Content "$root\Installateurs\setup2.exe" "installateur 2"
attrib +h +s "$root\Installateurs\setup1.exe"
attrib +h +s "$root\Installateurs"
Set-Content "$root\outil.exe" "programme de l'utilisateur"

# --- Infection simulée ---
# 1. le virus cache les fichiers de l'utilisateur
attrib +h +s "$root\Documents"
attrib +h +s "$root\Photos"
attrib +h +s "$root\notes.txt"
attrib +h +s "$root\outil.exe"
# 2. un script caché (inoffensif)
Set-Content "$root\x8f2k.vbe" "' faux virus de test"
attrib +h +s "$root\x8f2k.vbe"
# 3. des raccourcis piégés à la place des dossiers
$payload = '/c start wscript /e:VBScript.Encode x8f2k.vbe & start explorer {0} & exit'
NewShortcut "$root\Documents.lnk" "C:\Windows\System32\cmd.exe" ($payload -f "Documents")
NewShortcut "$root\Photos.lnk" "C:\Windows\System32\cmd.exe" ($payload -f "Photos")
NewShortcut "$root\notes.txt.lnk" "C:\Windows\System32\cmd.exe" ($payload -f "notes.txt")
# 4. autorun.inf
Set-Content "$root\autorun.inf" "[autorun]`r`nopen=wscript x8f2k.vbe"
attrib +h +s "$root\autorun.inf"
# 5. dossier caché du virus
New-Item -ItemType Directory "$root\WindowsServices" | Out-Null
Set-Content "$root\WindowsServices\helper.vbs" "' test"
Set-Content "$root\WindowsServices\installer.vbs" "' test"
attrib +h +s "$root\WindowsServices"
# 6. dossier « sans nom » (espace insécable) contenant des fichiers de l'utilisateur
$blank = [string][char]0x00A0
New-Item -ItemType Directory -Path "$root$blank" | Out-Null
Set-Content -LiteralPath "$root$blank\secret.txt" "fichier déplacé par le virus"
attrib +h +s "$root$blank\secret.txt"
Set-Content -LiteralPath "$root$blank\notes.txt" "autre version des notes"   # même nom qu'un fichier de la racine
New-Item -ItemType Directory -Path "$root$blank\Vacances" | Out-Null
Set-Content -LiteralPath "$root$blank\Vacances\plage.jpg" "image"
attrib +h +s "$root$blank\Vacances"
# variante réelle : le virus a infecté plusieurs fois et emboîté « Files are hidden by Trojan »
$wrap1 = "$root$blank\Files are hidden by Trojan"
$wrap2 = "$wrap1\Files are hidden by Trojan"
New-Item -ItemType Directory -Path "$wrap2\Profond" | Out-Null
Set-Content -LiteralPath "$wrap2\Profond\doc.txt" "fichier très profond"
Set-Content -LiteralPath "$wrap1\Pilotes.zip" "zip"
# dossier protégé (cas réel « SETUPS ») : appartient à SYSTEM, refus explicite pour les Administrateurs
New-Item -ItemType Directory -Path "$wrap2\SETUPS" | Out-Null
Set-Content -LiteralPath "$wrap2\SETUPS\setup.exe" "installateur"
icacls "$wrap2\SETUPS\setup.exe" /setowner *S-1-5-18 /C /Q | Out-Null
icacls "$wrap2\SETUPS\setup.exe" /inheritance:r /grant:r *S-1-5-18:F /C /Q | Out-Null
icacls "$wrap2\SETUPS" /setowner *S-1-5-18 /C /Q | Out-Null
icacls "$wrap2\SETUPS" /inheritance:r /grant:r *S-1-5-18:F /C /Q | Out-Null
icacls "$wrap2\SETUPS" /deny *S-1-5-32-544:F /C /Q | Out-Null
foreach ($w in $wrap1, $wrap2) {
    Set-Content -LiteralPath "$w\AUTORUN_.INF" "[autorun]"
    Set-Content -LiteralPath "$w\desktop.ini" "[.ShellClassInfo]"
}
attrib +h +s "$root$blank"
# variante : nom fait d'espaces ordinaires (Windows les supprime des chemins normaux)
# (PowerShell supprime lui aussi les espaces finaux : on passe directement par .NET avec \\?\)
$spaces = "   "
$spacesDir = "\\?\$root$spaces"
[IO.Directory]::CreateDirectory($spacesDir) | Out-Null
[IO.File]::WriteAllText("$spacesDir\rapport-espaces.txt", "dans le dossier aux espaces")
[IO.File]::SetAttributes($spacesDir, [IO.FileAttributes]'Directory, Hidden, System')
Check ([IO.Directory]::Exists($spacesDir)) "(préparation) dossier aux espaces ordinaires créé"

# --- Infection simulée de l'ordinateur ---
$appData = [Environment]::GetFolderPath("ApplicationData")
$evilRun = Join-Path $appData "mareya-test-run.vbs"
$evilTask = Join-Path $appData "mareya-test-task.vbs"
$evilSleep = Join-Path $appData "mareya-test-sleep.vbs"
$startup = [Environment]::GetFolderPath("Startup")
$evilStartup = Join-Path $startup "mareya-test-startup.vbs"
Set-Content $evilRun "' test"
Set-Content $evilTask "' test"
Set-Content $evilSleep "WScript.Sleep 600000"
New-Item -ItemType Directory $startup -Force | Out-Null
Set-Content $evilStartup "' test"
# (New-Item -Force effacerait les valeurs d'une clé existante : on ne la crée que si elle manque)
if (-not (Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run")) {
    New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" | Out-Null
}
New-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "MareyaTestVirus" `
    -Value "wscript.exe //B `"$evilRun`"" -PropertyType String -Force | Out-Null
schtasks /Create /TN "MareyaTestVirusTask" /TR "wscript.exe //B `"$evilTask`"" /SC ONLOGON /F | Out-Null
if (-not (Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System")) {
    New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System" -Force | Out-Null
}
New-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System" -Name "DisableTaskMgr" -Value 1 -PropertyType DWord -Force | Out-Null
$proc = Start-Process wscript.exe -ArgumentList "//B `"$evilSleep`"" -PassThru
Start-Sleep -Seconds 2

Write-Host "== Analyse seule (ne doit rien modifier)"
$scanLog = Join-Path $backing "..\scan.log"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/system", "/log", "`"$scanLog`"" -Wait -PassThru
Get-Content $scanLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -ge 10) "l'analyse détecte l'infection (code $($p.ExitCode))"
Check (IsHidden "$root\Documents") "l'analyse seule ne modifie rien"

Write-Host "== Réparation"
$repairLog = Join-Path $backing "..\repair.log"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/system", "/repair", "/vaccinate", "/log", "`"$repairLog`"" -Wait -PassThru
Get-Content $repairLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "plus aucun élément malveillant après réparation (code $($p.ExitCode))"

Write-Host "== Vérifications : clé"
Check ((Test-Path "$root\Installateurs\setup1.exe") -and (Test-Path "$root\Installateurs\setup2.exe")) "dossier caché d'installateurs (.exe) conservé"
Check (-not (IsHidden "$root\Installateurs")) "dossier d'installateurs réaffiché"
Check (Test-Path "$root\outil.exe") "programme caché non lancé par un raccourci conservé"
$q = "$root\Quarantaine Mareya USB Fix"
Check (Test-Path -LiteralPath $q) "éléments retirés placés en quarantaine (rien n'est supprimé définitivement)"
Check (@(Get-ChildItem -LiteralPath $q -Recurse -Force -Filter "x8f2k.vbe.quarantaine").Count -eq 1) "script du virus restaurable depuis la quarantaine"
Check (-not (IsHidden "$root\Documents")) "dossier Documents de nouveau visible"
Check (-not (IsHidden "$root\Photos")) "dossier Photos de nouveau visible"
Check (-not (IsHidden "$root\notes.txt")) "fichier notes.txt de nouveau visible"
Check ((Get-Content "$root\Documents\rapport.txt") -eq "Rapport annuel") "contenu des fichiers intact"
Check (-not (Test-Path "$root\Documents.lnk")) "raccourci piégé Documents.lnk supprimé"
Check (-not (Test-Path "$root\Photos.lnk")) "raccourci piégé Photos.lnk supprimé"
Check (-not (Test-Path "$root\notes.txt.lnk")) "raccourci piégé notes.txt.lnk supprimé"
Check (-not (Test-Path "$root\x8f2k.vbe")) "script caché supprimé"
Check (-not (Test-Path "$root\WindowsServices")) "dossier du virus supprimé"
Check (Test-Path -LiteralPath "$root\secret.txt") "fichiers du dossier sans nom remis à la racine"
Check (-not (IsHidden "$root\secret.txt")) "fichiers remis à la racine visibles"
Check ((Test-Path "$root\Vacances\plage.jpg") -and -not (IsHidden "$root\Vacances")) "sous-dossier remis à la racine et visible"
Check ((Get-Content "$root\notes.txt") -eq "mes notes") "fichier existant non écrasé"
Check ((Get-Content "$root\notes (2).txt") -eq "autre version des notes") "doublon remis sous le nom « notes (2).txt »"
Check (-not (Test-Path -LiteralPath "$root$blank")) "dossier sans nom du virus supprimé"
Check (Test-Path "$root\Profond\doc.txt") "emballages « Files are hidden by Trojan » imbriqués : fichiers profonds remis à la racine"
Check (Test-Path "$root\Pilotes.zip") "fichiers du premier emballage remis à la racine"
Check ([IO.Directory]::Exists("$root\SETUPS")) "dossier protégé SETUPS remis à la racine après reprise de propriété"
Check (-not (Test-Path "$root\Files are hidden by Trojan")) "aucun emballage « Files are hidden by Trojan » laissé"
Check (-not (Test-Path "$root\AUTORUN_.INF")) "déchets du virus (AUTORUN_.INF) non remis à la racine"
Check (Test-Path "$root\rapport-espaces.txt") "dossier aux espaces ordinaires : fichiers remis à la racine"
Check (-not [IO.Directory]::Exists($spacesDir)) "dossier aux espaces ordinaires supprimé"
Check (-not (Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -like '* (2)*' -and $_.Name -ne 'notes (2).txt' })) "aucun élément de la racine renommé « (2) » par erreur"
Check (-not (Test-Path -LiteralPath "$root\Fichiers récupérés")) "pas de dossier « Fichiers récupérés » laissé"
$walked = (Select-String -Path $scanLog -Pattern '(\d+) éléments parcourus').Matches[0].Groups[1].Value -as [int]
Check ($walked -lt 40) "la clé n'est parcourue qu'une fois ($walked éléments)"
Check (Test-Path "$root\Projet\app.js") "script légitime visible conservé"
Check (Test-Path "$root\Lien bloc-notes.lnk") "raccourci légitime conservé"
Check (IsHidden "$root\Photos\desktop.ini") "desktop.ini laissé caché"
Check ((Test-Path "$root\autorun.inf" -PathType Container)) "clé vaccinée (dossier autorun.inf protégé)"

Write-Host "== Dossier « Fichiers récupérés » laissé par une ancienne version"
New-Item -ItemType Directory "$root\Fichiers récupérés" | Out-Null
Set-Content "$root\Fichiers récupérés\ancien.txt" "ancien"
New-Item -ItemType Directory "$root\Fichiers récupérés\Musique\Album" | Out-Null   # sous-dossiers (cas réel)
Set-Content "$root\Fichiers récupérés\Musique\Album\titre.mp3" "son"
New-Item -ItemType Directory "$root\`$RECYCLE.BIN" | Out-Null   # Corbeille rendue visible
$oldLog = Join-Path $backing "..\old.log"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/repair", "/log", "`"$oldLog`"" -Wait -PassThru
Get-Content $oldLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "réparation réussie (code $($p.ExitCode))"
Check ((Test-Path "$root\ancien.txt") -and -not (Test-Path "$root\Fichiers récupérés")) "fichiers de « Fichiers récupérés » remis à la racine, dossier supprimé"
Check (Test-Path "$root\Musique\Album\titre.mp3") "sous-dossiers de « Fichiers récupérés » remis à la racine"
Check (IsHidden "$root\`$RECYCLE.BIN") "Corbeille de Windows recachée"

Write-Host "== Fichier ouvert pendant la réparation"
New-Item -ItemType Directory "$root\Fichiers récupérés" | Out-Null
Set-Content "$root\Fichiers récupérés\libre.txt" "libre"
Set-Content "$root\Fichiers récupérés\ouvert.txt" "ouvert"
$lock = [IO.File]::Open("$root\Fichiers récupérés\ouvert.txt", 'Open', 'Read', 'None')
$lockLog = Join-Path $backing "..\lock.log"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/repair", "/log", "`"$lockLog`"" -Wait -PassThru
Get-Content $lockLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -ne 0) "le fichier bloqué est signalé (code $($p.ExitCode))"
Check (Test-Path "$root\libre.txt") "les autres fichiers sont quand même remis à leur place"
Check (Select-String -Path $lockLog -Pattern 'ouvert.txt' -Quiet) "le journal nomme le fichier bloqué"
$lock.Close()
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/repair", "/log", "`"$lockLog`"" -Wait -PassThru
Check ($p.ExitCode -eq 0) "une fois le fichier fermé, la réparation se termine (code $($p.ExitCode))"
Check ((Test-Path "$root\ouvert.txt") -and -not (Test-Path "$root\Fichiers récupérés")) "fichier remis et dossier supprimé"

Write-Host "== Disque propre avec des fichiers que Windows garde cachés"
Set-Content "$root\pagefile.sys" "x"
attrib +h +s "$root\pagefile.sys"
New-Item -ItemType Directory "$root\`$Windows.~BT" | Out-Null
attrib +h +s "$root\`$Windows.~BT"
New-Item -ItemType Directory "$root\Projet\.git" | Out-Null
attrib +h "$root\Projet\.git"
$cleanLog = Join-Path $backing "..\clean.log"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/log", "`"$cleanLog`"" -Wait -PassThru
Get-Content $cleanLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "aucun faux virus signalé sur un disque propre (code $($p.ExitCode))"
Check (-not (Select-String -Path $cleanLog -Pattern 'Fichiers cachés' -Quiet)) "fichiers système et .git non proposés au réaffichage"

Write-Host "== Dossiers found.000 de Windows (protégés, propriété du système)"
foreach ($n in "found.000", "found.000 (2)") {
    New-Item -ItemType Directory "$root\$n\dir0000.chk\AadRecoveryPasswordDelete" | Out-Null
    Set-Content "$root\$n\FILE0000.CHK" "fragment"
    Set-Content "$root\$n\dir0000.chk\AadRecoveryPasswordDelete\data.bin" "x"
    attrib +h +s "$root\$n"
    # comme sur un vrai disque : chaque niveau appartient à SYSTEM et les Administrateurs n'ont aucun droit
    # (cas réel : dossier de « System Volume Information » récupéré par chkdsk)
    foreach ($sub in "$root\$n\dir0000.chk\AadRecoveryPasswordDelete\data.bin", "$root\$n\dir0000.chk\AadRecoveryPasswordDelete",
                     "$root\$n\dir0000.chk", "$root\$n\FILE0000.CHK", "$root\$n") {
        icacls $sub /setowner *S-1-5-18 /C /Q | Out-Null
        icacls $sub /inheritance:r /grant:r *S-1-5-18:F /C /Q | Out-Null
        icacls $sub /deny *S-1-5-32-544:F /C /Q | Out-Null   # refus explicite pour les Administrateurs
    }
}
New-Item -ItemType Directory "$root\found.009\x" | Out-Null       # copie jetable pour vérifier la difficulté
icacls "$root\found.009\x" /deny *S-1-5-32-544:F /C /Q | Out-Null
icacls "$root\found.009" /deny *S-1-5-32-544:F /C /Q | Out-Null
cmd /c rd /s /q "\\?\$($root)found.009" 2>$null
Write-Host "    (info) rd /s /q sur un dossier protégé : $(if (Test-Path "$root\found.009") { 'refusé, comme chez l''utilisateur' } else { 'réussi sur cette machine' })"
$foundLog = Join-Path $backing "..\found.log"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/log", "`"$foundLog`"" -Wait -PassThru
Check ($p.ExitCode -eq 0) "found.000 n'est pas compté comme virus (code $($p.ExitCode))"
Check ((Select-String -Path $foundLog -Pattern '\[SUSPECT\] Dossier Windows' -AllMatches).Matches.Count -ge 2) "les dossiers found sont proposés, non cochés"
Check (IsHidden "$root\found.000") "found.000 n'est pas réaffiché"
$p = Start-Process $Exe -ArgumentList "/scan", $root, "/repair", "/all", "/log", "`"$foundLog`"" -Wait -PassThru
Get-Content $foundLog | Select-String "found" | ForEach-Object { Write-Host "    $_" }
Check (-not (Test-Path "$root\found.000") -and -not (Test-Path "$root\found.000 (2)") -and -not (Test-Path "$root\found.009")) "dossiers found protégés supprimés quand on les coche"

Write-Host "== Quarantaine : rapport et restauration"
Check (@(Get-ChildItem -LiteralPath "$root\Quarantaine Mareya USB Fix" -Filter "rapport*.txt").Count -ge 1) "rapport de réparation enregistré"
$restoreLog = Join-Path $backing "..\restore.log"
$p = Start-Process $Exe -ArgumentList "/restore", $root, "/log", "`"$restoreLog`"" -Wait -PassThru
Get-Content $restoreLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "restauration sans erreur (code $($p.ExitCode))"
Check (Test-Path "$root\x8f2k.vbe") "fichier restauré à son emplacement d'origine, avec son vrai nom"
Check (Test-Path "$root\WindowsServices\helper.vbs") "dossier restauré avec son contenu"
Check (Test-Path "$root\Documents.lnk") "raccourci restauré"

Write-Host "== Diagnostic : santé du disque (lecture seule)"
$healthLog = Join-Path $backing "..\health.log"
$p = Start-Process $Exe -ArgumentList "/health", "C:\", "/log", "`"$healthLog`"" -Wait -PassThru
Get-Content $healthLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "diagnostic de santé sans erreur (code $($p.ExitCode))"
Check (Select-String -Path $healthLog -Pattern '^Verdict : ' -Quiet) "un verdict de santé est donné"
Check (Select-String -Path $healthLog -Pattern 'Modèle' -Quiet) "l'identité du disque est lue"
Check (Select-String -Path $healthLog -Pattern '^Santé : ' -Quiet) "le pourcentage de santé (ou « inconnue ») est donné"
$selfLog = Join-Path $backing "..\selftest.log"
$p = Start-Process $Exe -ArgumentList "/selftest", "/log", "`"$selfLog`"" -Wait -PassThru
Get-Content $selfLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "calcul du pourcentage de santé juste sur 10 cas connus (code $($p.ExitCode))"

Write-Host "== Diagnostic : test d'authenticité sur un vrai disque virtuel (VHD de 300 Mo)"
$vhd = [IO.Path]::GetFullPath((Join-Path $backing "..\capacite.vhdx"))   # diskpart exige un chemin complet
Remove-Item $vhd -Force -ErrorAction SilentlyContinue
$dp = Join-Path $backing "..\diskpart.txt"
@"
create vdisk file="$vhd" maximum=300 type=expandable
attach vdisk
create partition primary
format fs=ntfs quick label=TESTCAP
assign letter=V
"@ | Set-Content -Encoding ascii $dp
diskpart /s $dp | ForEach-Object { Write-Host "    diskpart> $_" }
for ($i = 0; $i -lt 20 -and -not (Test-Path "V:\"); $i++) { Start-Sleep -Milliseconds 500 }
Check (Test-Path "V:\") "(préparation) disque virtuel V: monté"
Set-Content "V:\mon-document.txt" "document personnel"
$hash = (Get-FileHash "V:\mon-document.txt").Hash
$capLog = Join-Path $backing "..\captest.log"
$p = Start-Process $Exe -ArgumentList "/captest", "V:\", "/log", "`"$capLog`"" -Wait -PassThru
Get-Content $capLog | ForEach-Object { Write-Host "    $_" }
Check ($p.ExitCode -eq 0) "disque authentique reconnu comme tel (code $($p.ExitCode))"
Check (Select-String -Path $capLog -Pattern 'AUTHENTIQUE' -Quiet) "verdict « AUTHENTIQUE » affiché"
Check (Select-String -Path $capLog -Pattern 'écriture [0-9]' -Quiet) "vitesses réelles mesurées"
Check (-not (Test-Path "V:\Mareya Test Capacite")) "fichiers de test supprimés à la fin"
Check ((Get-FileHash "V:\mon-document.txt").Hash -eq $hash) "fichier personnel intact"
$p = Start-Process $Exe -ArgumentList "/captest", "V:\", "/speed", "/log", "`"$capLog`"" -Wait -PassThru
Check ($p.ExitCode -eq 0) "test de vitesse rapide sans erreur (code $($p.ExitCode))"
$p = Start-Process $Exe -ArgumentList "/captest", "C:\", "/limit", "1", "/log", "`"$capLog`"" -Wait -PassThru
Check ($p.ExitCode -eq -1) "test d'authenticité refusé sur le disque de Windows"
@"
select vdisk file="$vhd"
detach vdisk
"@ | Set-Content -Encoding ascii $dp
diskpart /s $dp | Out-Null

Write-Host "== Vérifications : ordinateur"
$run = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "MareyaTestVirus" -ErrorAction SilentlyContinue
Check ($null -eq $run) "entrée de démarrage du registre supprimée"
Check (-not (Test-Path $evilRun)) "script lancé par le registre supprimé"
schtasks /Query /TN "MareyaTestVirusTask" 2>$null | Out-Null
Check ($LASTEXITCODE -ne 0) "tâche planifiée du virus supprimée"
Check (-not (Test-Path $evilTask)) "script de la tâche planifiée supprimé"
Check (-not (Test-Path $evilStartup)) "script du dossier Démarrage supprimé"
$tm = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System" -Name "DisableTaskMgr" -ErrorAction SilentlyContinue
Check ($null -eq $tm) "Gestionnaire des tâches débloqué"
Check ($proc.HasExited -or -not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) "processus wscript du virus arrêté"

# Nettoyage
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
subst "$($Letter):" /D | Out-Null

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) vérification(s) en échec." -ForegroundColor Red
    exit 1
}
Write-Host "Toutes les vérifications sont passées." -ForegroundColor Green

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

# --- Infection simulée ---
# 1. le virus cache les fichiers de l'utilisateur
attrib +h +s "$root\Documents"
attrib +h +s "$root\Photos"
attrib +h +s "$root\notes.txt"
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
attrib +h +s "$root$blank"

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
Check (-not (IsHidden "$root\Documents")) "dossier Documents de nouveau visible"
Check (-not (IsHidden "$root\Photos")) "dossier Photos de nouveau visible"
Check (-not (IsHidden "$root\notes.txt")) "fichier notes.txt de nouveau visible"
Check ((Get-Content "$root\Documents\rapport.txt") -eq "Rapport annuel") "contenu des fichiers intact"
Check (-not (Test-Path "$root\Documents.lnk")) "raccourci piégé Documents.lnk supprimé"
Check (-not (Test-Path "$root\Photos.lnk")) "raccourci piégé Photos.lnk supprimé"
Check (-not (Test-Path "$root\notes.txt.lnk")) "raccourci piégé notes.txt.lnk supprimé"
Check (-not (Test-Path "$root\x8f2k.vbe")) "script caché supprimé"
Check (-not (Test-Path "$root\WindowsServices")) "dossier du virus supprimé"
Check (Test-Path -LiteralPath "$root\Fichiers récupérés\secret.txt") "dossier sans nom renommé « Fichiers récupérés »"
Check (-not (IsHidden "$root\Fichiers récupérés\secret.txt")) "fichiers du dossier sans nom réaffichés"
$walked = (Select-String -Path $scanLog -Pattern '(\d+) éléments parcourus').Matches[0].Groups[1].Value -as [int]
Check ($walked -lt 40) "la clé n'est parcourue qu'une fois ($walked éléments)"
Check (Test-Path "$root\Projet\app.js") "script légitime visible conservé"
Check (Test-Path "$root\Lien bloc-notes.lnk") "raccourci légitime conservé"
Check (IsHidden "$root\Photos\desktop.ini") "desktop.ini laissé caché"
Check ((Test-Path "$root\autorun.inf" -PathType Container)) "clé vaccinée (dossier autorun.inf protégé)"

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

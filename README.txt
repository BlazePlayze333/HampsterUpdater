HAMSTER UPDATER - REAL C# / WINFORMS PROJECT
Not a PS1 script. Source package, NOT precompiled. No compiler/Windows runtime
is available in the authoring environment. GitHub or your PC must build it.

IMPORTANT: USE A SEPARATE PROJECT/REPOSITORY
Create a NEW repository, e.g. HamsterUpdater. Upload the CONTENTS of this ZIP
to that repo, including .github/workflows/build.yml, and commit to main/master.
Do NOT overwrite DesktopHamster's Program.cs with this updater's Program.cs.
Do NOT put these sources in the hamster project tree: default C# globbing can
compile both entry points. The two apps are separate projects.
Your existing BlazePlayze333/Hampster repository stays unchanged.

GitHub Actions > Build Hamster Updater builds and publishes HamsterUpdater.exe
in this NEW repository's Releases. Download that EXE, not Source code ZIP.
Workflow needs repository policy to allow contents:write. Its default token
is sufficient; no personal token is embedded. Repository must be controlled
by you. Keep code free of secrets.

LOCAL BUILD ALTERNATIVE (.NET 10 SDK)
dotnet publish HamsterUpdater.csproj -c Release -r win-x64 --self-contained true -o ReadyToRun
A fresh publish folder contains one HamsterUpdater.exe. No .ps1, .bat or
companion runtime DLLs needed to run it. Native runtime files can be extracted
into .NET's temporary cache automatically at startup.

RUNNING
Stop older updaters first. Use Stop-Background-Updates.vbs for the old hidden
watcher, Exit updater for prior GUI/tray updaters, or close old console.
Place the BUILT HamsterUpdater.exe beside DesktopHamster.exe. Double-click it.
It starts in the system tray with a generic information icon, no normal window
or taskbar button. Check the hidden-icons arrow if necessary.
Click icon for popup: status, countdown, seconds field (default 30), Apply,
Check now, Pause/Resume, Exit updater. Click away or X hides popup, not updater.
Right-click tray icon also offers commands. Apply saves chosen interval.
Range 15..86400 seconds. Pause stops future checks, not an active replacement.
Exit waits for an active operation to finish safely. No startup task/service
is installed. Launch again after sign-out/reboot.

UPDATES
Checks public BlazePlayze333/Hampster/releases/latest, NOT this new updater
repository. Your existing hamster workflow still builds DesktopHamster.exe.
The updater downloads DesktopHamster.exe release assets only and does NOT
update itself. If rebuilding updater, download its EXE manually.
Uses website redirect, not REST API, for recurring checks. Ordinary website
throttling/network failures remain possible; increase interval if needed.
Same release tag + same installed SHA-256: no download or restart. First check
or new tag may download once; identical content is not replaced/restarted.
Publish a NEW tag for changed builds. Swapping an asset under the same tag
is not detected while the cached hash still matches the installed EXE.

Actual change: cleanly close this folder's hamster, keep one backup as
DesktopHamster.previous.exe, replace EXE atomically where filesystem supports,
then launch. No saved care data changes. If closing or replacing fails, the
status reports the error; exit hamster via its tray menu and retry Check now.

Preferences/cache created locally: .hamster-csharp-updater-settings.json and
.hamster-csharp-updater-release.json. These are data, not runtime dependencies.
Own instance protection prevents duplicate C# updater for the same directory.
Older PowerShell watchers must be stopped manually; don't run both.
Hash comparisons identify changed content, not trusted authorship/signatures.
Only run releases you trust; repository compromise can distribute harmful code.

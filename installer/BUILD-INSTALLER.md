# Building the RDP Manager installer

Two steps: publish a self-contained app, then wrap it into a `setup.exe`.

## Step 1 — Publish (self-contained, runs on any Win10/11 x64)

From the solution root (`E:\Workspace\remote-desktop\RdpManager`):

```powershell
dotnet publish src\RdpManager.Presentation\RdpManager.Presentation.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None
```

Output folder:
```
src\RdpManager.Presentation\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\
```

This bundles the .NET runtime **and** the Windows App SDK, so the target machine needs nothing pre-installed. Test it by double-clicking `RdpManager.Presentation.exe` in that folder.

> Want a smaller download instead? Drop `--self-contained true` and `-p:WindowsAppSDKSelfContained=true`. The app is then framework-dependent and the target machine needs .NET 9 Desktop Runtime + Windows App SDK 1.7 installed.

## Step 2 — Make setup.exe with Inno Setup (free)

1. Install Inno Setup: https://jrsoftware.org/isdl.php
2. Open `installer\RdpManager.iss` in the Inno Setup Compiler and press **Build** (or run `iscc installer\RdpManager.iss` from a terminal).
3. The installer appears at `installer\Output\RdpManager-Setup.exe`.

It installs per-user (no admin needed), adds Start-menu and optional desktop shortcuts, and registers an uninstaller.

## Alternatives

- **Just a zip:** zip the `publish\` folder and share it — users unzip and run the `.exe`. No installer needed.
- **MSIX (Store/enterprise):** add a *Windows Application Packaging Project* to the solution, reference `RdpManager.Presentation`, and build the `.msix`. Requires a code-signing certificate to install. Heavier than Inno Setup but is the native Windows package format.

## Notes

- Update `AppVersion` in `RdpManager.iss` for each release.
- To sign the installer (removes SmartScreen warnings), sign both the app `.exe` and the produced `setup.exe` with `signtool` using your code-signing cert.

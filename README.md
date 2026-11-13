# Hover Haptics

Windows Logi Options+ plugin. One haptic pulse when the pointer rests on a window edge or a taskbar control.

## Build and install

Install current Logi Options+ and the .NET 10 SDK. In this folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The first build downloads LogiPluginTool. Double-click `HoverHaptics.lplug4` to install, then in Options+ enable haptics for app plugins and Hover Haptics. No button assignment is needed.

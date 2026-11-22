# PhoneBridge

PhoneBridge runs a Windows launcher and a separate WPF game window for an Android phone connected over USB. Milestone A is an experimental primary-display mirror built on the Phase 1 ADB device manager.

## Build on Windows 11

Install the .NET 8 SDK, then open PowerShell at the solution root.

```powershell
dotnet test ./tests/PhoneBridge.Core.Tests/PhoneBridge.Core.Tests.csproj
dotnet publish ./src/PhoneBridge.Wpf/PhoneBridge.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The publish directory is `src/PhoneBridge.Wpf/bin/Release/net8.0-windows/win-x64/publish/`. Keep the published native DLLs beside `PhoneBridge.exe`. Place the following user-supplied tools in that same directory tree.

```text
publish/
  PhoneBridge.exe
  tools/
    scrcpy-server
    ffmpeg.exe
    platform-tools/
      adb.exe
      ...remaining platform-tools files
```

- Copy the extensionless `scrcpy-server` from the Windows archive for scrcpy **3.3.4**. The server protocol is version-specific. [scrcpy 3.3.4 release](https://github.com/Genymobile/scrcpy/releases/tag/v3.3.4)
- Use a recent Windows x64 FFmpeg build with H.264 and HEVC decoders and the BMP encoder. A standalone build is the simplest option. [FFmpeg downloads](https://ffmpeg.org/download.html#build-windows)
- Extract the Android platform-tools folder intact. PhoneBridge also supports finding ADB on PATH or selecting it in launcher Settings. [Android platform-tools](https://developer.android.com/tools/releases/platform-tools)

For a Debug build from Visual Studio 2022, use the .NET desktop development workload and place the tools beside the Debug executable instead.

## Start a mirror

1. Enable **USB debugging** in HyperOS developer options. Connect the phone with a data cable, unlock it, and accept the USB debugging prompt.
2. Run `PhoneBridge.exe`. Open **Devices**, select the phone, press **Connect**, then **Open Game Window**.
3. Use **F11** for borderless fullscreen and **F12** to toggle the toolbar. **Rotate View** changes the PC view orientation. **Screenshot** saves the current frame in its source orientation.
4. To change the FPS cap, resolution cap, bitrate, or codec, stop the stream, open **Stream Settings**, choose the new values, then start it again.
5. After a disconnect, reconnect the same device in the launcher and start the stream again. Close the game window and reopen it for a different device. Use **Diagnostics** for ADB, scrcpy server, and FFmpeg messages.

## Touch and key mapping preview

- Click or drag on the video to touch the Android screen. Click the video to focus it before using a binding.
- Open **Keymapping Editor**, click a point in the video, then press **A–Z** or **0–9**. Press the same key at another point to move it, or remove a binding using the list. Click **Done Editing** to use the bindings.
- A bound key holds its touch until release, including when other keys or the mouse are used at the same time. This preview maps keys to fixed touch positions. Joystick movement and camera aiming are pending.
- Bindings are saved for the Windows user in `%APPDATA%\PhoneBridge\keymapping.json`. Positions are normalized to the video. For games that change orientation or layout, verify the positions before using them.
- If the image works but touch does not on Xiaomi/HyperOS, check whether developer options has a separate **USB debugging (Security settings)** switch, then reconnect the USB session. Check **Diagnostics** for Android input permission errors.

## Milestone A status

The game window renders the physical Android display inside WPF. ADB forwards a per-session loopback port to the pinned Android server. FFmpeg decodes the H.264 or HEVC stream on the CPU and sends BMP frames over a pipe. A single pending frame slot limits the WPF-side queue. The FPS counter measures frames submitted to WPF and the skipped counter measures frames superseded in that slot. Encoder drops, monitor scan-out and end-to-end latency are unmeasured.

The stream uses a software decoding and CPU transfer path. 90 and 120 FPS are selectable caps. Actual throughput depends on phone capture, encoding, decoder and WPF performance. The mirror carries video and touch control. Audio and volume control are pending.

| Changed area | Files |
|---|---|
| Game window, aspect ratio, toolbar, status and metrics | `GameWindow.xaml`, `GameWindow.xaml.cs` |
| ADB forwarding, decoder integration and stream cleanup | `Streaming/ScrcpyMirror.cs` |
| Launcher integration, session identity and shutdown | `MainWindow.xaml`, `MainWindow.xaml.cs`, `Services/SessionService.cs`, `App.xaml.cs` |
| Launcher copy | `ViewModels/PageViewModels.cs`, `Views/HomeView.xaml` |

Linux verification covered a Windows-targeted .NET 8 solution build, the self-contained win-x64 publish, all 12 existing Core tests, and the FFmpeg output framing for synthetic H.264 and HEVC streams. The WPF application and real USB pipeline require Windows and a physical phone for runtime acceptance.

### Windows and Redmi K70 acceptance checks

- Verify portrait and landscape playback and aspect ratio while resizing, maximizing and using fullscreen. Confirm that the toolbar stays outside the video and that screenshots save.
- Try both codecs and each FPS cap during motion. Record the actual submissions, skipped frames and CPU usage at each size. Test idle-to-motion and physical rotation.
- Unplug while starting, streaming, and stopping. Reconnect and restart. Verify that closing either window releases the associated forwarding rule and FFmpeg process. Check failure messages with a missing tool or an incompatible server.
- If ADB reports unauthorized or offline, reauthorize USB debugging, try a different data cable or USB port, and run the Diagnostics check.

## Implementation sequence

| Milestone | Work and acceptance |
|---|---|
| A · Game window | Validate the current in-window primary-display prototype on Windows and Redmi K70. |
| B · Virtual display | Detect the backend version and capabilities. Enumerate launchable apps and validate scrcpy’s `--new-display` and `--start-app` flow, including density, system decorations and teardown. Check what keep-active or wake options the pinned release exposes and test screen-off behavior separately from display creation. Verify phone multitasking and offer primary-display mirroring if creation or app launch fails. |
| C · Input | Validate the primary-display touch transport and fixed-position key bindings on real hardware. Expand physical input, profile support, and visualization. |
| D · FPS/TPS | Add relative mouse capture, WASD, camera drag and concurrent action bindings. Verify boundary reset and focus-loss cleanup. |
| E · MOBA | Add cursor-directed joystick movement, skill aim and quick and normal casts. Verify movement plus aiming using separate contacts and normalized calibration. |
| F · Editor | Add draggable overlay markers, editable built-in profiles and validated JSON import/export. |
| G · Performance and packaging | Compare the current CPU path with D3D11 hardware decode and GPU presentation, instrument timing and drops, then test sustained 120 FPS and packaging on the target hardware. |

[Upstream virtual display guide](https://github.com/Genymobile/scrcpy/blob/v3.3.4/doc/virtual_display.md) · [Upstream device controls](https://github.com/Genymobile/scrcpy/blob/v3.3.4/doc/device.md)

## Third-party licenses

| Component | Terms |
|---|---|
| scrcpy server | Apache-2.0. [Upstream license](https://github.com/Genymobile/scrcpy/blob/v3.3.4/LICENSE) |
| FFmpeg | LGPL or GPL depending on build configuration. [FFmpeg legal information](https://ffmpeg.org/legal.html) |
| Android platform-tools | ADB includes Apache-2.0 code. Review the Android SDK terms and notices supplied with the distribution. [Platform-tools](https://developer.android.com/tools/releases/platform-tools) |
| CommunityToolkit.Mvvm and Microsoft.Extensions | MIT |
| xUnit | Apache-2.0 |

The source archive excludes third-party executables.

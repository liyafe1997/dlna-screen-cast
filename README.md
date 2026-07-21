# English [(简体中文)](#简体中文)

# DLNA Screen Casting

Cast your entire Windows desktop over DLNA. This is especially useful when your TV or computer (Wi-Fi adapter) has poor Miracast support.

This project was brought to life with Claude Code and Codex.

## Download and Installation

Download the latest release from the [Releases](https://github.com/liyafe1997/dlna-screen-cast/releases) page.

Both MSIX and traditional `.exe` installers are available. Most users should download the `.exe` installer. Choose the x64 or ARM64 version according to your processor architecture.

Because the MSIX package is unsigned, it must be installed from an administrator PowerShell session by running `Add-AppxPackage -AllowUnsigned DLNAScreenCast_1.0.0.0_x64.msix`.

# How to Build (Not Required for Regular Users)

## Development Environment

- Windows 11 x64/ARM64 (this project is developed on Windows on Arm)
- .NET SDK 10.0.302 or a newer version in the same feature band
- Currently supported Visual Studio, Windows SDK, and Windows App SDK toolchains

## Build and Test

```powershell
dotnet restore DesktopDlnaCast.sln
dotnet build DesktopDlnaCast.sln --configuration Release --no-restore
dotnet test DesktopDlnaCast.sln --configuration Release --no-build
```

With MSVC and the Windows SDK installed, build the native ABI using the separate, reproducible CMake presets (`x64` and `arm64` each have a corresponding set of presets):

```powershell
cmake --preset native-x64-release      # or native-arm64-release
cmake --build --preset native-x64-release
ctest --preset native-x64-release      # arm64 ctest must run on an ARM64 host
```

Run the GUI:

```powershell
dotnet run --project src/DesktopDlnaCast.App/DesktopDlnaCast.App.csproj -c Release
```

The **Refresh Devices** button performs SSDP discovery across multiple network adapters. After selecting a device, **Test TV** plays the test clip included in the repository. Windows Firewall must allow inbound connections to the application on the selected private network adapter.

For the standalone MockRenderer CLI, fault-injection options, and test query API, see [tools/MockRenderer/README.md](tools/MockRenderer/README.md). For protocol and security boundaries, see [docs/protocol-notes.md](docs/protocol-notes.md).

## One-Click Build and Packaging

The following scripts build the project and produce MSIX and NSIS (traditional `.exe`) installer packages.

### MSIX Packaging

Run `build-release-msix-x64.bat` or `build-release-msix-arm64.bat`.

Both entry points are thin wrappers around `packaging/scripts/build-msix.ps1 -Architecture <arch>`.

Architecture-suffixed `.msix` and `.msixbundle` files are generated in `out/msix/artifacts/`. They are unsigned by default and can be installed from an administrator PowerShell session with `Add-AppxPackage -AllowUnsigned`; pass `-CertificatePath` when signing is required. The packages are fully self-contained and include the .NET Desktop Runtime, ASP.NET Core, Windows App SDK, native media core, and VC++ CRT. During installation, the system automatically registers inbound firewall allow rules for the executable with `Profile="all"` (TCP/UDP), so no manual firewall configuration is required. For signing and installation instructions, see [docs/packaging-msix.md](docs/packaging-msix.md).

### NSIS Installer (Traditional `.exe`)

Run `build-release-nsis-x64.bat` or `build-release-nsis-arm64.bat` (both are thin wrappers around `packaging/scripts/build-nsis.ps1 -Architecture <arch>`). The scripts generate `DLNAScreenCast_<version>_<arch>_Setup.exe` in `out/nsis/artifacts/` and require NSIS 3.x (`winget install NSIS.NSIS`). The arm64 installer can only be installed on Windows on ARM64; the x64 installer can still be installed and run through emulation on ARM64 systems. The installed contents are the same as those in the MSIX package and are fully self-contained. In addition:

- During installation, `netsh advfirewall` registers inbound allow rules for the application executable on **all network profiles** (`profile=any`, including Public) for TCP and UDP. This allows casting to work even when a home network is identified as a public network. The rules are removed automatically during uninstallation.
- When upgrading or installing over an existing installation, the previous version is uninstalled silently and automatically; no manual uninstallation is required.

## Repository Structure

- `src/DesktopDlnaCast.App`: WinUI 3 views, ViewModels, and application lifecycle
- `src/DesktopDlnaCast.Core`: use cases, explicit session state machine, interfaces, and error models
- `src/DesktopDlnaCast.Upnp`: SSDP, Device Description, SOAP, AVTransport, ConnectionManager, and DIDL-Lite
- `src/DesktopDlnaCast.Streaming`: Kestrel test-stream publishing, route-aware network adapter selection, tokens, and the MPEG-TS inspector
- `src/DesktopDlnaCast.Media.Interop`: managed/native ABI boundary
- `src/DesktopDlnaCast.Media.Native`: ABI v4 native media core, validated with real x64 WGC/H.264/AAC/MPEG-TS capture
- `tools/MockRenderer`: authoritative automated DMR
- `tools/StreamProbe`: bounded MPEG-TS/PTS/IDR command-line inspection tool
- `tools/NativeCaptureProbe`: explicit-output, time-bounded native capture acceptance tool for Milestone 2
- `tools/NativeCastProbe`: diskless native capture → HTTP → MockRenderer end-to-end acceptance tool


# 简体中文 

# DLNA 投屏

通过 DLNA 协议来投放你的整个Windows桌面。对于电视或电脑（Wi-Fi网卡）对Miracast支持不佳的情况非常有用。

该项目使用 Claude Code 和 Codex 落地。

## 下载和安装
前往 [Release](https://github.com/liyafe1997/dlna-screen-cast/releases) 下载。

目前提供MSIX和传统exe安装包两种格式，一般用户下载.exe的安装即可。根据自己的处理器架构下载x64或ARM64版本。

MSIX由于没有签名，必须以管理员权限执行 `Add-AppxPackage -AllowUnsigned DLNAScreenCast_1.0.0.0_x64.msix` 的方式来安装。

# 如何编译（普通用户不必看）

## 开发环境

- Windows 11 x64/ARM64（该项目就是在WoA上开发的）
- .NET SDK 10.0.302 或同一 feature band 的更新版本
- 当前受支持的 Visual Studio、Windows SDK 与 Windows App SDK 工具链

## 构建与测试

```powershell
dotnet restore DesktopDlnaCast.sln
dotnet build DesktopDlnaCast.sln --configuration Release --no-restore
dotnet test DesktopDlnaCast.sln --configuration Release --no-build
```

具备 MSVC/Windows SDK 后，原生 ABI 使用独立的可复现 CMake Preset 构建（`x64` 与 `arm64` 各有一组同名 Preset）：

```powershell
cmake --preset native-x64-release      # 或 native-arm64-release
cmake --build --preset native-x64-release
ctest --preset native-x64-release      # arm64 的 ctest 需在 ARM64 主机上执行
```

运行 GUI：

```powershell
dotnet run --project src/DesktopDlnaCast.App/DesktopDlnaCast.App.csproj -c Release
```

应用中的“刷新设备”执行多网卡 SSDP 搜索；选择设备后，“测试电视”会播放仓库内置测试片段。Windows Firewall 必须允许应用所选私有网卡上的入站连接。

MockRenderer 的独立 CLI、故障注入参数和测试查询 API 见 [tools/MockRenderer/README.md](tools/MockRenderer/README.md)。协议与安全边界见 [docs/protocol-notes.md](docs/protocol-notes.md)。

## 一键编译 & 打包
以下脚本会执行build和打出MSIX/NSIS（传统.exe）安装包

### MSIX 打包

运行 `build-release-msix-x64.bat` / `build-release-msix-arm64.bat`。

两个入口都是 `packaging/scripts/build-msix.ps1 -Architecture <arch>` 的薄封装。

在 `out/msix/artifacts/` 产出带架构后缀的 `.msix` 与 `.msixbundle`（默认不签名，管理员 PowerShell 用 `Add-AppxPackage -AllowUnsigned` 安装；需要签名时传 `-CertificatePath`）。包为完全自包含（.NET Desktop Runtime、ASP.NET Core、Windows App SDK、原生媒体核心与 VC++ CRT 全部随包），安装时系统自动为 exe 注册 `Profile="all"` 的防火墙入站放行规则（TCP/UDP），无需用户手动放行。签名与安装说明见 [docs/packaging-msix.md](docs/packaging-msix.md)。

### NSIS 安装包（传统 .exe）

运行 `build-release-nsis-x64.bat` / `build-release-nsis-arm64.bat`（两者都是 `packaging/scripts/build-nsis.ps1 -Architecture <arch>` 的薄封装）。在 `out/nsis/artifacts/` 产出 `DLNAScreenCast_<版本>_<arch>_Setup.exe`（需要 NSIS 3.x：`winget install NSIS.NSIS`）。arm64 安装包只允许安装到 Windows on ARM64；x64 安装包在 ARM64 系统上仍可通过转译安装运行。安装内容与 MSIX 相同（完全自包含），并且：

- 安装时通过 `netsh advfirewall` 为应用 exe 注册**所有网络配置文件**（`profile=any`，含 Public）的入站放行规则（TCP/UDP），保证家庭网络被识别为“公用网络”时也能正常投屏；卸载时自动删除该规则；
- 升级/覆盖安装时自动静默卸载旧版本，无需手动卸载。

## 仓库结构

- `src/DesktopDlnaCast.App`：WinUI 3 View、ViewModel 和应用生命周期
- `src/DesktopDlnaCast.Core`：用例、显式会话状态机、接口和错误模型
- `src/DesktopDlnaCast.Upnp`：SSDP、Device Description、SOAP、AVTransport、ConnectionManager 和 DIDL-Lite
- `src/DesktopDlnaCast.Streaming`：Kestrel 测试流发布、路由网卡选择、Token 和 MPEG-TS 检查器
- `src/DesktopDlnaCast.Media.Interop`：托管/原生 ABI 边界
- `src/DesktopDlnaCast.Media.Native`：ABI v4、已通过真实 x64 WGC/H.264/AAC/MPEG-TS 捕获验收的原生媒体核心
- `tools/MockRenderer`：权威自动化 DMR
- `tools/StreamProbe`：有界 MPEG-TS/PTS/IDR 命令行检查工具
- `tools/NativeCaptureProbe`：显式输出、有限时长的 Milestone 2 原生捕获验收工具
- `tools/NativeCastProbe`：不落盘的原生捕获→HTTP→MockRenderer 端到端验收工具

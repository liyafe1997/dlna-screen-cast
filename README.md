[☕Buy me a coffee](https://ko-fi.com/strawing)
# English [(简体中文)](#简体中文)

## DLNA Screen Casting

Cast your entire Windows desktop over DLNA. This is especially useful when your TV or computer (Wi-Fi adapter) has poor Miracast support.

Audio-only casting is also supported, allowing you to cast currently playing audio to DLNA-compatible speakers or use your TV as a speaker.

This project was brought to life with Claude Code and Codex.

<img width="757" height="481" alt="图片" src="https://github.com/user-attachments/assets/f90a6ed6-1b14-4f61-b19c-d5f761212302" />


## Download and Installation

Download the latest release from the [Releases](https://github.com/liyafe1997/dlna-screen-cast/releases) page.

Both MSIX and traditional `.exe` installers are available. Most users should download the `.exe` installer. Choose the x64 or ARM64 version according to your processor architecture.

Because the MSIX package is unsigned, it must be installed from an administrator PowerShell session by running `Add-AppxPackage -AllowUnsigned DLNAScreenCast_1.1.0.0_x64.msix`.

# How to Build (Not Required for Regular Users)

## Development Environment

- Windows 11 x64/ARM64 (this project is developed on Windows on Arm)
- .NET SDK 10.0.302 or a newer version in the same feature band
- Visual Studio 2026 or Build Tools 2026 with the **Desktop development with C++** workload, MSVC 14.50 or later x64/x86 tools, and Windows 11 SDK 10.0.26100 or later (add the MSVC ARM64 tools when building the ARM64 target)
- CMake 4.2 or later (required for the `Visual Studio 18 2026` generator), Git, and vcpkg at the repository-pinned baseline
- NSIS 3.x when producing a traditional `.exe` installer

## Build and Test

### Clean-clone prerequisite: build the native runtime

This repository contains a C++ native media core in addition to its .NET projects. Its generated DLL and the FFmpeg runtime DLLs are intentionally excluded from Git under `out/`; copying or cloning only the tracked files therefore does not copy them. A clean clone must build the native runtime before building, publishing, or packaging the WinUI application.

The MSIX and NSIS packaging scripts do **not** invoke CMake or install the native toolchain. They require the matching native runtime to have already been built. Otherwise the build stops with `DesktopDlnaCast native media runtime is missing` even when the correct .NET SDK is installed.

From the repository root, install the pinned vcpkg tree under the Git-ignored `out/tooling/vcpkg` directory and set `VCPKG_ROOT` in the PowerShell session used for the build:

> Tip: To make CMake, VC, and the other toolchain components available, first open `Start > Visual Studio 2026 > Native Tools Command Prompt for VS`, use `cd` to navigate to the project directory, run `powershell` from the VS Command Prompt, and then run the following commands. When building on an ARM64 host, use `ARM64_x64 Native Tools...` when targeting x64, or `ARM64 Native Tools...` when targeting ARM64.

```powershell
$vcpkg = Join-Path (Get-Location) "out\tooling\vcpkg"
New-Item -ItemType Directory -Force (Split-Path $vcpkg) | Out-Null
git clone https://github.com/microsoft/vcpkg $vcpkg
git -C $vcpkg checkout 0878b5224d4a4968940ee296a2e7fae2d3b62983
& "$vcpkg\bootstrap-vcpkg.bat" -disableMetrics
$env:VCPKG_ROOT = $vcpkg
```

`VCPKG_ROOT` above is scoped to the current PowerShell session and its child processes; it does not modify the user- or machine-level environment. In a later PowerShell session, restore it from the repository root with `$env:VCPKG_ROOT = (Resolve-Path ".\out\tooling\vcpkg").Path`. Deleting `out/` (including with `git clean -xfd`) also deletes this repository-local vcpkg checkout, so the setup must then be repeated.

Then configure, build, and test the native x64 runtime from the repository root. Use the corresponding `native-arm64-release` preset for an ARM64 package; ARM64 CTest must run on an ARM64 host.

```powershell
cmake --preset native-x64-release
cmake --build --preset native-x64-release
ctest --preset native-x64-release
```

Build target ARM64 native runtime (You need to use the `ARM64 Native Tools...` environment)
```powershell
cmake --preset native-arm64-release
cmake --build --preset native-arm64-release
ctest --preset native-arm64-release
```

The first configure may take some time because vcpkg downloads and builds the repository's constrained FFmpeg dependency set. A successful x64 build produces the native media DLL under `out/native-x64-release/src/DesktopDlnaCast.Media.Native/Release/` and its FFmpeg runtime DLLs under `out/native-x64-release/vcpkg_installed/x64-windows-desktopdlna/bin/`. Do not commit or manually copy these architecture-specific outputs into Git. See [docs/native-build.md](docs/native-build.md) for full prerequisites and troubleshooting details.

After the native runtime exists, build and test the managed solution:

```powershell
dotnet restore DesktopDlnaCast.sln
dotnet build DesktopDlnaCast.sln --configuration Release --no-restore
dotnet test DesktopDlnaCast.sln --configuration Release --no-build
```

Run the GUI:

```powershell
dotnet run --project src/DesktopDlnaCast.App/DesktopDlnaCast.App.csproj -c Release
```

The **Refresh Devices** button performs SSDP discovery across multiple network adapters. After selecting a device, **Test TV** plays the test clip included in the repository. Windows Firewall must allow inbound connections to the application on the selected private network adapter.

For the standalone MockRenderer CLI, fault-injection options, and test query API, see [tools/MockRenderer/README.md](tools/MockRenderer/README.md). For protocol and security boundaries, see [docs/protocol-notes.md](docs/protocol-notes.md).

## Build and Packaging Scripts

After building the matching native runtime as described above, the following scripts publish the application and produce MSIX and NSIS (traditional `.exe`) installer packages. They are packaging entry points, not native-toolchain bootstrap scripts.

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

## DLNA 投屏

通过 DLNA 协议来投放你的整个Windows桌面。对于电视或电脑（Wi-Fi网卡）对Miracast支持不佳的情况非常有用。

支持仅投放音频，可以把正在播放的音频投放到支持 DLNA 的音箱，或把电视当音响使用。

该项目使用 Claude Code 和 Codex 落地。

<img width="758" height="482" alt="图片" src="https://github.com/user-attachments/assets/bb2a06ba-1e9e-4de0-85fe-9a46bc730f02" />


## 下载和安装
前往 [Release](https://github.com/liyafe1997/dlna-screen-cast/releases) 下载。

目前提供MSIX和传统exe安装包两种格式，一般用户下载.exe的安装即可。根据自己的处理器架构下载x64或ARM64版本。

MSIX由于没有签名，必须以管理员权限执行 `Add-AppxPackage -AllowUnsigned DLNAScreenCast_1.1.0.0_x64.msix` 的方式来安装。

# 如何编译（普通用户不必看）

## 开发环境

- Windows 11 x64/ARM64（该项目就是在WoA上开发的）
- .NET SDK 10.0.302 或同一 feature band 的更新版本
- Visual Studio 2026 或 Build Tools 2026，并安装 **使用 C++ 的桌面开发**工作负载、MSVC 14.50 或更高版本的 x64/x86 工具以及 Windows 11 SDK 10.0.26100 或更高版本（构建 ARM64 目标时还需安装 MSVC ARM64 工具）
- CMake 4.2 或更高版本（`Visual Studio 18 2026` 生成器的最低要求）、Git，以及仓库指定基线版本的 vcpkg
- 生成传统 `.exe` 安装包时需要 NSIS 3.x

## 构建与测试

### 全新 Clone 的前置步骤：构建原生运行时

本仓库除了 .NET 项目，还包含一个 C++ 原生媒体核心。它生成的 DLL 和 FFmpeg 运行时 DLL 位于 `out/`，并被 Git 有意忽略；因此，仅复制 Git 跟踪的文件或全新 Clone 都不会包含这些产物。在构建、发布或打包 WinUI 应用之前，必须先构建原生运行时。

MSIX 和 NSIS 打包脚本**不会**自动调用 CMake，也不会安装原生工具链。它们要求对应架构的原生运行时已经构建完成。否则，即使已经正确安装 .NET SDK，构建仍会以 `DesktopDlnaCast native media runtime is missing` 报错停止。

首次准备环境时，在仓库根目录将固定版本的 vcpkg 安装到被 Git 忽略的 `out/tooling/vcpkg`，并在执行构建的 PowerShell 会话中设置 `VCPKG_ROOT`：

> Tip: 以下操作可以先打开 `开始 - Visual Studio 2026 - Native Tools Command Prompt for VS` 然后cd至项目路径，再在 VS Command Prompt 中执行 `powershell`，再运行以下操作，以获得cmake和VC等工具链环境。（如果你在ARM64 Host上编译，target x64时可以使用`ARM64_x64 Native Tools...`，target arm64时可以使用 `ARM64 Native Tools...`）

```powershell
$vcpkg = Join-Path (Get-Location) "out\tooling\vcpkg"
New-Item -ItemType Directory -Force (Split-Path $vcpkg) | Out-Null
git clone https://github.com/microsoft/vcpkg $vcpkg
git -C $vcpkg checkout 0878b5224d4a4968940ee296a2e7fae2d3b62983
& "$vcpkg\bootstrap-vcpkg.bat" -disableMetrics
$env:VCPKG_ROOT = $vcpkg
```

这里设置的 `VCPKG_ROOT` 只对当前 PowerShell 会话及其子进程有效，不会修改用户级或机器级环境变量。以后重新打开 PowerShell，可在仓库根目录执行 `$env:VCPKG_ROOT = (Resolve-Path ".\out\tooling\vcpkg").Path` 恢复设置。删除 `out/`（包括执行 `git clean -xfd`）也会删除这份仓库本地 vcpkg，届时需要重新执行上述准备步骤。

然后在仓库根目录配置、构建和测试 x64 原生运行时。构建 ARM64 安装包时改用对应的 `native-arm64-release` Preset；ARM64 CTest 必须在 ARM64 主机上运行。

```powershell
cmake --preset native-x64-release
cmake --build --preset native-x64-release
ctest --preset native-x64-release
```

构建 target ARM64 原生运行时（你需要使用 `ARM64 Native Tools...` 那个环境入口）
```powershell
cmake --preset native-arm64-release
cmake --build --preset native-arm64-release
ctest --preset native-arm64-release
```

首次配置可能耗时较长，因为 vcpkg 会下载并构建本项目裁剪过的 FFmpeg 依赖。x64 构建成功后，原生媒体 DLL 位于 `out/native-x64-release/src/DesktopDlnaCast.Media.Native/Release/`，FFmpeg 运行时 DLL 位于 `out/native-x64-release/vcpkg_installed/x64-windows-desktopdlna/bin/`。不要把这些与架构相关的产物提交到 Git，也不要用旧开发目录中的产物代替 clean-clone 验证。完整前置条件和故障排查见 [docs/native-build.md](docs/native-build.md)。

原生运行时生成后，再构建并测试托管 Solution：

```powershell
dotnet restore DesktopDlnaCast.sln
dotnet build DesktopDlnaCast.sln --configuration Release --no-restore
dotnet test DesktopDlnaCast.sln --configuration Release --no-build
```

运行 GUI：

```powershell
dotnet run --project src/DesktopDlnaCast.App/DesktopDlnaCast.App.csproj -c Release
```

应用中的“刷新设备”执行多网卡 SSDP 搜索；选择设备后，“测试电视”会播放仓库内置测试片段。Windows Firewall 必须允许应用所选私有网卡上的入站连接。

MockRenderer 的独立 CLI、故障注入参数和测试查询 API 见 [tools/MockRenderer/README.md](tools/MockRenderer/README.md)。协议与安全边界见 [docs/protocol-notes.md](docs/protocol-notes.md)。

## 构建与打包脚本

按照上面的说明构建好对应架构的原生运行时后，以下脚本会发布应用并生成 MSIX/NSIS（传统 `.exe`）安装包。它们是打包入口，不负责安装原生工具链或预先构建原生运行时。

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

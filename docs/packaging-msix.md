# MSIX 打包与分发

`packaging/scripts/build-msix.ps1` 一条命令产出 `.msix` 与 `.msixbundle`（默认不签名），`-Architecture x64|arm64` 选择目标架构（默认 x64）；`build-msix-x64.ps1` / `build-msix-arm64.ps1` 是对应的薄封装，根目录的 `build-release-msix-x64.bat` / `build-release-msix-arm64.bat` 是双击入口：

```powershell
powershell -ExecutionPolicy Bypass -File packaging/scripts/build-msix-x64.ps1 -Version 1.0.0.0
powershell -ExecutionPolicy Bypass -File packaging/scripts/build-msix-arm64.ps1 -Version 1.0.0.0
```

产物位于 `out/msix/artifacts/`。前置条件：

- 原生媒体核心已构建（`DesktopDlnaCast.Media.Native.dll` 与 FFmpeg DLL，见 [native-build.md](native-build.md)）；
- `dotnet restore` 已执行过（脚本从 NuGet 缓存的 `Microsoft.Windows.SDK.BuildTools` 取 `makeappx.exe`/`signtool.exe`，装有 Windows SDK 亦可）。

## 脚本做了什么

1. **Self-contained 发布**：`dotnet publish -r win-<arch> --self-contained true`。
   - **.NET Desktop Runtime** 与 **ASP.NET Core 共享框架**（Kestrel，来自
     `DesktopDlnaCast.Streaming` 的 `FrameworkReference Microsoft.AspNetCore.App`）
     全部随包分发。MSIX 安装过程不能链式运行运行时安装器，且 ASP.NET Core
     没有对应的 MSIX framework package，因此 self-contained 是唯一能离线安装、
     不依赖目标机器全局运行时的布局。
   - **Windows App SDK** 由 `WindowsAppSDKSelfContained=true`（csproj 中已有）
     一并打入，无需 `Microsoft.WindowsAppRuntime` 框架包依赖。
2. **修补发布输出的两个缺口**：
   - `resources.pri`：`dotnet publish` 不会为 `WindowsPackageType=None` 的 WinUI
     工程复制 PRI；脚本从 `bin/Release` 取 `DLNAScreenCast.pri`，以
     `resources.pri` 和原名两个名字放入包根，否则安装后所有本地化字符串丢失。
   - **VC++ CRT app-local**：`DesktopDlnaCast.Media.Native.dll` 与 vcpkg 构建的
     FFmpeg 链接 `vcruntime140/msvcp140`（x64 额外需要 `vcruntime140_1`，
     arm64 没有也不需要该 DLL）。.NET 运行时包与 Windows App SDK 都不带它们。
     脚本优先从 VS/Build Tools 的 `VC\Redist\MSVC\...\Microsoft.VC*.CRT` 复制，
     其次是 `VC\Tools\MSVC\...\bin\Host*\<arch>` 与仓库本地 `out\tooling` 工具链，
     最后回退 `System32`；每个候选都做 PE 头架构校验（防止 ARM64X/跨宿主 DLL
     混入错误架构的包）。
3. **生成 AppxManifest**：由 [packaging/msix/AppxManifest.template.xml](../packaging/msix/AppxManifest.template.xml)
   实例化版本号、Publisher、架构。
4. **pack → bundle**：`makeappx pack`、`makeappx bundle`。默认不签名；
   提供 `-CertificatePath` 时对 `.msix` 与 `.msixbundle` 逐个
   `signtool sign /fd SHA256`。

## 防火墙规则（安装时自动注册）

清单声明了包级 `windows.firewallRules` 扩展：

```xml
<desktop2:Extension Category="windows.firewallRules">
  <desktop2:FirewallRules Executable="DLNAScreenCast.exe">
    <desktop2:Rule Direction="in" IPProtocol="TCP" Profile="all" />
    <desktop2:Rule Direction="in" IPProtocol="UDP" Profile="all" />
  </desktop2:FirewallRules>
</desktop2:Extension>
```

安装 MSIX 时系统自动注册这些入站放行规则，卸载时自动移除，无需用户交互，
也不会出现首次监听端口时的防火墙弹窗。

- **TCP** 放行电视回连本机拉取 MPEG-TS HTTP 流；**UDP** 放行 SSDP
  发现应答/NOTIFY。规则按可执行文件限定，不限定端口（流端点使用动态端口）。
- **`Profile="all"` 是有意为之**：大量家庭/宿舍网络在 Windows 上被识别为
  “公用网络”，若只放行 Private 会导致这些用户发现不了设备或电视拉流失败。
  规则仍仅对本应用的 exe 生效，暴露面等同于应用自身监听的端点。

安装后可在 `wf.msc`（入站规则中程序路径指向
`C:\Program Files\WindowsApps\DLNAScreenCast_...`）确认规则存在。

## 签名与安装

### 未签名包（默认）

不带证书参数运行时产出**未签名**的包。脚本会自动在 Identity 的 Publisher 末尾
追加未签名标记
`OID.2.25.311729368913984317654407730594956997722=1`——这是
[Windows 未签名包机制](https://learn.microsoft.com/windows/msix/package/unsigned-package)
的硬性要求，缺少它安装会报 0x80073D2C；该标记同时保证未签名包永远不可能
冒用已签名包的身份。

安装（Windows 11，**管理员** PowerShell——含可执行内容的未签名包必须按
所有用户安装，因此普通权限会报 0x80073D2B）：

```powershell
Add-AppxPackage -Path DLNAScreenCast_1.0.0.0.msixbundle -AllowUnsigned
```

卸载：`Get-AppxPackage DLNAScreenCast | Remove-AppxPackage`。
未签名安装仅适合本机/测试机快速验证，不要用于对外分发。

### 签名分发

用受信任 CA 颁发的代码签名证书，Publisher 必须与证书 Subject 完全一致
（提供 `-CertificatePath` 时脚本不会追加未签名标记）：

```powershell
powershell -ExecutionPolicy Bypass -File packaging/scripts/build-msix.ps1 -Version 1.2.0.0 `
  -Publisher 'CN=Your Company, O=Your Company, C=SE' `
  -PublisherDisplayName 'Your Company' `
  -CertificatePath C:\secrets\codesign.pfx -CertificatePassword '...'
```

上架 Microsoft Store 时由 Store 负责签名与信任，提交未签名产物即可，但清单
Publisher 必须是合作伙伴中心分配的身份，且**不能**带未签名标记：

```powershell
powershell -ExecutionPolicy Bypass -File packaging/scripts/build-msix.ps1 -Version 1.2.0.0 `
  -Publisher 'CN=xxxxxxxx-xxxx-...' -NoUnsignedMarker
```

## 其他

- **开始菜单名称已本地化**：清单 `DisplayName` 引用 `ms-resource:WindowTitle`，
  由系统按显示语言从包根 `resources.pri` 解析（zh-Hans 显示"DLNA 投屏"等）。
  注意 Identity `Name` 必须保持 `DLNAScreenCast`（与应用工程 AssemblyName 生成
  的 PRI 资源映射名一致），改名会导致 ms-resource 解析失败、安装报 0x80080204。
- x64 与 arm64 各自产出独立的 `.msix` 与单架构 `.msixbundle`（文件名带架构
  后缀，互不覆盖）。打包 arm64 前需先构建 arm64 原生媒体核心
  （[native-build.md](native-build.md)）。如需单个双架构 bundle，把两个
  `.msix` 放入同一 bundle 输入目录再执行一次 `makeappx bundle` 即可。
- `packaging/msix/Assets/` 下包含正式的方形应用图标、Store 图标和宽磁贴图
  （150×150、44×44、50×50、310×150）；更新品牌设计后需要重新打包。
- 版本号必须为四段数字（`a.b.c.d`）；上架 Store 要求第四段为 0。

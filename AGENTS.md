# AGENTS.md

## 适用范围

本文件适用于整个仓库。若某个子目录中存在更具体的 `AGENTS.md`，则该子目录优先遵循更具体的文件。

项目暂定名：**DesktopDlnaCast**。

本项目是一个 Windows 桌面 GUI 应用，用于：

1. 捕获 Windows 显示器或指定应用窗口；
2. 可选捕获系统正在播放的声音；
3. 将画面和声音实时编码成智能电视易于兼容的直播媒体流；
4. 在局域网中通过 HTTP 提供该媒体流；
5. 使用 UPnP/DLNA 控制电视等 Digital Media Renderer（DMR）播放该流。

本项目的准确定位是：

> **通过 DLNA 播放 Windows 桌面的实时直播流。**

它不是 Miracast 实现，不是虚拟显示器驱动，也不是远程桌面协议。

---

# 1. 产品目标

用户应当能够在 GUI 中完成以下操作：

- 搜索当前局域网内的 DLNA/UPnP MediaRenderer 设备；
- 查看设备名称、厂商、型号和 IP 地址；
- 选择某一块显示器或某个应用窗口；
- 选择是否包含鼠标指针；
- 选择是否包含 Windows 系统声音；
- 选择兼容、标准或自定义画质；
- 选择自动、连续 MPEG-TS 或 HLS 输出模式；
- 用已知可播放的测试片段验证电视的 DLNA 播放链路；
- 一键开始和停止桌面投屏；
- 在失败时查看并导出足够详细的诊断信息。

视频数据链路：

```text
Windows 显示器或窗口
    ↓
Windows.Graphics.Capture
    ↓
D3D11 缩放、裁剪、色彩转换
    ↓
H.264 实时编码
```

音频数据链路：

```text
Windows 输出设备
    ↓
WASAPI Loopback
    ↓
重采样与格式转换
    ↓
AAC-LC 实时编码
```

媒体输出链路：

```text
H.264 + AAC
    ↓
MPEG-TS 复用
    ├─ 连续 HTTP MPEG-TS，默认低延迟模式
    └─ HLS，电视兼容回退模式
    ↓
电视通过 HTTP 主动拉流
```

DLNA 控制链路：

```text
SSDP 发现
    ↓
获取 Device Description XML
    ↓
解析 AVTransport / ConnectionManager
    ↓
GetProtocolInfo 能力探测
    ↓
SetAVTransportURI
    ↓
Play
```

MVP 的自动化验收目标是仓库内自带的、提供标准 UPnP AVTransport 服务的测试 Receiver。它必须像真实 DMR 一样完成 SSDP 响应、接收控制命令、主动拉取并验证媒体流。真实电视或盒子只用于发布前的人工兼容性抽查，不得成为 AI 开发、CI 或里程碑推进的必备条件。

---

# 2. 明确不做的事项

除非后续 issue 明确修改范围，否则不要实现：

- Miracast；
- Wi-Fi Direct；
- Google Cast / Chromecast；
- AirPlay；
- Lelink、乐联、Cast+ 等私有投屏协议；
- WebRTC；
- Windows 虚拟显示器或 Indirect Display Driver；
- 远程鼠标、键盘、触控或手柄输入；
- DRM 绕过；
- 受保护视频捕获绕过；
- HDCP 规避；
- 云端中继；
- 公网直播；
- NAT 穿透或路由器端口映射；
- 完整的 DLNA MediaServer；
- ContentDirectory 服务；
- 默认把桌面录制到硬盘；
- MVP 阶段同时投多台电视；
- MVP 阶段默认使用 HEVC/H.265；
- 裸 H.264 与裸 PCM 的私有混流；
- 游戏级低延迟；
- 对低于 200 ms 延迟作出承诺。

电视在本项目中是一个网络媒体播放器，而不是真正的 Windows 第二显示器。

---

# 3. 初始平台范围

初始支持目标：

- Windows 11；
- x64；
- 当前受支持的 Visual Studio；
- 当前受支持的 Windows SDK；
- 当前受支持的 .NET SDK；
- 当前受支持的 Windows App SDK。

在不会明显增加复杂度的前提下，托管代码应避免不必要的 x64 假设，为以后支持 Windows on ARM64 保留空间。

初始阶段：

- 不支持 x86；
- 不要在 x64 版本尚未稳定前投入 ARM64 原生媒体核心；
- 不要为了支持过旧的 Windows 版本牺牲架构清晰度。

如果实际实现需要降低最低 Windows 版本，必须先提交设计说明和验证结果。

---

# 4. 固定技术基线

## 4.1 GUI 与托管宿主

使用：

- C#；
- WinUI 3；
- Windows App SDK；
- MVVM；
- `Microsoft.Extensions.DependencyInjection`；
- `Microsoft.Extensions.Logging`；
- Kestrel 嵌入式 HTTP Server。

托管层负责：

- GUI；
- ViewModel；
- 应用配置；
- 投屏会话编排；
- SSDP 发现；
- UPnP Device Description 解析；
- SOAP 控制；
- DIDL-Lite 生成；
- 电视兼容配置；
- HTTP Server 生命周期；
- 日志与诊断导出。

禁止：

- 在 UI 线程执行阻塞网络操作；
- 在 UI 类中运行实时帧处理循环；
- 在 UI 线程调用 `.Result`、`.Wait()`；
- 用隐藏的无生命周期后台任务承载核心媒体流程。

## 4.2 原生媒体核心

实时媒体流水线使用 C++/WinRT 原生 DLL。

原生层负责：

- Windows.Graphics.Capture；
- D3D11 纹理管理；
- GPU 侧缩放和色彩转换；
- WASAPI Loopback；
- Media Foundation H.264 编码；
- Media Foundation AAC 编码；
- FFmpeg `libavformat` 的 MPEG-TS/HLS 复用；
- PTS/DTS；
- 音视频交织；
- 有界队列；
- 关键帧感知的启动缓冲。

托管层通过精简、稳定的 C ABI 使用原生核心。

禁止跨 ABI 暴露：

- C++ STL 容器；
- C++ 异常；
- WinRT 对象；
- COM 接口指针；
- 所有权不明确的裸指针。

建议接口形状：

```c
typedef void* ddc_session_handle;

typedef struct ddc_stream_config {
    int32_t width;
    int32_t height;
    int32_t frame_rate;
    int32_t video_bitrate;
    int32_t audio_bitrate;
    int32_t include_audio;
    int32_t stream_mode;
} ddc_stream_config;

int32_t ddc_session_create(
    const ddc_stream_config* config,
    ddc_session_handle* result);

int32_t ddc_session_start(ddc_session_handle handle);
int32_t ddc_session_stop(ddc_session_handle handle);
void ddc_session_destroy(ddc_session_handle handle);
```

接口可以演进，但必须保持：

- 小；
- 明确；
- 可版本化；
- 可测试；
- 可取消；
- 重复 stop/cleanup 安全；
- 不跨边界抛出异常。


# 5. 推荐仓库结构

```text
DesktopDlnaCast/
├── AGENTS.md
├── README.md
├── LICENSE
├── THIRD_PARTY_NOTICES.md
├── DesktopDlnaCast.sln
├── docs/
│   ├── architecture.md
│   ├── protocol-notes.md
│   ├── compatibility.md
│   └── troubleshooting.md
├── src/
│   ├── DesktopDlnaCast.App/
│   ├── DesktopDlnaCast.Core/
│   ├── DesktopDlnaCast.Upnp/
│   ├── DesktopDlnaCast.Streaming/
│   ├── DesktopDlnaCast.Media.Interop/
│   └── DesktopDlnaCast.Media.Native/
├── tests/
│   ├── DesktopDlnaCast.Core.Tests/
│   ├── DesktopDlnaCast.Upnp.Tests/
│   ├── DesktopDlnaCast.Streaming.Tests/
│   └── DesktopDlnaCast.IntegrationTests/
└── tools/
    ├── MockRenderer/
    └── StreamProbe/
```

## `DesktopDlnaCast.App`

负责：

- WinUI View；
- ViewModel；
- Command；
- 资源字典；
- 本地化资源；
- 用户交互。

不得包含：

- SSDP 协议实现；
- SOAP 底层实现；
- 编码器实现；
- HTTP 流输出实现。

## `DesktopDlnaCast.Core`

负责：

- 投屏会话状态机；
- 用例；
- 接口；
- 配置模型；
- 兼容配置选择；
- 错误模型；
- 会话编排。

## `DesktopDlnaCast.Upnp`

负责：

- SSDP；
- Device Description；
- Service URL 解析；
- SOAP；
- AVTransport；
- ConnectionManager；
- 可选 RenderingControl；
- DIDL-Lite；
- Renderer 设备模型。

## `DesktopDlnaCast.Streaming`

负责：

- Kestrel Host；
- 会话 Token；
- HTTP Endpoint；
- 连续 MPEG-TS 输出；
- HLS Playlist 和 Segment；
- 网卡绑定；
- 目标电视 IP 限制。

## `DesktopDlnaCast.Media.Interop`

负责：

- P/Invoke；
- `SafeHandle`；
- 托管配置到原生配置的转换；
- 原生回调；
- 原生错误翻译。

## `DesktopDlnaCast.Media.Native`

负责：

- 视频捕获；
- 音频捕获；
- D3D11；
- Media Foundation；
- FFmpeg mux；
- 媒体时钟；
- 环形缓冲；
- 原生统计信息。

## `MockRenderer`

实现可重复测试的 UPnP MediaRenderer：接收 SOAP 控制、主动拉取媒体、推进 Transport State，并暴露机器可读的断言与故障注入接口。

## `StreamProbe`

用于检查生成的流，并在开发环境存在 `ffprobe` 时调用它进行额外验证。

协议层、媒体层和 GUI 层必须解耦。

---

# 6. 核心接口与会话状态机

优先采用类似接口：

```csharp
public interface IDlnaDiscoveryService;
public interface IDlnaRendererClient;
public interface IRendererCapabilityProbe;
public interface IStreamPublisher;
public interface IMediaCaptureSession;
public interface INetworkInterfaceSelector;
public interface ICompatibilityProfileStore;
public interface ICastSession;
```

核心投屏状态机必须显式建模：

```text
Idle
  → Discovering
  → ProbingRenderer
  → StartingMediaPipeline
  → WaitingForKeyframe
  → Publishing
  → SendingTransportUri
  → StartingPlayback
  → Playing
  → Stopping
  → Idle
```

错误不能只是一个通用字符串。错误对象至少记录：

- 失败阶段；
- 面向用户的说明；
- 原始异常或 SOAP Fault；
- HTTP/UPnP 状态；
- 建议回退方案；
- 会话 Correlation ID；
- 可安全导出的诊断上下文。

MVP 同时只允许一个投屏会话。

Start、Stop、Cancel 和 Dispose 必须可重复调用，不得因重复调用崩溃。

---

# 7. UPnP/DLNA 实现要求

## 7.1 SSDP 发现

至少搜索：

```text
urn:schemas-upnp-org:device:MediaRenderer:1
urn:schemas-upnp-org:service:AVTransport:1
ssdp:all
```

要求：

- 在每个符合条件的活动局域网接口发送 M-SEARCH；
- 支持有线和 Wi-Fi；
- 不要假设默认网卡就是能到达电视的网卡；
- 默认忽略 Loopback；
- 默认忽略断开网卡；
- 默认忽略 VPN、TUN、TAP 和隧道接口；
- 默认忽略只有不可用 Link-local 地址的接口；
- 高级设置允许用户手动指定网卡；
- 以 UDN/USN 去重，而不是 Friendly Name；
- 解析 `LOCATION`；
- 下载 Device Description XML；
- 以 Description URL 为基准解析相对 Service URL；
- 保存 Friendly Name、Manufacturer、Model、UDN 和 IP；
- 所有网络操作必须支持取消；
- 所有网络操作必须有有限超时；
- XML 必须关闭 DTD 和外部实体；
- XML 响应必须设置大小上限；
- 设备返回的数据一律视为不可信输入。

基本 M-SEARCH 稳定之后，可以增加：

- `ssdp:alive`；
- `ssdp:byebye`；
- 设备过期机制。

这些不是 Milestone 1 的阻塞项。

## 7.2 服务识别

识别：

- AVTransport 1/2/3；
- ConnectionManager 1/2/3；
- RenderingControl，可选。

构造 SOAPAction 时必须使用电视实际声明的 Service Type。

不要固定假设所有电视都是 AVTransport:1。

MVP 必须有 AVTransport。

ConnectionManager 强烈建议实现，但电视缺少或实现错误时不得导致整个设备不可用。

## 7.3 能力探测

若电视提供 ConnectionManager，调用：

```text
ConnectionManager.GetProtocolInfo
```

解析 Sink 列表，并记录：

- `http-get`；
- `video/mpeg`；
- MPEG-TS 相关 DLNA Profile；
- H.264；
- AAC；
- MP4；
- 常见 HLS MIME。

不得把 `GetProtocolInfo` 结果当作绝对真相。

很多电视会漏报实际可播放格式。该结果只用于：

- 推荐默认配置；
- 排序回退顺序；
- 提供诊断。

不得因此隐藏用户手动尝试某种模式的能力。

## 7.4 播放启动顺序

必须按以下顺序：

1. 创建并启动捕获/编码流水线；
2. 启动 HTTP Server；
3. 等待编码参数有效；
4. 等待 MPEG-TS PAT/PMT；
5. 等待首个 IDR；
6. 计算能到达目标电视的本机 IPv4；
7. 生成随机 Session Token；
8. 构造直播 URL；
9. 调用 `SetAVTransportURI`；
10. 调用 `Play`，Speed 为 `1`；
11. 轮询 `GetTransportInfo`；
12. 确认电视实际请求了 HTTP URL。

示例 URL：

```text
http://192.168.1.10:51783/stream/7f4f6d.../live.ts
```

禁止把以下内容发给电视：

- `localhost`；
- `127.0.0.1`；
- `0.0.0.0`；
- 通配监听地址；
- 默认情况下的 Windows 主机名；
- IPv6 Link-local 地址；
- 与电视无关的 VPN 或虚拟网卡地址。

## 7.5 DIDL-Lite

优先发送标准 DIDL-Lite 元数据。

示例：

```xml
<DIDL-Lite
  xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/"
  xmlns:dc="http://purl.org/dc/elements/1.1/"
  xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/">
  <item id="desktop-live" parentID="0" restricted="1">
    <dc:title>Windows Desktop</dc:title>
    <upnp:class>object.item.videoItem</upnp:class>
    <res protocolInfo="http-get:*:video/mpeg:*">
      http://192.168.1.10:51783/stream/token/live.ts
    </res>
  </item>
</DIDL-Lite>
```

要求：

- SOAP 中正确 XML Escape；
- `protocolInfo` 与实际输出一致；
- 日志中不得出现完整 Token；
- 电视拒绝 Metadata 时，兼容配置可允许用空 `CurrentURIMetaData` 重试一次；
- 空 Metadata 重试必须是受控回退，不得无限循环。

## 7.6 停止顺序

停止时：

1. 尽力调用 `AVTransport.Stop`；
2. 取消正在输出的 HTTP Response；
3. 停止编码器；
4. 停止捕获；
5. 释放 Frame Pool；
6. 释放 D3D11 资源；
7. 释放 WASAPI；
8. 释放 Media Foundation Transform；
9. 关闭 FFmpeg muxer；
10. 使 Session Token 立即失效；
11. 在不再需要时关闭 HTTP Listener；
12. 状态机返回 `Idle`。

即使电视不响应 Stop，也必须完成本地清理。

绝不能在用户停止之后继续后台捕获桌面或声音。

---

# 8. 视频捕获与处理

主捕获 API：

```text
Windows.Graphics.Capture
```

支持：

- 整块显示器；
- 指定应用窗口；
- 是否包含鼠标指针；
- 捕获源动态改变尺寸。

推荐流水线：

```text
Direct3D11CaptureFrame
    ↓
BGRA D3D11 Texture
    ↓
D3D11 Video Processor
    ├─ 缩放
    ├─ 裁剪或 Letterbox
    └─ SDR 色彩转换
    ↓
NV12 D3D11 Texture
    ↓
Media Foundation H.264 Encoder
```

MVP 限制：

- 只支持 SDR；
- 只支持 8-bit 输出；
- 不做 HDR Passthrough；
- 输出宽高必须为偶数；
- 支持 1280×720；
- 支持 1920×1080；
- 先稳定 30 fps；
- 30 fps 未稳定前不要实现 60 fps。

捕获源尺寸变化时：

- 保持宽高比；
- 默认 Letterbox；
- 不得静默拉伸；
- 安全重建捕获资源；
- 尽量避免重启整个应用。

以后可以添加 Desktop Duplication API 作为兼容后端。

主捕获路径未完成前，不要同时实现两套捕获后端。

---

# 9. 音频捕获与同步

使用 WASAPI Loopback 捕获 Windows Render Endpoint。

默认：

```text
采样率：48 kHz
声道：双声道
编码：AAC-LC
码率：128 或 160 kbit/s
```

要求：

- 默认捕获当前默认输出设备；
- 高级设置可选择输出设备；
- 兼容 Float 和 Integer Endpoint Format；
- 重采样到编码器需要的格式；
- 当前无应用发声时生成带正确时间戳的静音；
- 检测默认输出设备变化；
- 音频初始化失败时允许退回 Video-only；
- 短暂音频故障不要不必要地终止视频。

使用统一的单调会话时钟。

音频启用后，优先把音频作为长期同步参考。

必须校正缓慢漂移，禁止靠无限增长的队列“解决”同步问题。

所有音视频队列必须有界。

暴露统计：

- 捕获视频帧；
- 编码视频帧；
- 丢弃视频帧；
- 捕获音频包；
- 队列溢出；
- 时间戳校正；
- 编码器等待时间。

---

# 10. 编码配置

所有编码参数应集中为命名 Profile。

禁止把码率、GOP 和 Profile 常量散落在不同类中。

## 10.1 兼容配置

```text
分辨率：1280×720
帧率：30 fps
视频：偏兼容的 H.264 Profile
视频码率：约 3 Mbit/s
音频：AAC-LC Stereo
音频码率：128 kbit/s
GOP：1 秒
B-frame：关闭
输出：连续 MPEG-TS
```

## 10.2 标准配置

```text
分辨率：1920×1080
帧率：30 fps
视频：H.264 Main
视频码率：约 6 Mbit/s
音频：AAC-LC Stereo
音频码率：160 kbit/s
GOP：1 秒
B-frame：关闭
输出：连续 MPEG-TS
```

## 10.3 HLS 兼容配置

```text
分辨率：1280×720 或 1920×1080
帧率：30 fps
视频：H.264
音频：AAC-LC
Segment：约 1 秒
Playlist Window：6 个 Segment
每段独立起播：必须
存储：仅内存
```

编码器要求：

- 每个 GOP 边界产生 IDR；
- 开播前能够提供 SPS/PPS；
- 避免长 Lookahead；
- 初始兼容配置关闭 B-frame；
- 兼容性优先于压缩率；
- 诊断中显示最终选择的编码器；
- 显示是否为硬件 MFT；
- 显示编码器实际接受的参数，而不仅是请求参数。

不要假定请求硬件编码后系统一定选到了硬件编码器。

---

# 11. 连续 HTTP MPEG-TS

连续 MPEG-TS 是默认低延迟模式。

Endpoint：

```text
GET /stream/{token}/live.ts
Content-Type: video/mpeg
Cache-Control: no-store, no-cache
```

默认行为：

- 不设置固定 `Content-Length`；
- 持续输出；
- 在适合时使用 HTTP Chunked Transfer；
- 以有限间隔 Flush；
- 收到取消后快速结束响应。

MPEG-TS 要求：

- H.264 Video；
- 启用音频时为 AAC；
- PTS/DTS 合法；
- 启动时存在 PAT/PMT；
- 频繁重复 PAT/PMT；
- 新客户端尽量从 IDR 附近开始；
- 在可解码视频之前提供必要 Codec Configuration；
- 使用有界内存 Ring Buffer；
- 默认仅保留最近约 2–5 秒；
- 默认不把直播数据写入磁盘。

兼容开关：

- Chunked Response；
- Connection-close Streaming；
- DIDL-Lite；
- Empty Metadata；
- MIME Override；
- Video-only；
- Startup Delay；
- 720p Fallback。

支持 `HEAD` 探测请求。

电视发送的异常或特殊请求头应进入诊断日志，但必须限制长度。

MVP 对 Live Endpoint 的 Range Request 可以：

- 忽略 Range；
- 返回新的 `200 OK`；
- 从下一个合法起播点开始。

MVP 不要求可 Seek 的直播 Range 实现。

---

# 12. HLS 回退

Endpoint：

```text
GET /hls/{token}/index.m3u8
GET /hls/{token}/segment-{sequence}.ts
```

要求：

- Live Sliding Playlist；
- 单调递增的 Media Sequence；
- 会话进行期间不写 `#EXT-X-ENDLIST`；
- 每个 Segment 从 IDR 开始；
- 旧 Segment 从内存过期；
- MIME 正确；
- Playlist 使用合适的 Live Cache Header；
- 不依赖磁盘；
- Playlist 和内存都有明确上限。

HLS 只作为连续 MPEG-TS 被电视拒绝时的兼容回退。

不得只实现 HLS，而完全没有连续 MPEG-TS。

---

# 13. HTTP Server 与网卡选择

默认使用 Kestrel，除非性能数据证明其为瓶颈。

建议 Endpoint：

```text
GET  /health
GET  /stream/{token}/live.ts
HEAD /stream/{token}/live.ts
GET  /hls/{token}/index.m3u8
GET  /hls/{token}/segment-{sequence}.ts
GET  /diagnostics/session
```

安全要求：

- 默认只绑定所选局域网接口；
- 除非用户显式打开诊断选项，否则不要绑定所有网卡；
- 不要绑定公网接口；
- 每个 Session 使用密码学安全随机 Token；
- 停止后 Token 立即失效；
- 可行时仅允许目标电视 IP；
- 不持久化桌面画面和音频；
- 不在日志中记录媒体内容；
- 不做 UPnP IGD；
- 不做 NAT 端口映射。

本机 IP 选择必须以“到电视的实际路由”为依据。

禁止简单选择系统返回的第一个 IPv4。

## Windows Firewall

应用必须：

- 只请求最小范围的入站权限；
- 规则限定到应用程序；
- 尽量限定到 Private Profile；
- 绝不关闭 Windows Firewall；
- 电视无法访问 HTTP Server 时给出明确提示；
- 文档中给出手动恢复和删除规则的方法。

---

# 14. GUI 要求

## 14.1 设备区域

显示：

- Friendly Name；
- Manufacturer；
- Model；
- IP；
- 在线状态；
- 刷新按钮；
- 高级网卡选择。

## 14.2 捕获源区域

显示：

- 显示器选择；
- 窗口选择；
- 捕获鼠标指针；
- 捕获系统声音；
- 高级音频设备选择。

## 14.3 画质区域

提供：

- 兼容；
- 标准；
- 自动；
- 连续 MPEG-TS；
- HLS；
- Video-only Fallback。

自定义编码参数可在后续加入。

## 14.4 控制按钮

必须有：

- 测试电视；
- 开始投放；
- 停止投放。

“测试电视”使用已知可播放的 H.264/AAC MPEG-TS 测试片段，不需要桌面捕获。

## 14.5 状态区域

显示：

- 投屏状态；
- 目标电视；
- 捕获源；
- 编码器名称；
- 输出分辨率；
- 帧率；
- 实际码率；
- 丢帧；
- HTTP Client 状态；
- Renderer Transport State；
- 已运行时间；
- 当前 Stream Mode。

Stream URL 必须隐藏 Token。

## 14.6 诊断页面

支持复制或导出：

- SSDP Response；
- Device Description URL；
- 清理敏感信息后的 Device XML；
- Service Type 与 Control URL；
- `GetProtocolInfo`；
- SOAP Action 名；
- SOAP HTTP Status；
- SOAP Fault；
- 电视 HTTP Method 和 Header；
- 所选网卡与 IP；
- Media Profile；
- Encoder/Muxer 信息；
- 最近结构化日志。

不得导出完整 Token。

所有用户可见字符串放入资源文件，为后续本地化留出空间。

---

# 15. 电视兼容配置

设备兼容差异必须数据化。

禁止在各处散落：

```csharp
if (modelName.Contains("某品牌"))
```

示例配置：

```json
{
  "rendererKey": "manufacturer:model",
  "preferredMode": "MpegTsContinuous",
  "mimeType": "video/mpeg",
  "sendDidlLite": true,
  "allowEmptyMetadataRetry": true,
  "disableAudio": false,
  "httpTransferMode": "Chunked",
  "startupDelayMs": 500
}
```

保存用户已验证的单设备覆盖设置。

建议回退顺序：

1. 标准 1080p 连续 MPEG-TS + DIDL-Lite；
2. 标准连续 MPEG-TS + Empty Metadata；
3. 720p 兼容 MPEG-TS；
4. Video-only MPEG-TS；
5. HLS + DIDL-Lite；
6. HLS + Empty Metadata。

禁止无限自动重试。

GUI 应显示当前正在尝试哪个回退配置。

达到有限重试次数后停止，并显示诊断。

---

# 16. 日志与可观测性

使用结构化日志。

每个投屏 Session 必须有 Correlation ID。

关键事件：

- DiscoveryStarted；
- DiscoveryCompleted；
- RendererFound；
- RendererUpdated；
- RendererExpired；
- DeviceDescriptionFetched；
- ServiceResolved；
- CapabilityProbeCompleted；
- HttpServerBound；
- MediaPipelineCreated；
- FirstKeyframeEncoded；
- StreamReady；
- SetAVTransportUriStarted/Completed；
- PlayStarted/Completed；
- RendererHttpRequestReceived；
- FirstMediaByteSent；
- TransportStateChanged；
- QueueDrop；
- EncoderError；
- AudioDeviceChanged；
- StopReason；
- CleanupCompleted。

规则：

- 禁止静默吞掉异常；
- 禁止无上下文的 `catch (Exception)`；
- 禁止记录媒体 Payload；
- 禁止记录 Cookie、Credential 或完整 Token；
- SOAP 与网络日志必须限制大小；
- 诊断导出必须稳定、可读和可复现。

---

# 17. 测试要求

## 17.1 单元测试

至少覆盖：

- SSDP Response 解析；
- 设备去重；
- Description URL 解析；
- 相对 Service URL 解析；
- Service Version 解析；
- SOAP Envelope 生成；
- SOAP Fault 解析；
- DIDL-Lite 生成；
- XML Escape；
- `GetProtocolInfo` 解析；
- Compatibility Profile 选择；
- 本机网卡/IP 选择；
- Token 验证；
- 状态机转换；
- Cancel；
- Timeout；
- 失败后的资源清理。

## 17.2 仓库内测试 Receiver（MockRenderer）

`tools/MockRenderer` 是 AI、本地开发和 CI 的权威 DLNA 测试目标。它不是只返回固定 SOAP 响应的 Stub，而是一个可从命令行无界面启动、可观测、可配置故障的简单 UPnP/DLNA MediaRenderer。

它至少实现：

- SSDP M-SEARCH Response，并使用固定测试 UDN 去重；
- Device Description XML；
- AVTransport Endpoint，至少支持 `SetAVTransportURI`、`Play`、`Stop` 和 `GetTransportInfo`；
- ConnectionManager Endpoint，至少支持 `GetProtocolInfo`；
- 收到 `Play` 后主动对 `CurrentURI` 发起 HTTP `HEAD` 或 `GET`；
- 持续读取连续 MPEG-TS，或按 Playlist 拉取 HLS Segment；
- 检查 HTTP Status、Content-Type、首字节时间和连接结束原因；
- 将 Transport State 从 `STOPPED` 推进到 `TRANSITIONING`、`PLAYING`，并允许测试覆盖转换时机；
- 可配置 SOAP Fault；
- Metadata Rejection；
- 延迟发起 HTTP 请求；
- 立即发起 HTTP 请求；
- GET；
- HEAD；
- 中途断开 HTTP；
- 自定义请求 Header；
- Transport State。

Mock 必须记录：

- 收到的 SOAP Action；
- CurrentURI；
- CurrentURIMetaData；
- Play/Stop 顺序；
- HTTP 请求时间和 Header。

此外必须提供：

- 独立于 GUI 的 CLI 启动方式；
- 机器可读的事件日志或测试查询 API；
- 就绪探针和确定性的启动/停止；
- 可由测试分配的端口，避免写死端口；
- 对收到的 URI、Metadata 和 Header 设置长度上限，不记录完整 Session Token；
- 仅在测试明确要求时监听非 Loopback 网卡；
- 不依赖真实电视、云服务或公网。

MockRenderer 不要求实现完整 DLNA MediaServer、ContentDirectory 或高质量画面渲染。媒体正确性由内置 MPEG-TS/HLS 检查器与 `StreamProbe` 验证；环境中存在 `ffprobe` 时，可额外启动它作为解码/容器验证 Oracle，但核心集成测试不得因此变成外部工具必需。

## 17.3 流验证

自动或集成测试至少验证：

- MPEG-TS 可解析；
- 视频为 H.264；
- 启用音频时音频为 AAC；
- PTS 单调；
- PAT/PMT 存在；
- IDR 间隔符合配置；
- 新连接能在合理时间内开始解码；
- HLS Media Sequence 推进；
- 旧 Segment 过期；
- HLS 内存有界。

`ffprobe` 可作为开发和集成测试的辅助 Oracle。

不得让所有单元测试都依赖外部 `ffprobe`。

## 17.4 测试目标分层

测试按以下顺序执行：

1. 单元测试：协议解析、状态机、安全边界和 Cleanup；
2. 仓库内 MockRenderer 集成测试：作为 CI 和 AI 开发的权威 DMR 测试；
3. `StreamProbe` 与可选 `ffprobe`：验证容器、Codec、时间戳和起播点；
4. Windows 上的 Kodi：可选的第三方黑盒冒烟测试；
5. 真实电视或盒子：发布候选版本的可选人工兼容性抽查。

Windows 版 Kodi 可以在启用 `Settings → Services → UPnP/DLNA → Allow remote control via UPnP` 后作为外部 Renderer 使用。它适合确认应用能与独立实现互操作，但不能替代 MockRenderer，也不得作为 CI、AI 开发或任何 Milestone 的硬门槛。使用时记录 Kodi 版本、配置、日志和测试结果；

Windows Media Player Legacy 的 DLNA 行为依赖可选系统组件和交互式设置，VLC 的常规 HTTP 播放也不等同于 UPnP AVTransport DMR；两者都不作为标准测试目标。若以后引入其他第三方 Receiver，必须先记录版本、许可证、无界面自动化能力和可复现配置。

## 17.5 稳定性测试

MVP 完成前必须：

- 运行 2 分钟 1080p30；
- 验证内存有界；
- 验证队列不会持续增长；
- 连续 Start/Stop 至少 20 次；
- 多次切换捕获源；
- 测试 MockRenderer 拉流中断、重连和恢复；
- 测试默认音频设备变化；
- 验证 Stop 后端口关闭；
- 验证原生资源释放。

在 `docs/compatibility.md` 记录测试环境：

- Receiver 名称、版本和运行平台；
- MockRenderer 故障配置；
- GetProtocolInfo；
- 可用 Profile；
- 启动耗时；
- 大致延迟；
- 音频表现；
- 已知 Quirk。

若执行了真实设备人工抽查，再额外记录品牌、型号和固件版本。未提供真实电视不得造成测试跳过、验收失败或 Milestone 阻塞。

---

# 18. 里程碑

必须按里程碑推进，并在每个里程碑结束时保持仓库可构建。

## Milestone 0：仓库骨架

交付：

- Solution；
- 项目结构；
- 依赖注入；
- 日志；
- 配置；
- 空 WinUI Shell；
- Test Projects；
- Architecture 文档；
- CI Build。

验收：

- Clean Clone 可构建；
- 测试可运行；
- 应用可正常打开和退出。

## Milestone 1：静态媒体 DLNA 验证

此阶段禁止实现桌面实时捕获。

交付：

- SSDP；
- Device Description；
- AVTransport Client；
- ConnectionManager Client；
- Embedded HTTP Server；
- 已知可播放的 H.264/AAC MPEG-TS Test Clip；
- “测试电视”按钮；
- SetAVTransportURI；
- Play；
- Stop；
- SOAP/HTTP Diagnostics。

验收：

- MockRenderer 能被发现，并能完成 `SetAVTransportURI → Play → HTTP GET → Stop` 的完整链路；
- MockRenderer 主动拉取测试片段，内置检查器确认 MPEG-TS/H.264/AAC；
- MockRenderer 的故障注入与 Cleanup 集成测试通过；
- 应用能说明 Receiver 是否请求了 URL；
- 常见错误有可执行的诊断。

Milestone 1 未完成前，不得进入实时桌面捕获。

## Milestone 2：无音频桌面直播

交付：

- Windows.Graphics.Capture；
- Display Capture；
- H.264 Encode；
- MPEG-TS Mux；
- Continuous HTTP Endpoint；
- Keyframe-aware Startup Buffer；
- 720p30 Compatibility Profile。

验收：

- MockRenderer 连续拉取并验证桌面流至少 2 分钟；
- 新 HTTP 播放请求能在约一个 GOP 内获得可解码起播点；
- Stop 后所有资源释放。

## Milestone 3：系统声音

交付：

- WASAPI Loopback；
- AAC Encode；
- A/V Mux；
- Clock 与 Drift；
- Video-only Fallback。

验收：

- 2 分钟内音画同步可接受；
- 静音不会破坏流；
- 音频设备变化不会导致应用崩溃。

## Milestone 4：GUI 与诊断完善

交付：

- Source Selection；
- Profile Selection；
- State/Statistics；
- Compatibility Override；
- Diagnostics Export；
- Firewall Guidance。

验收：

- 日常使用不依赖命令行；
- 常见失败有可执行说明。

## Milestone 5：HLS 回退

交付：

- In-memory HLS；
- 约 1 秒 Segment；
- Sliding Playlist；
- 有限自动回退；
- HLS MockRenderer Tests。

验收：

- HLS 内存有界；
- 至少一个 Renderer Profile 可使用 HLS；
- Stop 后 Playlist 正确结束并失效。

## Milestone 6：优化与 ARM64 准备

功能稳定后才进行：

- 减少 CPU/GPU Copy；
- 改进 D3D11 Zero-copy；
- 完善硬件编码器诊断；
- 评估 ARM64 Native Build；
- 考虑 1080p60。

没有性能数据时不要提前优化。

---

# 19. 编码规范

## 19.1 通用规则

- 优先小而可审查的改动；
- 每次任务结束保持 Solution 可构建；
- 开启 Nullable Reference Types；
- 项目代码 Warning as Error；
- 统一 Analyzer 和 Format；
- 长操作接收 `CancellationToken`；
- 网络操作有明确 Timeout；
- Channel/Queue 必须有界；
- 确定性释放资源；
- 避免全局可变状态；
- 避免 Service Locator；
- 避免隐藏后台任务；
- 除 GUI Event Handler 外不使用 `async void`；
- UI 路径不调用 `.Result` 或 `.Wait()`；
- 未完成里程碑时不做大范围推测性重构。

## 19.2 添加依赖

添加依赖前必须：

1. 说明平台 API 或现有代码为何不够；
2. 检查维护状态；
3. 检查许可证；
4. 优先选择小而专一的库；
5. 更新第三方声明；
6. 为关键行为增加测试。

只需要 Control Point 时，不要引入完整 UPnP Server Framework。

## 19.3 原生代码

- 使用 RAII；
- 显式管理 COM 初始化和关闭；
- 检查每一个 HRESULT；
- 错误必须附带上下文；
- C++ Exception 不跨 C ABI；
- 使用一种有文档说明的 Timestamp 表示；
- D3D/Media Foundation 所有权明确；
- Stop/Cancel 幂等；
- 回调不得访问已销毁托管对象；
- 禁止无界 Packet Collection。

## 19.4 XML 与网络

- 禁用 DTD 和外部实体；
- 保留 Namespace；
- 正式代码不要手工字符串拼接 XML；
- 验证设备提供的 URL；
- 限制 Response Size；
- 电视响应视为不可信输入；
- SOAP/XML/SSDP 都要有 Timeout；
- 局域网设备发现期间不要任意跟随公网 Redirect；
- 不通过无关 VPN 网卡发送媒体 URL。

---

# 20. AI Agent 工作协议

AI Agent 修改本仓库前必须：

1. 阅读本文件；
2. 阅读相关 `docs/`；
3. 检查现有代码，不得假设仓库为空；
4. 确认当前 Milestone；
5. 说明改动对应哪个验收条件；
6. 实现最小但完整的功能切片；
7. 同步增加或更新测试；
8. 运行格式化、构建和相关测试；
9. 准确报告实际运行的命令；
10. 说明哪些测试因环境限制未运行；
11. 行为变化时更新 Architecture/Protocol 文档；
12. 把硬件依赖放在接口和 Mock 后面；
13. 面对电视兼容性问题时优先增加诊断，而不是猜测。

AI Agent 禁止：

- 没有日志或可复现测试就声称某电视必然如何工作；
- 用推测性重写替换已工作的协议路径；
- 静默降低网络安全；
- 无理由绑定所有网卡；
- 添加云服务；
- 硬编码开发者 IP；
- 硬编码电视 IP；
- 硬编码网卡名；
- 硬编码捕获源 ID；
- 为让 CI 变绿而删除失败测试；
- 只实现 Happy Path 而忽略 Cleanup；
- 在一个不可审查的改动中一次实现全部 Milestone。

AI Agent 不应请求用户把真实电视、家庭局域网或其他私人设备暴露给调试环境。默认应当：

- 增强 MockRenderer；
- 添加协议 Fixture；
- 添加流格式验证；
- 添加诊断；
- 自动执行可复现的集成测试。

真实设备测试仅在用户自愿且能在本地人工执行时提供步骤。AI 不得因无法访问真实设备而声称工作未完成，也不得用真实设备结果替代本应由 MockRenderer 覆盖的断言。

---

# 21. 每次改动的完成定义

改动只有在满足以下条件后才算完成：

- 代码可构建；
- 相关测试通过；
- 已考虑 Cancel；
- 已考虑 Cleanup；
- 日志能解释失败位置；
- 未引入无界队列；
- 未记录 Token 或私有媒体内容；
- 对外行为有文档；
- 新依赖已登记；
- 对当前 Milestone 的验收条件有可验证推进。

协议改动必须附：

- 清理敏感信息后的 Request/Response Fixture。

媒体改动必须附：

- Stream Validation 结果；
- 或可复现的本地验证步骤。

---

# 22. MVP 完成定义

同时满足以下条件才可宣布 MVP 完成：

- GUI 能发现仓库内 MockRenderer，并正确显示其设备信息；
- 能通过 `SetAVTransportURI` 发送测试媒体；
- MockRenderer 能主动拉取测试媒体，且流验证通过；
- 能以 720p30 推送选定 Windows 显示器；
- 可启用系统声音；
- 音画同步可接受；
- MockRenderer 能持续拉取连续 MPEG-TS，且媒体流在规定时间内可解析和解码；
- HLS 可作为回退；
- Start/Stop 不需要命令行；
- 失败能导出诊断报告；
- 2 分钟会话内存有界；
- 多次 Start/Stop 不泄漏 Capture、Audio、HTTP、COM、D3D 或 Muxer 资源；
- 默认不把媒体流暴露到所选局域网范围之外。

---

# 23. 官方技术参考

实现细节不确定时，优先查阅当前官方文档，而不是依赖博客或未经验证的示例。

## Microsoft

- [Windows.Graphics.Capture Namespace](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture)
- [Screen capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [Screen capture to video](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture-video)
- [WASAPI](https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi)
- [Loopback Recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
- [Media Foundation H.264 Video Encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder)
- [Media Foundation AAC Encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/aac-encoder)
- [Media Foundation Codec Objects](https://learn.microsoft.com/en-us/windows/win32/medfound/codecobjects)

## UPnP

- [AVTransport:3 Service Specification](https://upnp.org/specs/av/UPnP-av-AVTransport-v3-Service-20101231.pdf)
- [ConnectionManager:3 Service Specification](https://upnp.org/specs/av/UPnP-av-ConnectionManager-v3-Service.pdf)

## 可选 Windows 黑盒 Receiver

- [Kodi for Windows](https://kodi.tv/download/windows)
- [Kodi UPnP/DLNA Settings](https://kodi.wiki/view/Settings/Services/UPnP_DLNA)
## FFmpeg

- [FFmpeg Formats Documentation](https://ffmpeg.org/ffmpeg-formats.html)
- [FFmpeg Protocols Documentation](https://ffmpeg.org/ffmpeg-protocols.html)
- [FFmpeg License and Legal Considerations](https://ffmpeg.org/legal.html)

遇到电视兼容性要求与标准严格实现不同的情况时：

1. 保留标准路径；
2. 把差异实现为 Renderer Compatibility Option；
3. 记录电视型号、固件、请求和响应；
4. 尽可能添加回归测试。

---

# 24. 第一项实现任务

仓库刚创建时，Agent 的第一项任务不是做桌面捕获。

第一项任务必须是：

> 建立 Milestone 0 的仓库骨架，并实现 Milestone 1 的最小纵向切片：MockRenderer + SSDP 发现 + Device Description 解析 + 本地静态测试媒体 HTTP Endpoint + SetAVTransportURI + Play。

只有该链路在 MockRenderer 中端到端通过，且测试媒体已经过内置检查器与可用时的 `ffprobe` 验证后，才进入实时桌面捕获。Kodi 或真实电视测试可以增加互操作信心，但不阻塞 Milestone 2。


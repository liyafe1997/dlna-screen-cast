# MockRenderer

MockRenderer 是仓库内权威、无界面、可观测且可注入故障的 UPnP/DLNA MediaRenderer。它实现 SSDP、Device Description、AVTransport、ConnectionManager，并在收到 `Play` 后主动拉取和验证媒体。默认仅监听 Loopback，HTTP 与 SSDP 端口由系统动态分配。

## 启动

```powershell
dotnet run --project tools/MockRenderer/DesktopDlnaCast.MockRenderer.csproj -c Release -- --http-port 0 --ssdp-port 0 --method GET
```

标准输出第一行是机器可读的 JSON 就绪事件，包含 HTTP 地址、SSDP 端点和固定测试 UDN。可用端点：

- `GET /health`：就绪探针
- `GET /device.xml`：Device Description
- `POST /upnp/control/avtransport`：AVTransport
- `POST /upnp/control/connectionmanager`：ConnectionManager
- `GET /test/events`：有界机器可读事件日志
- `GET /test/state`：当前 URI、Transport State 和动作历史
- `POST /test/shutdown`：确定性停止进程

事件日志中的完整会话 Token 会被遮蔽；敏感 HTTP Header 值会被替换为 `<redacted>`。

## 故障注入

```text
--method GET|HEAD
--pull-delay-ms <0..60000>
--disconnect-after-bytes <positive integer>
--reject-metadata
--fault-action <SOAP action name>
--forced-transport-state <state>
--header "Name: Value"
```

监听真实局域网必须同时提供 `--listen-address <IPv4>` 与 `--allow-non-loopback`。该开关只用于明确的本地互操作测试；MockRenderer 不依赖公网或真实电视。

自动化测试直接为 SSDP 和 HTTP 分配动态端口，验证发现、去重、能力探测、控制顺序、GET/HEAD、首字节、MPEG-TS/H.264/AAC、Metadata 回退、SOAP Fault、断流和 Cleanup。连续直播在收到受控 Stop/取消时也会验证已经读取的完整 TS 包；仅取消边界处不足一个 188 字节包的尾部数据会被忽略。

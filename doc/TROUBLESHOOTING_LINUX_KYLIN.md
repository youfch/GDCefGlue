# Linux (Kylin V10) 排障指南

GDCefGlue 在 Linux 上运行 CEF 时会遇到一系列平台特有的问题。本文档记录了在 **Kylin V10 SP1**（GLIBC 2.31）上的排查过程和修复方案，适用于所有 Linux 发行版。

> **⚠️ Linux 上必须使用 `scripts/` 下的启动脚本运行项目**
> 
> CEF 的 `libcef.so` 使用 initial-exec TLS 模型，通过 `dlopen` 动态加载时会 SEGV。
> 项目提供的 `scripts/run_gde_linux.sh` 和 `scripts/run_plugin_linux.sh` 会自动设置
> `LD_PRELOAD` 预加载 `libcef.so`，解决此问题。直接运行 Godot 可执行文件会导致崩溃。
> 
> ```bash
> # 将脚本复制到项目根目录后执行
> cp scripts/run_gde_linux.sh ./run.sh && chmod +x ./run.sh && ./run.sh
> ```

---

## 环境信息

| 项目 | 值 |
|------|-----|
| 操作系统 | Kylin V10 SP1 (x86_64) |
| GLIBC | 2.31 |
| GPU | Vulkan 驱动兼容性一般的 GPU |
| Godot | 4.6.3 Mono (linux_x86_64) |
| CEF | 120.1.8 (Chromium 120.0.6099.109) |
| CefGlue | 120.6099.211 (OutSystems/CefGlue) |
| .NET SDK | 8.0.423 + 10.0.302 |

---

## 问题总览

| # | 问题 | 现象 | 修复 |
|---|------|------|------|
| 1 | `MultiThreadedMessageLoop=true` 在 Linux 触发 int3 DCHECK | CEF 初始化时子进程 `trap int3` 崩溃 | Linux 上设 `=false`，改用 `ExternalMessagePump=true` + `_Process` 驱动 `DoMessageLoopWork()` |
| 2 | Zygote 子进程 int3 崩溃 | `--type=zygote` 子进程 `trap int3` | `GodotCefApp` 添加 `--no-zygote`，GPU 开关移到所有进程类型 |
| 3 | `libcef.so` initial-exec TLS 模型，dlopen 加载时 SEGV | 主进程 `general protection fault` in libcef.so | `LD_PRELOAD` 预加载 `libcef.so` |
| 4 | CEF 149 需要 GLIBC 2.34 | `GLIBC_2.34 not found` | 降级到 CEF 120（需要 GLIBC 2.17） |
| 5 | GPU 驱动兼容性 | GPU 进程 `error_code=1002` | `--disable-gpu` + `--use-gl=swiftshader` 软渲染 |

---

## 问题 1：MultiThreadedMessageLoop 在 Linux 触发 int3

### 现象

Godot 日志停在：
```
CefInitializer: Starting CEF initialization...
```

`dmesg` 显示：
```
traps: Xilium.CefGlue.[PID] trap int3 ip:7f... in libcef.so
traps: Godot_v4.6.3-st[PID] general protection fault ip:7f... in libcef.so
```

`int3` 是 CEF 内部的 `DCHECK` 断言失败（release 模式中 DCHECK 转为 `int3` 指令）。

### 根因

`CefSettings.MultiThreadedMessageLoop = true` 让 CEF 在独立线程中运行消息循环。虽然 CEF 文档说 "only supported on Windows and Linux"，但 CEF 120 在 Linux 上实际会触发内部断言。

### 修复

在 `CefInitializer.cs` 中按平台区分：

```csharp
var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

var settings = new CefSettings
{
    // ...
    MultiThreadedMessageLoop = isWindows,   // Windows 用多线程模式
    ExternalMessagePump = !isWindows,       // Linux/macOS 用外部消息泵
    // ...
};

UseExternalMessageLoop = !isWindows;
```

在 `CefGlueControl.Rendering.cs` 的 `_Process` 中驱动 CEF 消息循环：

```csharp
protected override void _Process(double delta)
{
    if (Godot.Engine.Singleton.IsEditorHint()) return;

    // 非 Windows 平台：驱动 CEF 外部消息循环
    if (CefInitializer.UseExternalMessageLoop && CefRuntime.IsInitialized)
    {
        try { CefRuntime.DoMessageLoopWork(); }
        catch { /* CEF 尚未完成初始化或已关闭 */ }
    }

    // ... 原有逻辑
}
```

---

## 问题 2：Zygote 子进程 int3 崩溃

### 现象

`coredumpctl info` 显示崩溃进程的命令行包含 `--type=zygote`：
```
Command Line: .../Xilium.CefGlue.BrowserProcess --type=zygote --no-zygote-sandbox --no-sandbox ...
Signal: 5 (TRAP)
```

### 根因

Linux 上 CEF 默认通过 zygote 进程 fork 出子进程（renderer、gpu 等）。CEF 120 的 zygote 在某些 Linux 环境下会触发 `DCHECK` 断言。

### 修复

在 `GodotCefApp.OnBeforeCommandLineProcessing` 中对 Linux 添加 `--no-zygote`：

```csharp
protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
{
    if (CefRuntime.Platform == CefRuntimePlatform.Linux)
    {
        commandLine.AppendSwitch("no-zygote");
    }
    // ...
}
```

**重要**：禁用 zygote 后，GPU 相关开关（`--disable-gpu` 等）必须移到 `processType` 检查的**外面**，使其应用到所有进程类型（包括 `gpu-process`），否则 GPU 子进程仍会尝试硬件加速并崩溃：

```csharp
// ✅ 正确：GPU 开关在 processType 检查外面
if (!CefGlueControl.UseGpuAcceleration)
{
    commandLine.AppendSwitch("disable-gpu");
    commandLine.AppendSwitch("disable-gpu-compositing");
    commandLine.AppendSwitch("use-angle", "swiftshader");
}

if (string.IsNullOrEmpty(processType))
{
    // 浏览器主进程专用开关...
}
```

参考：[OutSystems/CefGlue](https://github.com/OutSystems/CefGlue/blob/main/CefGlue.Common/BrowserCefApp.cs) 的 `BrowserCefApp.cs` 对 Linux 也是同样处理。

---

## 问题 3：libcef.so TLS 初始化 SEGV（最关键）

### 现象

修复问题 1 和 2 后，子进程不再崩溃，但主进程仍在 `CefRuntime.Initialize()` 中 SEGV：

```
Signal: 11 (SEGV)
Stack trace of thread:
#0  0x... in ?? from .../libcef.so + 0x636652c
```

GDB 反汇编显示崩溃点：
```asm
callq  __tls_get_addr@plt       ; 获取线程局部存储
mov    0x200(%rax),%rcx          ; 读取 TLS 偏移 0x200
test   %rcx,%rcx
je     ...                       ; 非空，继续
mov    0x18(%rcx),%rdx           ; 💥 CRASH - rcx=0x93 不是有效指针
```

### 根因

`libcef.so` 编译时使用了 **initial-exec (IE) TLS 模型**。IE 模型要求共享库在进程启动时就被加载（静态链接或 `LD_PRELOAD`），其 TLS 块会被分配在主线程的静态 TLS 区域。

CefGlue 通过 `dlopen("libcef.so")` 动态加载库（`CefRuntime.Load()` 内部调用），此时 IE TLS 模型无法正确初始化 TLS 块，`__tls_get_addr` 返回的 TLS 偏移指向无效内存，导致解引用时 SEGV。

### 修复

**方法 1（推荐）：运行脚本中设置 `LD_PRELOAD`**

```bash
#!/bin/bash
export DISPLAY=:0

# 预加载 libcef.so，使其 TLS 块在进程启动时分配到静态 TLS 区域
export LD_PRELOAD=/path/to/addons/gdcefglue/linux-x64/libcef.so

/path/to/Godot --path /path/to/project/ --verbose
```

**方法 2：系统级配置**

在 `/etc/ld.so.preload` 中添加路径（影响所有进程，不推荐）。

**方法 3：构建时链接（理想方案，需修改 CefGlue）**

如果 CefGlue 的 `CefRuntime.Load()` 改用 `RTLD_GLOBAL | RTLD_NOW` 标志的 `dlopen`，并在加载后手动调用 `__tls_get_addr` 初始化，可以避免此问题。但这需要修改 CefGlue 源码，目前通过 `LD_PRELOAD` 规避。

### 验证

设置 `LD_PRELOAD` 后日志应显示：
```
CefInitializer: Starting CEF initialization...
CefInitializer: CEF initialized. IsInitialized = True
CefGlueControl: Creating browser 1152x549 @ 60fps (Mode: OSR)
CefGlueControl: Browser initialized
```

---

## 问题 4：CEF 版本与 GLIBC 兼容性

### 现象

```
./libcef.so: /lib/x86_64-linux-gnu/libc.so.6: version `GLIBC_2.34' not found (required by ./libcef.so)
```

### 根因

CEF 149（Chromium 149）的 `libcef.so` 和 `BrowserProcess` 需要 GLIBC 2.34+。Kylin V10 只有 GLIBC 2.31。

### 修复

降级到 CEF 120（Chromium 120.0.6099.109），只需要 GLIBC 2.17：

| CEF 版本 | Chromium | GLIBC 要求 | BrowserProcess GLIBC 要求 |
|----------|----------|-----------|--------------------------|
| 149 | 149 | 2.34 | 2.34 |
| 120 | 120.0.6099.109 | 2.17 | 2.16 |

在 `GDCefGlueExtension.csproj` 中使用 CEF 120 的包：

```xml
<PackageReference Include="cef.redist.linux64" Version="120.1.8" />
<PackageReference Include="CefGlue.Common" Version="120.6099.211" />
<PackageReference Include="chromiumembeddedframework.runtime" Version="120.1.8" />
```

---

## 问题 5：GPU 兼容性

### 现象

```
ERROR:gpu_process_host.cc(986)] GPU process launch failed: error_code=1002
WARNING:gpu_process_host.cc(1362)] The GPU process has crashed N time(s)
FATAL:gpu_data_manager_impl_private.cc(448)] GPU process isn't usable. Goodbye.
```

### 根因

部分 GPU 的 Vulkan/OpenGL 驱动兼容性有限，CEF GPU 进程无法正常初始化。

### 修复

确保场景中 `CefGlueControl` 的 `gpu_acceleration = false`（默认），使 CEF 使用 SwiftShader 软渲染：

```gdscript
var browser = CefGlueControl.new()
browser.gpu_acceleration = false  # 使用软渲染
```

`GodotCefApp` 会自动添加 `--disable-gpu`、`--disable-gpu-compositing`、`--use-angle=swiftshader`。

### 清理无效 Vulkan ICD

如果系统中有多个无效 Vulkan ICD 文件，可能导致冲突：

```bash
# 查看现有 ICD
ls /etc/vulkan/icd.d/

# 移除无效的（保留实际的 GPU 驱动）
sudo rm -f /etc/vulkan/icd.d/xdxgpu_icd.json      # 无效
sudo rm -f /etc/vulkan/icd.d/innoconf.json         # 无效
sudo rm -f /etc/vulkan/icd.d/fh3_conf.json         # 无效
sudo rm -f /etc/vulkan/implicit_layer.d/nvidia_layers.json
```

---

## 依赖检查

### ldd 检查 libcef.so

```bash
ldd ./addons/gdcefglue/linux-x64/libcef.so | grep "not found"
```

正常输出应为空（无缺失依赖）。

### 常见缺失依赖

如果 `ldd` 显示缺失库，安装：

```bash
# Ubuntu/Debian/Kylin
sudo apt install libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 \
    libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libgbm1

# 验证
ldd ./libcef.so | grep "not found"  # 应为空
```

### 文件权限

确保 CEF 二进制有可执行权限：

```bash
chmod +x ./addons/gdcefglue/linux-x64/CefGlueBrowserProcess/Xilium.CefGlue.BrowserProcess
chmod +x ./addons/gdcefglue/linux-x64/libcef.so
```

---

## 调试方法

### 查看 Godot 日志

```bash
godot --path ./project/ --verbose 2>&1 | tee godot.log
```

### 查看 CEF 日志

```bash
cat ~/.local/share/godot/app_userdata/<项目名>/cef_cache/cef.log
```

### 查看 dmesg 崩溃信息

```bash
sudo dmesg | tail -20
# 关注 "traps:" 和 "segfault" 条目
```

### 分析 coredump

```bash
# 列出最近的 coredump
coredumpctl list

# 查看崩溃信息
coredumpctl info <PID>

# 导出 core 文件并用 GDB 分析
coredumpctl dump <PID> -o /tmp/core.raw
sudo gdb -batch -ex "bt" -ex "info registers" /path/to/executable /tmp/core.raw
```

### 关键崩溃模式对照

| dmesg 模式 | 含义 | 对应问题 |
|------------|------|---------|
| `trap int3 ... in libcef.so` | CEF DCHECK 断言失败 | 问题 1 或 2 |
| `general protection fault ... in libcef.so` | SEGV，通常 TLS 相关 | 问题 3 |
| `segfault ... in libc-2.31.so` | GLIBC 兼容问题 | 问题 4 |
| `GPU process launch failed: error_code=1002` | GPU 初始化失败 | 问题 5 |

---

## 完整启动脚本

项目提供了通用启动脚本，自动处理 `LD_PRELOAD` 和路径配置：

```bash
# 查看脚本
cat scripts/run_gde_linux.sh      # GDExtension 版
cat scripts/run_plugin_linux.sh   # Plugin 版
```

### GDExtension

```bash
#!/bin/bash
# 放在项目根目录运行
PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
export DISPLAY=:0

# 预加载 libcef.so
export LD_PRELOAD="$PROJECT_DIR/addons/gdcefglue/linux-x64/libcef.so"

exec godot --path "$PROJECT_DIR" --verbose 2>&1 | tee godot.log
```

### Plugin

```bash
#!/bin/bash
PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
export DISPLAY=:0

# Plugin 模式下 CEF 运行时在 .godot/mono/temp/bin/Debug/CefGlueBrowserProcess/
export LD_PRELOAD="$PROJECT_DIR/.godot/mono/temp/bin/Debug/CefGlueBrowserProcess/libcef.so"

exec godot --path "$PROJECT_DIR" --verbose 2>&1 | tee godot.log
```

---

## 测试验证清单

- [ ] `ldd libcef.so` 无缺失依赖
- [ ] `libcef.so` 和 `BrowserProcess` 有可执行权限
- [ ] 启动脚本设置了 `LD_PRELOAD`
- [ ] 启动脚本设置了 `DISPLAY=:0`（无桌面环境时）
- [ ] 场景中 `gpu_acceleration = false`（GPU 兼容性差时）
- [ ] CEF 版本为 120（GLIBC < 2.34 时）
- [ ] Godot 日志显示 `CEF initialized. IsInitialized = True`
- [ ] Godot 日志显示 `Browser initialized`
- [ ] CEF 日志无 `Could not load resources.pak` 错误

---

## 参考

- [OutSystems/CefGlue](https://github.com/OutSystems/CefGlue) — CefGlue 上游
- [CEF 120 发布说明](https://bitbucket.org/chromiumembedded/cef/wiki/Version120) — CEF 120 变更
- [CefSettings 字段说明](https://github.com/OutSystems/CefGlue/blob/main/CefGlue/Structs/CefSettings.cs) — `MultiThreadedMessageLoop` / `ExternalMessagePump`

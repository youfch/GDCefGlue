# GPU 加速 OSR 全平台方案

> 版本: 2.0
> 日期: 2026-08-10
> 状态: 已定稿，待执行
> 参考实现: [godot-cef](https://github.com/dsh0416/godot-cef) (Rust)

---

## 1. 目标

为 GDCefGlue 的 OSR（Off-Screen Rendering）模式实现全平台 GPU 加速，使 CEF 通过共享纹理（`OnAcceleratedPaint`）直接渲染到 Godot `RenderingDevice`，**消除每一帧的 GPU→CPU 回读**，对标 godot-cef 的 GPU 加速能力。

---

## 2. 现状

### 已实现
- Windows D3D12 后端：`D3D11on12TextureCopier`（Vortice 绑定）
- 条件编译 `GD_GPU_WINDOWS` 隔离非 Windows 平台
- `project.godot` 已配置 `rendering_device/driver.windows="d3d12"`
- GPU 回退逻辑：`OnAcceleratedPaint` 失败时自动切回 CPU `OnPaint`

### 已知问题
- **主线程阻塞**：`WaitForSingleObject(INFINITE)` 阻塞 Godot 主线程等待 fence
- **仅 Windows D3D12**：Linux/macOS 无 GPU 路径
- **仅 x86_64**：ARM64 未覆盖
- **Plugin/Extension 代码独立**：两份重复，不共享（维持现状，不主动合并）

### 与 godot-cef 差距

| 维度 | GDCefGlue | godot-cef | 差距 |
|------|:---:|:---:|:---:|
| Windows D3D12 | ✅ | ✅ | 已对齐 |
| Windows Vulkan | ❌ | ✅ | 未实现 |
| Linux Vulkan | ❌ | ✅ | 未实现 |
| macOS Metal | ❌ | ✅ | 未实现 |
| ARM64 覆盖 | ❌ | ❌（Vulkan hook 仅 x86_64） | 待解决 |
| 非阻塞同步 | ❌ 阻塞 INFINITE | ✅ 双缓冲 fence 轮询 | 需修复 |
| 后端自动检测 | ❌ 硬编码 D3D12 | ✅ RenderBackend::detect() | 需添加 |

---

## 3. 核心架构决策

### 3.1 放弃 JMP Detour，改用 Vulkan Layer

旧方案（Phase 3）计划自写 C# 进程内 detour 库 hook `vkCreateDevice`。**经 [Oracle 验证] 已否决**：

| 维度 | JMP Detour（旧方案） | Vulkan Layer（新方案） |
|------|:---:|:---:|
| NativeAOT 兼容 | ⚠️ 脆弱（VirtualProtect + 自改代码） | ✅ 无自改代码，ABI 层 |
| **ARM64 支持** | ❌ CFG/W^X 阻止，不可移植 | ✅ 架构无关，同 x86_64/ARM64 |
| 实现复杂度 | 高（detour 库 + 反汇编 + 多线程安全） | 低（~200 行 C 纯透传） |
| 部署 | 零配置但脆弱 | `VK_LAYER_PATH` + 打包 `.so/.dll/.dylib` |
| 与 Godot 的兼容性 | 未知（改代码段） | 已验证（Godot 用系统 Vulkan loader） |

**方案**：用 C 写一个微型 Vulkan Layer（`gdcef_vulkan_layer.{dll,so,dylib}`），在 `vkCreateDevice` 中向 `pCreateInfo` 追加外部内存扩展。Layer 是弹性架构，多平台同代码源。

### 3.2 平台优先级

**Linux Vulkan > macOS Metal > Windows Vulkan**

- Linux 无 D3D12，Vulkan 是唯一 GPU 路径，每个 Linux 用户当前都是 CPU 回退（最差体验）
- macOS Metal 零 hook，实现最简单
- Win/Linux Vulkan 共享 ~90% copier 代码，Linux 先做 = Win Vulkan 白捡

### 3.3 Plugin/Extension 代码不共享

维持现状，两份代码各自独立。GPU 逻辑在新开发时注意保持两边同步。

---

## 4. 目标覆盖矩阵

| 平台 | 架构 | 渲染后端 | GPU 路径 | 优先级 | 说明 |
|------|------|---------|---------|:---:|------|
| **Windows** | x86_64 | D3D12 | D3D11on12 | **P0** | 已实现，需修复阻塞同步 |
| **Windows** | ARM64 | D3D12 | D3D11on12 | P2 | 同代码，需验证 |
| **Linux** | x86_64 | Vulkan | 外部内存 + DMA-BUF | **P1** | Vulkan Layer + 独立 copier |
| **Linux** | ARM64 | Vulkan | 外部内存 + DMA-BUF | P1 | Layer 天然支持，需验证 |
| **macOS** | ARM64/x86_64 | Metal | IOSurface 导入 | **P1** | C shim，零 hook |
| **Windows** | x86_64 | Vulkan | 外部内存 + Win32 handle | P2 | 复用 Linux 90% copier |
| 所有平台 | 任意 | OpenGL | 无 | — | 软件回退 |

---

## 5. 分阶段路线图

### Phase 1（P0 — Quick）: 修复 Windows D3D12 阻塞同步

**目标**：消除 `WaitForSingleObject(INFINITE)` 主线程阻塞

**操作**：
- `D3D11on12TextureCopier.ProcessPendingCopy()` 中 fence 等待改为非阻塞轮询（`CompletedValue` 检查）
- 双缓冲帧交换：当前帧 fence 未完成时跳过此帧，保留上一帧数据
- `RetryLater` 路径已存在，只需调整 fence 等待策略

**验证**：运行 GPU 路径，任务管理器确认 CPU 使用率不再出现帧率尖刺

**工作量**：≈ 0.5 天

---

### Phase 2（P1 — Medium）: macOS Metal

**目标**：macOS 下通过 IOSurface 实现 GPU 加速 OSR

**架构**：
```
C shim (MetalTextureCopier.mm)
  ├── dlopen + objc_getClass 获取 MTLDevice
  ├── newTextureWithDescriptor:iosurface:plane: 导入 IOSurface
  ├── blitCommandEncoder 从源纹理拷贝到 Godot 纹理
  └── 返回 blittable void* 给 C# 侧

C# 侧 (GpuTextureCopier.macOS.cs)
  ├── P/Invoke 调用 C shim
  └── 通过 RenderingDevice.GetDriverResource(Texture) 获取 Godot 的 Metal 纹理
```

**关键点**：
- 不通过 ObjCRuntime（Xamarin 已死），不通过 Silk.NET.Metal（覆盖不全）
- 用 C shim 暴露 `MTLTextureCopy(void* srcTex, void* dstTex, int w, int h) → bool`
- C# 侧只传 `IntPtr`，零结构体编组风险
- macOS 无 Hook 需求，Metal 原生支持 IOSurface 导入

**文件**：
- 新增 `plugin/addons/GCefGlue/GpuTextureCopier.macOS.cs`
- 新增 `plugin/addons/GCefGlue/Native/MetalTextureCopier.mm`（构建时编译）
- Extension 同名

**验证**：macOS 上打开 OSR 标签，GPU 路径生效，`OnAcceleratedPaint` 被调用

**工作量**：≈ 1 天

---

### Phase 3（P1 — Large）: Vulkan Layer + Linux Vulkan

**目标**：Linux 下通过 Vulkan 外部内存 + DMA-BUF 实现 GPU 加速 OSR

#### 3.1 Vulkan Layer（C 项目）

**文件**：`externals/gdcef_vulkan_layer/vulkan_layer.c`

**逻辑**：
```c
// 1. 拦截 vkCreateDevice
VkResult VKAPI_CALL vkCreateDevice(
    VkPhysicalDevice device,
    const VkDeviceCreateInfo* pCreateInfo,
    const VkAllocationCallbacks* pAllocator,
    VkDevice* pDevice)
{
    // 2. 检查 pCreateInfo 是否已包含所需扩展
    // 3. 若未包含，构建新 extension list，追加：
    //    - VK_KHR_external_memory
    //    - VK_KHR_external_memory_fd
    //    - VK_EXT_external_memory_dma_buf
    //    - VK_EXT_image_drm_format_modifier
    //    - VK_EXT_queue_family_foreign
    // 4. 调用原始 vkCreateDevice（通过 dlsym 获取）
    // 5. 将结果写入 pDevice
}
```

**约束**：
- Layer API 版本 1.2（Godot 4.x 要求）
- 正确链式传递 `pNext`（避免与 Nsight/PerfStudio 冲突）
- 仅注入必要的扩展，不盲目追加
- 同时支持 `vkCreateInstance` 拦截（如果需要）

**部署**：
- 构建产物：`gdcef_vulkan_layer.so`（Linux）、`gdcef_vulkan_layer.dll`（Windows）
- 打包位置：随 `addons/GCefGlue/` 或 `addons/gdcefglue/` 分发
- 加载方式：Plugin 在 `CefInitializer.Initialize()` 前设置 `VK_LAYER_PATH` + `VK_INSTANCE_LAYERS`

#### 3.2 Linux Vulkan Copier

**文件**：`GpuTextureCopier.Linux.cs`

**流程**：
```
CEF 渲染 → DMA-BUF fd
    ↓ libc::dup(fd) 延长生命周期
    ↓ VkImportMemoryFdInfoKHR
    ↓   handle_type = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_EXT
Vulkan Image (TRANSFER_SRC)
    ↓ vkCmdCopyImage（独立 queue，避免阻塞 Godot 主队列）
    ↓ pipeline barrier → VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
Godot 的 Vulkan 纹理
    ↓ CanvasItemAddTextureRect 渲染
```

**同步机制**（参考 godot-cef 双缓冲设计）：
```
Phase 1: 非阻塞 fence 轮询（timeout=0），跳过帧保留上一帧数据
Phase 2: 双缓冲帧交换（command_buffers[2], fences[2]）
```

**NVIDIA 要求**：`nvidia-drm.modeset=1` 内核参数（与 godot-cef 一致）

**工作量**：≈ 2 天

---

### Phase 4（P2 — Medium）: Windows Vulkan

**目标**：Windows 上 Vulkan 后端的 GPU 加速

**操作**：
- 复用 Phase 3 的 Vulkan Layer（`gdcef_vulkan_layer.dll`），注入 `VK_KHR_external_memory_win32` 替代 DMA-BUF
- 复用 Linux copier 代码，替换 handle 类型：
  - `VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT`（Win32 khandle）
  - `vkGetMemoryWin32HandlePropertiesKHR` 替代 `vkGetMemoryFdPropertiesKHR`
- 使用 `DuplicateHandle` 确保句柄生命周期（参考 godot-cef 的 `duplicate_win32_handle`）

**工作量**：≈ 0.5 天（Linux copier 完成后）

---

### Phase 5（P2 — Short）: 后端自动检测 + 遥测

**目标**：运行时自动检测渲染后端，决定 GPU 路径

**接口**（参考 `RenderBackend` 设计）：
```csharp
enum GpuBackend { D3D12, Vulkan, Metal, OpenGL, Unknown }

static GpuBackend DetectBackend()
{
    var driver = RenderingServer.GetRenderingDevice().GetCurrentRenderingDriverName();
    // "d3d12" → D3D12, "vulkan" → Vulkan, "metal" → Metal, "opengl" → OpenGL
}
```

**日志输出**：
```
[CefGlueControl] Detected render backend: D3D12 (driver: d3d12)
[CefGlueControl] GPU acceleration: enabled (D3D11on12)
[CefGlueControl] GPU acceleration: not available for this backend, falling back to CPU
```

**工作量**：≈ 0.5 天

---

### Phase 6（P2 — Short）: ARM64 验证

**目标**：验证 Layer + copier 在 ARM64 平台正常工作

**验证点**：
- Windows ARM64 + D3D12：现有 D3D11on12 路径（无需 Layer）
- Linux ARM64 + Vulkan + Layer：Layer 加载正常，扩展注入成功
- macOS ARM64 + Metal：Phase 2 已覆盖

**工作量**：≈ 0.5 天（测试周期）

---

## 6. 回退策略

```
OnAcceleratedPaint 被调用？
  ├── 是 → 走对应平台的 GPU copier
  │     ├── 成功 → 更新 GPU 纹理，_gpuTextureDirty = true
  │     └── 失败/超时 → 跳过此帧，保留上一帧数据，不切回 CPU
  └── 否（SharedTextureEnabled 不支持/未启用）
        └── 保持当前 OnPaint CPU 路径
```

**关键原则**：GPU 路径失败时永不切回 CPU 路径（避免模式切换毛刺），而是保留上一帧 GPU 纹理。

---

## 7. 风险清单

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| **Vulkan 纹理格式不匹配** | 静默黑帧 | 在 copier 初始化时验证格式兼容性，打印日志 |
| **Layer 加载时机太晚** | 扩展未注入，GPU 路径失败 | Plugin 在 `_Ready` 最前设置 `VK_LAYER_PATH`；Extension 在 `_init` 设置 |
| **Linux NVIDIA DMA-BUF** | 加速不可用 | 文档要求 `nvidia-drm.modeset=1`；Layer 加载失败时自动回退 CPU |
| **NativeAOT 下 Vortice 裁剪** | D3D12 路径崩溃 | 备选方案：CsWin32 重写 D3D11on12 copier |
| **macOS Metal shim 加载** | 找不到符号 | 用 `[DllImport("__Internal")]` + NativeAOT 静态链接 shim |
| **CEF 版本升级** | SharedTexture 行为变化 | 关注 CefGlue 更新日志，预留 OnPaint 回退路径 |

---

## 8. 工作量汇总

| Phase | 内容 | 工作量 | 风险 |
|:---:|------|:---:|:---:|
| P1 | 修复 D3D12 阻塞同步 | 0.5 天 | 低 |
| P2 | macOS Metal 加速 | 1 天 | 低 |
| P3 | Vulkan Layer + Linux | 2 天 | 中（Layer 加载时机） |
| P4 | Windows Vulkan | 0.5 天 | 低（复用 Linux） |
| P5 | 后端自动检测 | 0.5 天 | 低 |
| P6 | ARM64 验证 | 0.5 天 | 低 |
| **合计** | | **≈ 5 天** | |

---

## 9. 文件变更清单

### Phase 1（修复 D3D12 阻塞）
| 文件 | 位置 | 操作 |
|------|------|:----:|
| `GpuTextureCopier.Windows.cs` | plugin + extension | 修改 |

### Phase 2（macOS Metal）
| 文件 | 位置 | 操作 |
|------|------|:----:|
| `GpuTextureCopier.macOS.cs` | plugin + extension | 新增 |
| `MetalTextureCopier.mm` | plugin + extension | 新增（C shim） |
| `CefGlueControl.AcceleratedPaint.cs` | plugin + extension | 修改（注册 Metal 路径） |

### Phase 3（Vulkan Layer + Linux）
| 文件 | 位置 | 操作 |
|------|------|:----:|
| `externals/gdcef_vulkan_layer/vulkan_layer.c` | 独立 C 项目 | 新增 |
| `externals/gdcef_vulkan_layer/CMakeLists.txt` | 独立 C 项目 | 新增 |
| `GpuTextureCopier.Linux.cs` | plugin + extension | 新增 |
| 构建脚本（Linux CI） | 新增 | 编译 Layer .so |

### Phase 4（Windows Vulkan）
| 文件 | 位置 | 操作 |
|------|------|:----:|
| `GpuTextureCopier.Windows.Vulkan.cs` | plugin + extension | 新增 |
| 构建脚本（Windows CI） | 新增 | 编译 Layer .dll |

### Phase 5（后端检测）
| 文件 | 位置 | 操作 |
|------|------|:----:|
| `CefGlueControl.Properties.cs` | plugin + extension | 修改（添加 GpuBackend 属性） |
| `CefGlueControl.Initialization.cs` | plugin + extension | 修改（添加检测逻辑） |

---

## 10. 附录

### 10.1 Vulkan Layer 技术细节

Vulkan Layer 是 Vulkan 加载器（`vulkan-1.dll` / `libvulkan.so.1`）的原生扩展机制。Layer 在 `VK_LAYER_PATH` 指定的路径中通过 JSON manifest 文件注册：

```json
{
  "file_format_version": "1.0.0",
  "layer": {
    "name": "VK_LAYER_GDCEF_EXTMEM",
    "type": "GLOBAL",
    "api_version": "1.2.200",
    "implementation_version": "1",
    "description": "GDCefGlue: injects external memory extensions for GPU-accelerated OSR",
    "functions": {
      "vkGetDeviceProcAddr": "gdcef_vulkan_layer_GetDeviceProcAddr",
      "vkCreateDevice": "gdcef_vulkan_layer_CreateDevice"
    }
  }
}
```

### 10.2 参考实现

- [godot-cef accelerated_osr/windows/vulkan.rs](https://github.com/dsh0416/godot-cef/blob/main/crates/gdcef/src/accelerated_osr/windows/vulkan.rs) — Windows Vulkan 纹理导入
- [godot-cef accelerated_osr/linux/vulkan.rs](https://github.com/dsh0416/godot-cef/blob/main/crates/gdcef/src/accelerated_osr/linux/vulkan.rs) — Linux DMA-BUF 纹理导入
- [godot-cef accelerated_osr/macos.rs](https://github.com/dsh0416/godot-cef/blob/main/crates/gdcef/src/accelerated_osr/macos.rs) — macOS Metal IOSurface 导入
- [godot-cef vulkan_hook](https://github.com/dsh0416/godot-cef/tree/main/crates/gdcef/src/vulkan_hook) — 参考其扩展注入逻辑（用 Layer 替换其 JMP hook）
- [godot-proposals#13969](https://github.com/godotengine/godot-proposals/issues/13969) — 向 Godot 请求原生支持外部内存扩展
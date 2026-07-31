# GPU 加速 OSR 实现计划

## 目标
为 GDCefGlue 的 OSR 模式增加 GPU 共享纹理加速（替代当前的 CPU `OnPaint` 像素拷贝），使其可配置、可回退。

## 架构

```
CefGlueControl
  ├── GpuAccelerationMode = 0 (Disabled)
  │   └── OnPaint → 当前 CPU 像素拷贝 (BGRA→RGBA → ImageTexture)
  │
  └── GpuAccelerationMode > 0 (Enabled)
      └── OnAcceleratedPaint → GPU 加速
          ├── Windows D3D12  →  D3D11on12TextureCopier (CsWin32)
          ├── macOS Metal    →  MetalTextureCopier (ObjCRuntime)
          └── Linux Vulkan   →  [暂不支持，回退 OnPaint]
```

## 阶段

### Phase 1: Infrastructure（P0 - Windows D3D12）

1. 定义 `ITextureCopier` 接口
2. 实现 `D3D11on12TextureCopier`：CsWin32 生成 D3D11on12 API，从 Godot 拿 D3D12 device，导入 CEF 共享纹理，GPU 拷贝
3. 修改 `CefGlueControl.SetAsWindowless` 设置 `SharedTextureEnabled = true`
4. 实现 `OnAcceleratedPaint` 分派逻辑
5. 添加 `GpuAccelerationMode` 导出属性

### Phase 2: macOS Metal（P1）

1. 实现 `MetalTextureCopier`：通过 ObjCRuntime 调用 Metal API，从 Godot 拿 Metal device，IOSurface 导入，blit copy
2. 适配 `OnAcceleratedPaint` 的 macOS 路径

### Phase 3: Function Detour 库（储备）

用于 Linux/Windows Vulkan 方案的 `vkCreateDevice` hook。C# 实现函数 detour 库，类似 Rust 的 `retour`。

| 方法 | 说明 | 状态 |
|---|---|---|
| **A: Godot 编译时开启扩展** | 需要改 Godot 源码，不现实 | ❌ 放弃 |
| **B: C# 函数 detour hook** | 自己实现 detour 库，`mprotect` + JMP 跳转 | ✅ 保留 |
| **C: NativeAOT 预加载钩子** | 通过 NativeAOT 的早期入口注入 | ✅ 保留 |
| **D: CPU 回退** | Linux 继续走 OnPaint | ✅ 当前方案 |

#### Detour 库设计要点

```
Detour<TDelegate>
  ├── 平台抽象: mprotect (Linux) / VirtualProtect (Windows)
  ├── x86_64: JMP [rip+offset] 绝对跳转 (12-14 bytes)
  ├── ARM64: 预留
  ├── Trampoline: 保存原始函数前 N 字节 + 跳回
  └── 线程安全: 原子操作 + 内存屏障
```

需要处理的细节：
- x86_64 相对跳转指令长度（至少 14 字节：`mov rax, addr; jmp rax`）
- 多线程下修改代码段的原子性
- 函数前 N 字节的反汇编（需要知道哪些指令是多字节的）
- 懒加载：`OnceLock` 模式

### Phase 4: Linux/Windows Vulkan（保留）

依赖 Phase 3 的 detour 库：
- 在 Godot 创建 Vulkan 设备前 hook `vkCreateDevice`
- 注入 `VK_EXT_external_memory_dma_buf`（Linux）或 `VK_KHR_external_memory_win32`（Windows）
- 实现 Vulkan 外部内存导入 + `vkCmdCopyImage`

## 回退策略

```
OnAcceleratedPaint 被调用？
  ├── 是 → 走对应的 GPU copier
  │     ├── 成功 → 更新 GPU 纹理
  │     └── 失败 → 标记回退，下次切回 OnPaint
  └── 否（SharedTextureEnabled 不支持）
        └── 保持当前 OnPaint CPU 路径
```

## 文件结构

```
addons/GCefGlue/
  ├── CefGlueControl.Rendering.cs          # 不变，OnPaint CPU 回退
  ├── CefGlueControl.AcceleratedPaint.cs   # 新增：OnAcceleratedPaint + 分派
  ├── GpuTextureCopier.cs                  # 新增：ITextureCopier 接口 + 工厂
  ├── GpuTextureCopier.Windows.cs          # 新增：D3D11on12 实现
  ├── GpuTextureCopier.macOS.cs            # 新增：Metal 实现
  ├── Detour/                              # 新增：函数 detour 库
  │   ├── Detour.cs                        # 泛型 Detour<T> 核心
  │   ├── NativeApi.cs                     # mprotect/VirtualProtect P/Invoke
  │   └── ArchWriter.cs                    # x86_64 JMP 生成
  └── CefGlueControl.Properties.cs         # 修改：添加 GpuAccelerationMode
```
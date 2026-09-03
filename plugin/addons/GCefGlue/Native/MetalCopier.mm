//
// MetalCopier.mm
//
// GDCefGlue — macOS Metal GPU 纹理拷贝原生 shim。
//
// 为什么需要这个 shim？
// ---------------------------------------------------------------
// 在 C# 侧直接通过 objc_msgSend P/Invoke 调用 Metal API 非常脆弱：
//   1. objc_msgSend 的 ABI 变体繁多（返回值大小、浮点返回值、结构体返回等），
//      手写签名极易出错，且不同 CPU 架构（x86_64 / arm64）行为不同。
//   2. Metal 的 block 回调、autorelease pool、ARC 内存管理在 C# 侧难以正确对齐。
//   3. 使用 Silk.NET 等包装库会引入不必要的 NuGet 依赖。
// 因此我们用一个极小的 Objective-C++ shim，通过 4 个扁平 C 函数暴露能力，
// 由 C# 用 P/Invoke 调用。shim 内部全权负责 Metal 的创建、导入与 blit 拷贝。
//
// 整个流程（对应 C# GpuTextureCopier.macOS.cs）：
//   1. Initialize : 通过 Godot RenderingDevice 拿到 MTLDevice 指针，
//                  传入 gdcef_metal_create 创建命令队列包装（ctx）。
//   2. QueueCopy  : CEF UI 线程回调，仅 CFRetain IOSurface 并登记待处理，
//                  不在此处做任何 Metal 操作（保持非阻塞）。
//   3. ProcessPendingCopy : Godot 主线程，用 gdcef_metal_import_io_surface
//                  把 IOSurface 包装成 MTLTexture，再用 gdcef_metal_copy
//                  blit 到 Godot 的目标纹理，同步等待完成。
//
// 编译：本文件需在 macOS 上以 clang -fobjc-arc 编译（另链接 Metal、
//       IOSurface、CoreFoundation 框架）。由外部构建脚本负责。
// 本文件与 extension/Native/MetalCopier.mm 保持完全一致（仅仓库位置不同）。
//
// #pragma once
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <IOSurface/IOSurface.h>
#import <CoreFoundation/CoreFoundation.h>
#import <stdlib.h>

#ifdef __cplusplus
extern "C" {
#endif

// ─────────────────────────────────────────────────────────────
// 上下文对象：持有 Metal 设备（引用自 Godot，retain 保护）与自有命令队列。
// C# 侧仅把它当作不透明指针（nint）传递。
// ─────────────────────────────────────────────────────────────
typedef struct MetalCopierContext {
    id<MTLDevice>         device;        // 引用自 Godot 的 MTLDevice（已 retain）
    id<MTLCommandQueue>   commandQueue;  // 我们创建的独立命令队列
} MetalCopierContext;

// ─────────────────────────────────────────────────────────────
// gdcef_metal_create
// 用 Godot 提供的 MTLDevice 指针创建命令队列包装。
// 返回：非 NULL 表示成功（调用方负责用 gdcef_metal_destroy 释放）。
// ─────────────────────────────────────────────────────────────
void* gdcef_metal_create(void* mtlDevice) {
    if (mtlDevice == NULL) {
        return NULL;
    }

    // 不拥有 Godot 的设备，但为了保险起见 retain 一次，
    // 避免在使用期间 Godot 侧意外释放导致野指针。
    id<MTLDevice> device = (__bridge id<MTLDevice>)mtlDevice;
    [device retain];

    id<MTLCommandQueue> commandQueue = [device newCommandQueue];
    if (commandQueue == nil) {
        [device release];
        return NULL;
    }

    MetalCopierContext* ctx = (MetalCopierContext*)calloc(1, sizeof(MetalCopierContext));
    if (ctx == NULL) {
        [commandQueue release];
        [device release];
        return NULL;
    }

    ctx->device = device;
    ctx->commandQueue = commandQueue;
    return ctx;
}

// ─────────────────────────────────────────────────────────────
// gdcef_metal_destroy
// 释放命令队列包装及其持有的引用。
// ─────────────────────────────────────────────────────────────
void gdcef_metal_destroy(void* ctxPtr) {
    if (ctxPtr == NULL) {
        return;
    }
    MetalCopierContext* ctx = (MetalCopierContext*)ctxPtr;

    if (ctx->commandQueue != nil) {
        [ctx->commandQueue release];
        ctx->commandQueue = nil;
    }
    if (ctx->device != nil) {
        [ctx->device release];
        ctx->device = nil;
    }
    free(ctx);
}

// ─────────────────────────────────────────────────────────────
// gdcef_metal_import_io_surface
// 把 CEF 提供的 IOSurface 包装成 Metal 2D 纹理。
//   - format: 0 = BGRA8Unorm_sRGB（默认，对应 CEF BGRA_8888）
//             1 = RGBA8Unorm_sRGB（对应 CEF RGBA_8888）
// 返回：非 NULL 表示成功。返回的纹理指针已 retain（+1），
//       用完必须由 gdcef_metal_release_texture 释放，否则泄漏。
// ─────────────────────────────────────────────────────────────
void* gdcef_metal_import_io_surface(void* ctxPtr, void* ioSurfacePtr,
                                    int width, int height, int format) {
    if (ctxPtr == NULL || ioSurfacePtr == NULL || width <= 0 || height <= 0) {
        return NULL;
    }
    MetalCopierContext* ctx = (MetalCopierContext*)ctxPtr;
    IOSurfaceRef ioSurface = (IOSurfaceRef)ioSurfacePtr;

    // 使用 sRGB 格式保证网页内容的 gamma 处理正确（对齐 godot-cef 参考实现）
    MTLPixelFormat pixelFormat = MTLPixelFormatBGRA8Unorm_sRGB;
    if (format == 1) {
        pixelFormat = MTLPixelFormatRGBA8Unorm_sRGB;
    }

    MTLTextureDescriptor* desc =
        [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:pixelFormat
                                                           width:(NSUInteger)width
                                                          height:(NSUInteger)height
                                                       mipmapped:NO];
    desc.textureType = MTLTextureType2D;
    desc.usage = MTLTextureUsageShaderRead;
    desc.storageMode = MTLStorageModeShared;

    id<MTLTexture> texture = [ctx->device newTextureWithDescriptor:desc
                                                          iosurface:ioSurface
                                                             plane:0];
    if (texture == nil) {
        return NULL;
    }

    // newTextureWithDescriptor: 返回 +1 引用，用 __bridge_retained 移交所有权，
    // 由 C# 侧持有不透明指针，最终用 CFBridgingRelease 平衡。
    return (__bridge_retained void*)texture;
}

// ─────────────────────────────────────────────────────────────
// gdcef_metal_copy
// 把 srcTexture blit 拷贝到 dstTexture，并同步等待 GPU 完成。
// 返回：1 = 成功，0 = 失败。
// ─────────────────────────────────────────────────────────────
int gdcef_metal_copy(void* ctxPtr, void* srcPtr, void* dstPtr, int width, int height) {
    if (ctxPtr == NULL || srcPtr == NULL || dstPtr == NULL) {
        return 0;
    }
    MetalCopierContext* ctx = (MetalCopierContext*)ctxPtr;
    id<MTLTexture> src = (__bridge id<MTLTexture>)srcPtr;
    id<MTLTexture> dst = (__bridge id<MTLTexture>)dstPtr;

    @autoreleasepool {
        id<MTLCommandBuffer> commandBuffer = [ctx->commandQueue commandBuffer];
        if (commandBuffer == nil) {
            return 0;
        }
        id<MTLBlitCommandEncoder> encoder = [commandBuffer blitCommandEncoder];
        if (encoder == nil) {
            return 0;
        }

        MTLOrigin origin = {0, 0, 0};
        MTLSize size = {(NSUInteger)width, (NSUInteger)height, 1};

        [encoder copyFromTexture:src
                    sourceSlice:0     sourceLevel:0
                  sourceOrigin:origin sourceSize:size
                     toTexture:dst     destinationSlice:0 destinationLevel:0
             destinationOrigin:origin];
        [encoder endEncoding];

        [commandBuffer commit];
        [commandBuffer waitUntilCompleted];
    }

    return 1;
}

// ─────────────────────────────────────────────────────────────
// gdcef_metal_release_texture
// 释放 gdcef_metal_import_io_surface 返回的纹理（平衡其 +1 引用）。
// ─────────────────────────────────────────────────────────────
void gdcef_metal_release_texture(void* texturePtr) {
    if (texturePtr == NULL) {
        return;
    }
    CFBridgingRelease(texturePtr);
}

#ifdef __cplusplus
}
#endif
/*
 * vulkan_layer.c
 *
 * GDCefGlue 专用的最小化 Vulkan 验证层（Vulkan Layer）。
 *
 * 唯一职责：在 Godot 的 RenderingDevice 创建逻辑设备时，注入外部内存
 * （external-memory）相关的 Vulkan 设备扩展，使跨 API 的 GPU 纹理共享
 * （加速离屏渲染 OSR）能够工作。
 *
 * 背景：
 *   Godot 4.x 的 RenderingDevice 默认并不启用这些外部内存扩展，导致
 *   CEF 无法把 GPU 纹理通过平台句柄（Windows HANDLE / Linux DMA-BUF fd）
 *   直接共享给 Godot。本项目参考 godot-cef（Rust + JMP detour）的实现，
 *   改用「Vulkan Layer」方案注入扩展，因为它与 CPU 架构无关（ARM64 同样
 *   有效）并且对 NativeAOT 安全。
 *
 * 本层遵循标准 Vulkan Layer 接口：
 *   - 导出 vkGetInstanceProcAddr / vkGetDeviceProcAddr（加载器入口）
 *   - 拦截 vkCreateDevice，在调用真实实现前追加扩展
 *   - 仅拦截 vkCreateInstance 用于获取下一层的 dispatch 指针（不改变其行为）
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>

#include <vulkan/vulkan.h>

/* =========================================================================
 * 加载器专用结构（vulkan_core.h 只定义了枚举值，不包含这些结构体，
 * 它们来自 Vulkan Loader 的 vk_layer.h，此处自行定义，避免依赖。
 *
 * Vulkan 加载器在创建实例时，会通过 pCreateInfo->pNext 链传递一个
 * VkLayerInstanceCreateInfo 结构，其中携带了「下一层」的 dispatch 函数
 * 指针。层需要从中取出链路信息，然后推进链路指针，再把调用转发给下一层。
 * ========================================================================= */

/* VkLayerFunction 枚举，定义层的链路功能类型，与 Vulkan Loader 保持一致 */
typedef enum VkLayerFunction_ {
    VK_LAYER_LINK_INFO          = 0,
    VK_LAYER_DEVICE_LINK_INFO   = 1,
} VkLayerFunction;

/* PFN_GetPhysicalDeviceProcAddr 类型，Vulkan Loader 在 VkLayerInstanceLink
 * 中使用，但不在公共 Vulkan 头文件中，此处自行定义。 */
typedef PFN_vkVoidFunction (VKAPI_PTR *PFN_GetPhysicalDeviceProcAddr)(
    VkInstance instance, const char *pName);

/* 实例层链路节点：保存下一层的 vkGetInstanceProcAddr 和
 * vkGetPhysicalDeviceProcAddr。必须与 Vulkan Loader 的
 * VkLayerInstanceLink 结构完全一致（字段个数和顺序），否则通过 pNext
 * 指针访问链表时会导致内存偏移错误。 */
typedef struct VkLayerInstanceLink_ {
    struct VkLayerInstanceLink_ *pNext;
    PFN_vkGetInstanceProcAddr pfnNextGetInstanceProcAddr;
    PFN_GetPhysicalDeviceProcAddr pfnNextGetPhysicalDeviceProcAddr;
} VkLayerInstanceLink;

/* 实例创建时的层信息，由加载器塞进 pCreateInfo->pNext 链。
 * 本层需要从中取出 pLayerInfo（链路链表头），保存自己的下一层指针，
 * 然后把 pLayerInfo 推进到下一个节点，以便下一层使用。
 *
 * 注意：加载器定义中 pLayerInfo 为 const 指针，但标准 Layer 做法是
 * 通过 const_cast 修改它（推进链路），因此此处保留 const 以匹配加载器
 * 的实际结构布局，需要修改时主动 cast away const。 */
typedef struct VkLayerInstanceCreateInfo_ {
    VkStructureType sType;
    const void *pNext;
    VkLayerFunction function;
    const struct VkLayerInstanceLink_ *pLayerInfo;
} VkLayerInstanceCreateInfo;

/* =========================================================================
 * 需要注入的扩展列表（按平台选择）
 *
 * 与 godot-cef 的 vulkan_hook/windows.rs 与 linux.rs 保持一致：
 *   - Windows：VK_KHR_external_memory + VK_KHR_external_memory_win32
 *               （通过 Windows HANDLE 在 Godot 与 CEF 之间共享纹理）
 *   - Linux  ：VK_KHR_external_memory + VK_KHR_external_memory_fd
 *               + VK_EXT_external_memory_dma_buf
 *               + VK_EXT_image_drm_format_modifier
 *               + VK_EXT_queue_family_foreign
 *               （通过 DMA-BUF 文件描述符共享纹理）
 * ========================================================================= */

#ifdef _WIN32
static const char *const kRequiredExtensions[] = {
    "VK_KHR_external_memory",
    "VK_KHR_external_memory_win32",
};
static const uint32_t kRequiredExtensionCount = 2;
#else
static const char *const kRequiredExtensions[] = {
    "VK_KHR_external_memory",
    "VK_KHR_external_memory_fd",
    "VK_EXT_external_memory_dma_buf",
    "VK_EXT_image_drm_format_modifier",
    "VK_EXT_queue_family_foreign",
};
static const uint32_t kRequiredExtensionCount = 5;
#endif

/* 最大扩展数（用于固定数组，避免 VLA / MSVC 兼容性问题） */
#define MAX_REQUIRED_EXTENSIONS 5

/* =========================================================================
 * 下一层 dispatch 指针（全局）
 *
 * 在 gdcef_vkCreateInstance 中初始化，之后为只读。
 * 注意：Vulkan 加载器先调用 vkGetInstanceProcAddr 获取本层的入口，再调用
 * vkCreateInstance 传递链路信息。因此这些全局变量在 vkCreateDevice 被调用
 * 之前必定已经就绪。
 * ========================================================================= */

static PFN_vkGetInstanceProcAddr g_pfnNextGetInstanceProcAddr = NULL;
static PFN_vkGetDeviceProcAddr   g_pfnNextGetDeviceProcAddr   = NULL;

static PFN_vkCreateInstance                       g_pfnNextCreateInstance = NULL;
static PFN_vkCreateDevice                         g_pfnNextCreateDevice   = NULL;
static PFN_vkEnumerateDeviceExtensionProperties   g_pfnNextEnumerateDeviceExtensionProperties = NULL;

/* 前向声明 */
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetInstanceProcAddr(
    VkInstance instance, const char *pName);
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(
    VkDevice device, const char *pName);

VKAPI_ATTR VkResult VKAPI_CALL gdcef_vkCreateInstance(
    const VkInstanceCreateInfo *pCreateInfo,
    const VkAllocationCallbacks *pAllocator,
    VkInstance *pInstance);

VKAPI_ATTR VkResult VKAPI_CALL gdcef_vkCreateDevice(
    VkPhysicalDevice physicalDevice,
    const VkDeviceCreateInfo *pCreateInfo,
    const VkAllocationCallbacks *pAllocator,
    VkDevice *pDevice);

/* =========================================================================
 * 工具函数
 * ========================================================================= */

/* 检查目标扩展是否已经被调用方在 VkDeviceCreateInfo 中显式启用 */
static bool extension_already_enabled(
    const VkDeviceCreateInfo *pCreateInfo, const char *extName)
{
    if (pCreateInfo == NULL ||
        pCreateInfo->enabledExtensionCount == 0 ||
        pCreateInfo->ppEnabledExtensionNames == NULL)
    {
        return false;
    }
    for (uint32_t i = 0; i < pCreateInfo->enabledExtensionCount; ++i) {
        const char *name = pCreateInfo->ppEnabledExtensionNames[i];
        if (name != NULL && strcmp(name, extName) == 0) {
            return true;
        }
    }
    return false;
}

/* 检查物理设备是否支持某个扩展
 * 通过下一层的 vkEnumerateDeviceExtensionProperties 查询 */
static bool device_supports_extension(
    VkPhysicalDevice physicalDevice, const char *extName)
{
    if (g_pfnNextEnumerateDeviceExtensionProperties == NULL) {
        return false;
    }

    uint32_t count = 0;
    VkResult result = g_pfnNextEnumerateDeviceExtensionProperties(
        physicalDevice, NULL, &count, NULL);
    if (result != VK_SUCCESS || count == 0) {
        return false;
    }

    VkExtensionProperties *props =
        (VkExtensionProperties *)malloc((size_t)count * sizeof(VkExtensionProperties));
    if (props == NULL) {
        return false;
    }

    result = g_pfnNextEnumerateDeviceExtensionProperties(
        physicalDevice, NULL, &count, props);
    if (result != VK_SUCCESS) {
        free(props);
        return false;
    }

    bool found = false;
    for (uint32_t i = 0; i < count; ++i) {
        /* extensionName 是定长 char[VK_MAX_EXTENSION_NAME_SIZE]，以 '\0' 结尾 */
        if (strcmp(props[i].extensionName, extName) == 0) {
            found = true;
            break;
        }
    }

    free(props);
    return found;
}

/* =========================================================================
 * 拦截后的 vkCreateDevice —— 本层核心逻辑
 *
 * 流程：
 *   1. 对每个需要注入的扩展：
 *        a) 若调用方已启用该扩展 -> 跳过（no-op）
 *        b) 若物理设备不支持该扩展 -> 跳过（no-op）
 *        c) 否则 -> 记录下来，准备注入
 *   2. 若没有需要注入的扩展 -> 直接透传给下一层
 *   3. 否则，重建扩展名列表（保留原有 + 追加新扩展），构造一个新的
 *      VkDeviceCreateInfo，保持 pNext 链不变，再调用下一层的 vkCreateDevice
 * ========================================================================= */

VKAPI_ATTR VkResult VKAPI_CALL gdcef_vkCreateDevice(
    VkPhysicalDevice physicalDevice,
    const VkDeviceCreateInfo *pCreateInfo,
    const VkAllocationCallbacks *pAllocator,
    VkDevice *pDevice)
{
    if (pCreateInfo == NULL || g_pfnNextCreateDevice == NULL) {
        fprintf(stderr, "[GDCefVulkanLayer] ERROR: vkCreateDevice chained with "
                "pCreateInfo=%p, next=%p\n",
                (const void *)pCreateInfo, (void *)g_pfnNextCreateDevice);
        if (g_pfnNextCreateDevice != NULL) {
            return g_pfnNextCreateDevice(
                physicalDevice, pCreateInfo, pAllocator, pDevice);
        }
        return VK_ERROR_INITIALIZATION_FAILED;
    }

    /* ---- 第 1 步：确定哪些扩展需要注入 ---- */
    /* 用固定数组（而非 VLA），确保 MSVC 兼容性 */
    bool toAdd[MAX_REQUIRED_EXTENSIONS];
    uint32_t numToAdd = 0;

    for (uint32_t i = 0; i < kRequiredExtensionCount; ++i) {
        const char *ext = kRequiredExtensions[i];

        /* 情况 a：已显式启用，跳过 */
        if (extension_already_enabled(pCreateInfo, ext)) {
            toAdd[i] = false;
            fprintf(stderr, "[GDCefVulkanLayer] %s already enabled, skipping\n", ext);
            continue;
        }

        /* 情况 b：物理设备不支持，跳过（让应用继续，不打断流程） */
        if (!device_supports_extension(physicalDevice, ext)) {
            toAdd[i] = false;
            fprintf(stderr, "[GDCefVulkanLayer] %s not supported by device, skipping\n", ext);
            continue;
        }

        /* 情况 c：需要注入 */
        toAdd[i] = true;
        ++numToAdd;
        fprintf(stderr, "[GDCefVulkanLayer] will inject %s\n", ext);
    }

    /* ---- 第 2 步：无需注入，直接透传 ---- */
    if (numToAdd == 0) {
        return g_pfnNextCreateDevice(
            physicalDevice, pCreateInfo, pAllocator, pDevice);
    }

    /* ---- 第 3 步：重建扩展名列表 ---- */
    const uint32_t originalCount = pCreateInfo->enabledExtensionCount;
    const uint32_t newCount = originalCount + numToAdd;

    /* 新扩展数组：先把原有扩展名指针复制进去，再追加需要注入的扩展。
     * 注意：我们只复制指针，不复制字符串本身。字符串都是静态常量，
     * 在程序生命周期内有效。 */
    const char **newExtensions =
        (const char **)malloc((size_t)newCount * sizeof(const char *));
    if (newExtensions == NULL) {
        return VK_ERROR_OUT_OF_HOST_MEMORY;
    }

    /* 复制原有扩展名指针 */
    for (uint32_t i = 0; i < originalCount; ++i) {
        newExtensions[i] = pCreateInfo->ppEnabledExtensionNames[i];
    }

    /* 追加需要注入的扩展 */
    uint32_t idx = originalCount;
    for (uint32_t i = 0; i < kRequiredExtensionCount; ++i) {
        if (toAdd[i]) {
            newExtensions[idx++] = kRequiredExtensions[i];
        }
    }

    /* 拷贝原 VkDeviceCreateInfo，仅替换扩展名列表。
     * pNext 链保持不变 —— 不破坏调用方传入的任何结构。 */
    VkDeviceCreateInfo modifiedInfo = *pCreateInfo;
    modifiedInfo.enabledExtensionCount = newCount;
    modifiedInfo.ppEnabledExtensionNames = newExtensions;

    fprintf(stderr, "[GDCefVulkanLayer] injecting %u external-memory extension(s), "
            "total enabled now = %u\n", numToAdd, newCount);

    VkResult result = g_pfnNextCreateDevice(
        physicalDevice, &modifiedInfo, pAllocator, pDevice);

    /* modifiedInfo 是栈上拷贝，但 newExtensions 是我们 malloc 的，需释放 */
    free(newExtensions);

    if (result == VK_SUCCESS) {
        fprintf(stderr, "[GDCefVulkanLayer] device created with external-memory extensions\n");
    } else {
        fprintf(stderr, "[GDCefVulkanLayer] vkCreateDevice failed: %d\n", (int)result);
    }

    return result;
}

/* =========================================================================
 * 拦截后的 vkCreateInstance —— 仅用于获取下一层 dispatch，不改行为
 *
 * 加载器在调用本层的 vkCreateInstance 时，会在 pCreateInfo->pNext 链中放入
 * VkLayerInstanceCreateInfo（function == VK_LAYER_LINK_INFO），其 pLayerInfo
 * 指向一个 VkLayerInstanceLink 链表头。链表中的每个节点对应一个层，节点内
 * 保存了该层的「下一层」dispatch 函数指针。
 *
 * 本层需要：
 *   1. 从链表头取出第一个节点，获得下一层的 vkGetInstanceProcAddr
 *   2. 把链表头推进到下一个节点（pLayerInfo = pLayerInfo->pNext），
 *      这样下一层调用 vkCreateInstance 时，就能正确处理自己的链路信息
 *   3. 用下一层的 GIPA 获取下一层的 vkCreateInstance / vkCreateDevice /
 *      vkEnumerateDeviceExtensionProperties / vkGetDeviceProcAddr
 *   4. 把原始 pCreateInfo 转发给下一层（不修改任何内容）
 * ========================================================================= */

VKAPI_ATTR VkResult VKAPI_CALL gdcef_vkCreateInstance(
    const VkInstanceCreateInfo *pCreateInfo,
    const VkAllocationCallbacks *pAllocator,
    VkInstance *pInstance)
{
    if (pCreateInfo == NULL) {
        return VK_ERROR_INITIALIZATION_FAILED;
    }

    /* 在 pNext 链中查找加载器注入的层链路信息 */
    const void *chain = pCreateInfo->pNext;
    const VkLayerInstanceCreateInfo *layerInfo = NULL;

    while (chain != NULL) {
        const VkLayerInstanceCreateInfo *info =
            (const VkLayerInstanceCreateInfo *)chain;
        if (info->sType == VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO &&
            info->function == VK_LAYER_LINK_INFO) {
            layerInfo = info;
            break;
        }
        chain = info->pNext;
    }

    /* 安全检查：没有链路信息意味着本层不是被加载器调用的，无法转发 */
    if (layerInfo == NULL || layerInfo->pLayerInfo == NULL ||
        layerInfo->pLayerInfo->pfnNextGetInstanceProcAddr == NULL) {
        fprintf(stderr, "[GDCefVulkanLayer] ERROR: no loader layer link info found in "
                "vkCreateInstance pNext chain\n");
        return VK_ERROR_INITIALIZATION_FAILED;
    }

    /* ---- 第 1 步：取出下一层的 vkGetInstanceProcAddr ---- */
    const VkLayerInstanceLink *currentLink = layerInfo->pLayerInfo;
    g_pfnNextGetInstanceProcAddr = currentLink->pfnNextGetInstanceProcAddr;

    /* ---- 第 2 步：推进链路指针，让下一层能拿到正确的链路节点 ---- */
    /* 标准 Vulkan Layer 做法：pLayerInfo 是一个链表，每个节点对应一个层。
     * 本层取走第一个节点后，把链表头推进到下一个节点。这样下一层调用
     * vkCreateInstance 时，就能在 pNext 链中找到 VkLayerInstanceCreateInfo，
     * 并且其 pLayerInfo 已经指向了下一层自己的链路节点。
     *
     * 加载器定义的 pLayerInfo 是 const 指针，但 Vulkan Layer 规范要求层
     * 必须推进它，因此在此处 const_cast 是标准做法。 */
    {
        VkLayerInstanceCreateInfo *mutableInfo =
            (VkLayerInstanceCreateInfo *)layerInfo;
        mutableInfo->pLayerInfo = (const struct VkLayerInstanceLink_ *)currentLink->pNext;
    }

    /* ---- 第 3 步：获取下一层的各函数指针 ---- */
    g_pfnNextCreateInstance = (PFN_vkCreateInstance)
        g_pfnNextGetInstanceProcAddr(NULL, "vkCreateInstance");
    if (g_pfnNextCreateInstance == NULL) {
        fprintf(stderr, "[GDCefVulkanLayer] ERROR: next layer has no vkCreateInstance\n");
        return VK_ERROR_INITIALIZATION_FAILED;
    }

    g_pfnNextCreateDevice = (PFN_vkCreateDevice)
        g_pfnNextGetInstanceProcAddr(NULL, "vkCreateDevice");
    g_pfnNextEnumerateDeviceExtensionProperties =
        (PFN_vkEnumerateDeviceExtensionProperties)
            g_pfnNextGetInstanceProcAddr(NULL, "vkEnumerateDeviceExtensionProperties");
    g_pfnNextGetDeviceProcAddr = (PFN_vkGetDeviceProcAddr)
        g_pfnNextGetInstanceProcAddr(NULL, "vkGetDeviceProcAddr");

    fprintf(stderr, "[GDCefVulkanLayer] initialized, chaining to next vkCreateInstance\n");

    /* ---- 第 4 步：把原始 pCreateInfo 原样转发给下一层 ---- */
    return g_pfnNextCreateInstance(pCreateInfo, pAllocator, pInstance);
}

/* =========================================================================
 * 标准层导出入口
 * ========================================================================= */

/* vkGetInstanceProcAddr：加载器通过它获取本层提供的函数。
 * 对本层拦截的函数返回本层实现；其余函数转发给下一层。 */
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetInstanceProcAddr(
    VkInstance instance, const char *pName)
{
    if (pName == NULL) {
        return NULL;
    }

    /* 本层拦截的函数 */
    if (strcmp(pName, "vkCreateInstance") == 0) {
        return (PFN_vkVoidFunction)gdcef_vkCreateInstance;
    }
    if (strcmp(pName, "vkCreateDevice") == 0) {
        return (PFN_vkVoidFunction)gdcef_vkCreateDevice;
    }
    if (strcmp(pName, "vkGetInstanceProcAddr") == 0) {
        return (PFN_vkVoidFunction)vkGetInstanceProcAddr;
    }
    if (strcmp(pName, "vkGetDeviceProcAddr") == 0) {
        return (PFN_vkVoidFunction)vkGetDeviceProcAddr;
    }

    /* 其余函数交给下一层处理 */
    if (g_pfnNextGetInstanceProcAddr != NULL) {
        return g_pfnNextGetInstanceProcAddr(instance, pName);
    }

    return NULL;
}

/* vkGetDeviceProcAddr：本层不拦截任何设备级函数，全部转发给下一层。 */
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(
    VkDevice device, const char *pName)
{
    if (pName == NULL) {
        return NULL;
    }

    if (g_pfnNextGetDeviceProcAddr != NULL) {
        return g_pfnNextGetDeviceProcAddr(device, pName);
    }

    return NULL;
}
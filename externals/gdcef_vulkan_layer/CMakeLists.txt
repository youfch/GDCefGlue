cmake_minimum_required(VERSION 3.16)
project(gdcef_vulkan_layer
    LANGUAGES C
    VERSION 1.0.0
    DESCRIPTION "GDCefGlue: injects external-memory Vulkan extensions for GPU-accelerated OSR"
)

# 要求 C11 (C99 也可，本层只用到 C99 特性，但使用 C11 以获得更好的兼容性)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# 查找 Vulkan 头文件（不链接 Vulkan 库：层由加载器加载，运行时自动解析符号）
find_package(Vulkan REQUIRED)

# 共享库：gdcef_vulkan_layer.so (Linux) / gdcef_vulkan_layer.dll (Windows)
# CMake 会自动在 Linux 上添加 lib 前缀和 .so 后缀，Windows 上添加 .dll 后缀。
# 输出名称设为 gdcef_vulkan_layer，CMake 会生成：
#   Linux:   libgdcef_vulkan_layer.so
#   Windows: gdcef_vulkan_layer.dll
add_library(gdcef_vulkan_layer SHARED
    vulkan_layer.c
)

# 链接 Vulkan 头文件（仅头文件，不链接库）
target_include_directories(gdcef_vulkan_layer PRIVATE
    ${Vulkan_INCLUDE_DIRS}
)

# 编译选项
# -fPIC: Linux 共享库必需（CMake 对 SHARED 目标默认添加，此写在这里作显式说明）
# 不链接任何外部库 —— 本层是纯 C 代码，仅依赖 Vulkan 头文件中的类型定义
target_compile_options(gdcef_vulkan_layer PRIVATE
    $<$<PLATFORM_ID:Linux>: -fPIC>
    $<$<PLATFORM_ID:Windows>:>
)

# 无需链接 Vulkan 加载器库：层由 Vulkan Loader 加载，
# 所有 Vulkan 函数指针通过 dispatch 表在运行时获取
target_link_libraries(gdcef_vulkan_layer PRIVATE
    # 空：不链接任何库
)

# 设置输出目录：构建到 ${CMAKE_BINARY_DIR}/output/
set_target_properties(gdcef_vulkan_layer PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/output"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/output"
)

# 安装配置
install(TARGETS gdcef_vulkan_layer
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin
)

# 安装层 manifest
install(FILES gdcef_vulkan_layer.json
    DESTINATION share/vulkan/explicit_layer.d
)

message(STATUS "GDCefGlue Vulkan layer configured")
message(STATUS "  Target:        gdcef_vulkan_layer")
message(STATUS "  Output:        ${CMAKE_BINARY_DIR}/output/")
message(STATUS "  Vulkan SDK:    ${Vulkan_INCLUDE_DIRS}")
message(STATUS "  C Standard:    C${CMAKE_C_STANDARD}")
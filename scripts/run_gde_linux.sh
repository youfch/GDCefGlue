#!/bin/bash
# GDCefGlue GDExtension 启动脚本 (Linux)
# 用法: 放在项目根目录，chmod +x 后运行
# 也可复制到上级目录，修改 PROJECT_DIR 为相对路径

set -e

# 项目路径（相对于脚本位置，或绝对路径）
PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"

export DISPLAY=:0
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

# ------------------------------------------------------------
# 【关键】预加载 libcef.so 解决 TLS 初始化问题
# CEF 使用 initial-exec TLS 模型，通过 dlopen 加载时 TLS 块无法正确初始化。
# LD_PRELOAD 确保 libcef.so 在进程启动时加载到静态 TLS 区域。
# ------------------------------------------------------------
CEF_LIB_DIR="$PROJECT_DIR/addons/gdcefglue/linux-x64"
if [ -f "$CEF_LIB_DIR/libcef.so" ]; then
    export LD_PRELOAD="$CEF_LIB_DIR/libcef.so"
else
    echo "WARNING: libcef.so not found at $CEF_LIB_DIR"
    echo "LD_PRELOAD 未设置，CEF 可能在 Linux 上 TLS 初始化失败！"
fi

# Godot 可执行路径（按需修改）
GODOT="/usr/bin/godot"

# 启动
exec "$GODOT" --path "$PROJECT_DIR" --verbose 2>&1 | tee "$PROJECT_DIR/godot.log"
#!/bin/bash
# GDCefGlue Plugin 启动脚本 (Linux)
# 用法: 放在项目根目录，chmod +x 后运行

set -e

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"

export DISPLAY=:0
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

# ------------------------------------------------------------
# 【关键】预加载 libcef.so 解决 TLS 初始化问题
# Plugin 模式下 CEF 运行时在 .godot/mono/temp/bin/Debug/CefGlueBrowserProcess/
# ------------------------------------------------------------
CEF_LIB_DIR="$PROJECT_DIR/.godot/mono/temp/bin/Debug/CefGlueBrowserProcess"
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
#!/bin/bash
set -e

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
export DISPLAY=:0

# Wayland 会话下 Xwayland 的授权文件
XAUTH=$(ls /run/user/$(id -u)/.mutter-Xwaylandauth.* 2>/dev/null | head -1)
if [ -n "$XAUTH" ]; then
    export XAUTHORITY="$XAUTH"
fi

export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

# Plugin 模式：LD_PRELOAD 指向调试构建目录的 libcef.so
CEF_LIB_DIR="$PROJECT_DIR/plugin/.godot/mono/temp/bin/Debug/CefGlueBrowserProcess"
if [ -f "$CEF_LIB_DIR/libcef.so" ]; then
    export LD_PRELOAD="$CEF_LIB_DIR/libcef.so"
else
    echo "WARNING: libcef.so not found at $CEF_LIB_DIR"
    echo "请先构建 plugin 项目，或手动编译 C# 脚本"
fi

GODOT="/home/rootu/App/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"

exec "$GODOT" --path "$PROJECT_DIR/plugin/" --verbose 2>&1 | tee "$PROJECT_DIR/godot_plugin.log"
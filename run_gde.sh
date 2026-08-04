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

# LD_PRELOAD libcef.so 解决 TLS 初始化问题
CEF_LIB_DIR="$PROJECT_DIR/test/GDExtensionGame/addons/gdcefglue/linux-x64"
export LD_PRELOAD="$CEF_LIB_DIR/libcef.so"

GODOT="/home/rootu/App/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"

exec "$GODOT" --path "$PROJECT_DIR/test/GDExtensionGame/" --verbose 2>&1 | tee "$PROJECT_DIR/godot_gde.log"
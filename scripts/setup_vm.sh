#!/bin/bash
set -e

GDE_DIR="/home/rootu/WorkSpace/Hub/Hub_GDCefGlue/test/GDExtensionGame/addons/gdcefglue/linux-x64"
CEF_DIR="/home/rootu/.nuget/packages/cef.redist.linux64/149.0.4/CEF"
BP_DIR="/home/rootu/.nuget/packages/cefglue.browserprocess.runtime.linux-x64/149.7827.156/build/bin/linux-x64"
PUB_DIR="/home/rootu/WorkSpace/Hub/Hub_GDCefGlue/extension/bin/Release/net10.0/linux-x64/publish"

echo "=== 1. Creating directories ==="
mkdir -p "$GDE_DIR/CefGlueBrowserProcess"

echo "=== 2. Copying CEF runtime ==="
cp "$CEF_DIR/libcef.so" "$GDE_DIR/"
cp "$CEF_DIR/resources.pak" "$GDE_DIR/"
cp "$CEF_DIR/chrome_100_percent.pak" "$GDE_DIR/"
cp "$CEF_DIR/chrome_200_percent.pak" "$GDE_DIR/"
cp "$CEF_DIR/icudtl.dat" "$GDE_DIR/"
cp "$CEF_DIR/v8_context_snapshot.bin" "$GDE_DIR/"
cp -r "$CEF_DIR/locales" "$GDE_DIR/"

echo "=== 3. Copying GPU libs ==="
cp "$CEF_DIR/libEGL.so" "$GDE_DIR/" 2>/dev/null || true
cp "$CEF_DIR/libGLESv2.so" "$GDE_DIR/" 2>/dev/null || true
cp "$CEF_DIR/libvk_swiftshader.so" "$GDE_DIR/" 2>/dev/null || true
cp "$CEF_DIR/libvulkan.so.1" "$GDE_DIR/" 2>/dev/null || true
cp "$CEF_DIR/vk_swiftshader_icd.json" "$GDE_DIR/" 2>/dev/null || true

echo "=== 4. Copying BrowserProcess ==="
cp "$BP_DIR/Xilium.CefGlue.BrowserProcess" "$GDE_DIR/CefGlueBrowserProcess/"
if [ -f "$BP_DIR/Xilium.CefGlue.BrowserProcess.dll" ]; then
    cp "$BP_DIR/Xilium.CefGlue.BrowserProcess.dll" "$GDE_DIR/CefGlueBrowserProcess/"
fi
if [ -f "$BP_DIR/Xilium.CefGlue.BrowserProcess.dbg" ]; then
    cp "$BP_DIR/Xilium.CefGlue.BrowserProcess.dbg" "$GDE_DIR/CefGlueBrowserProcess/"
fi
if [ -f "$BP_DIR/Xilium.CefGlue.BrowserProcess.xml" ]; then
    cp "$BP_DIR/Xilium.CefGlue.BrowserProcess.xml" "$GDE_DIR/CefGlueBrowserProcess/"
fi
if [ -f "$BP_DIR/Xilium.CefGlue.BrowserProcess.deps.json" ]; then
    cp "$BP_DIR/Xilium.CefGlue.BrowserProcess.deps.json" "$GDE_DIR/CefGlueBrowserProcess/"
fi
if [ -f "$BP_DIR/Xilium.CefGlue.BrowserProcess.runtimeconfig.json" ]; then
    cp "$BP_DIR/Xilium.CefGlue.BrowserProcess.runtimeconfig.json" "$GDE_DIR/CefGlueBrowserProcess/"
fi

# Copy CefGlue DLLs from publish output
if [ -d "$PUB_DIR/CefGlueBrowserProcess" ]; then
    cp "$PUB_DIR/CefGlueBrowserProcess/"*.dll "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"*.deps.json "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"*.runtimeconfig.json "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"*.pak "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"libcef.so "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp -r "$PUB_DIR/CefGlueBrowserProcess/locales" "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"icudtl.dat "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"v8_context_snapshot.bin "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
    cp "$PUB_DIR/CefGlueBrowserProcess/"snapshot_blob.bin "$GDE_DIR/CefGlueBrowserProcess/" 2>/dev/null || true
fi

echo "=== 5. Copying GDCefGlueExtension.so ==="
cp "$PUB_DIR/GDCefGlueExtension.so" "$GDE_DIR/"
chmod +x "$GDE_DIR/GDCefGlueExtension.so"
chmod +x "$GDE_DIR/libcef.so"
chmod +x "$GDE_DIR/CefGlueBrowserProcess/Xilium.CefGlue.BrowserProcess" 2>/dev/null || true

echo "=== GDE dir ==="
ls -la "$GDE_DIR/"
echo "=== BrowserProcess dir ==="
ls -la "$GDE_DIR/CefGlueBrowserProcess/" | head -10
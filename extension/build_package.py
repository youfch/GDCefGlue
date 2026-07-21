#!/usr/bin/env python3
"""
Single-platform packaging script for GDCefGlue GDExtension.

C# NativeAOT cannot cross-compile — this script builds ONLY for the current
platform. For multi-platform distribution, use the GitHub Actions workflow
(.github/workflows/build.yml) which runs a matrix of three runners.

Usage:
    python build_package.py             # Build + package for current platform
    python build_package.py --no-build  # Only collect/repackage (skip dotnet publish)
    python build_package.py --rid win-x64  # Force RID (override auto-detect)
"""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
EXTENSION_DIR = REPO_ROOT / "extension"
OUTPUT_DIR = REPO_ROOT / "addons" / "gdcefglue"

# Platform definitions
# Platform definitions: file names are auto-detected (AOT output may vary)
PLATFORMS = {
    "win-x64":  {"dir": "windows", "bp": "Xilium.CefGlue.BrowserProcess.exe"},
    "linux-x64": {"dir": "linux",   "bp": "Xilium.CefGlue.BrowserProcess"},
    "osx-x64":  {"dir": "macos",   "bp": "Xilium.CefGlue.BrowserProcess"},
    "osx-arm64": {"dir": "macos",  "bp": "Xilium.CefGlue.BrowserProcess"},
}

# CEF runtime files per platform
CEF_FILES = {
    "windows": [
        "libcef.dll", "chrome_100_percent.pak", "chrome_200_percent.pak",
        "resources.pak", "icudtl.dat", "v8_context_snapshot.bin",
        "vk_swiftshader.dll", "vk_swiftshader_icd.json", "vulkan-1.dll",
        "chrome_elf.dll", "d3dcompiler_47.dll", "libEGL.dll", "libGLESv2.dll",
    ],
    "linux": [
        "libcef.so", "chrome_100_percent.pak", "chrome_200_percent.pak",
        "resources.pak", "icudtl.dat", "v8_context_snapshot.bin",
        "libEGL.so", "libGLESv2.so", "libvk_swiftshader.so", "libvulkan.so.1",
        "vk_swiftshader_icd.json",
    ],
    "macos": [
        "libcef.dylib", "chrome_100_percent.pak", "chrome_200_percent.pak",
        "resources.pak", "icudtl.dat", "v8_context_snapshot.bin",
    ],
}

# NuGet packages that provide CEF files per platform
CEF_PACKAGES = {
    "windows": ("chromiumembeddedframework.runtime.win-x64", "149.0.4", "runtimes/win-x64/native"),
    "linux":   ("cef.redist.linux64", "149.0.4", "CEF"),
    "macos":   ("cef.redist.osx64", "149.0.4", "CEF"),
}

# BrowserProcess NuGet packages (AOT, platform-specific)
BP_PACKAGES = {
    "win-x64":  "CefGlue.BrowserProcess.runtime.win-x64",
    "linux-x64": "CefGlue.BrowserProcess.runtime.linux-x64",
    "osx-x64":  "CefGlue.BrowserProcess.runtime.osx-x64",
    "osx-arm64": "CefGlue.BrowserProcess.runtime.osx-arm64",
}


def detect_rid():
    """Auto-detect the current platform's RID."""
    import platform as plat
    system = plat.system().lower()
    machine = plat.machine().lower()
    if system == "windows":
        return "win-x64"
    elif system == "linux":
        return "linux-x64" if "x86_64" in machine else "linux-arm64"
    elif system == "darwin":
        return "osx-arm64" if "arm" in machine else "osx-x64"
    return None


def get_nuget_cache():
    r = subprocess.run(["dotnet", "nuget", "locals", "global-packages", "--list"],
                       capture_output=True, text=True, check=True)
    return r.stdout.strip().split(":", 1)[1].strip()


def find_pkg(cache, pkg_id, version):
    p = Path(cache) / pkg_id.lower() / version
    return p if p.exists() else None


def run(cmd, **kw):
    print(f"[exec] {' '.join(cmd)}")
    subprocess.check_call(cmd, **kw)


def copy_cef_files(platform_dir, nuget_cache):
    pkg_id, ver, subdir = CEF_PACKAGES[platform_dir]
    pkg = find_pkg(nuget_cache, pkg_id, ver)
    if not pkg:
        print(f"  [SKIP] {pkg_id} not found in NuGet cache")
        return

    src = pkg / subdir
    dst = OUTPUT_DIR / platform_dir
    dst.mkdir(parents=True, exist_ok=True)

    for f in CEF_FILES[platform_dir]:
        s = src / f
        if s.exists():
            shutil.copy2(s, dst / f)
            print(f"  {f}")

    # locales
    locales_src = src / "locales"
    if locales_src.exists():
        (dst / "locales").mkdir(exist_ok=True)
        for lf in locales_src.glob("*.pak"):
            shutil.copy2(lf, dst / "locales" / lf.name)
            print(f"  locales/{lf.name}")


def copy_browser_process(rid, nuget_cache):
    bp_pkg = BP_PACKAGES.get(rid)
    if not bp_pkg:
        print(f"  [SKIP] No BrowserProcess package for {rid}")
        return

    pkg = find_pkg(nuget_cache, bp_pkg, "149.7827.156")
    if not pkg:
        print(f"  [SKIP] {bp_pkg} not found in NuGet cache")
        return

    info = PLATFORMS[rid]
    bp_name = info["bp"]
    bp_src = pkg / bp_name
    if not bp_src.exists():
        # Try runtimes subfolder
        rid_dir = {"win-x64": "win-x64", "linux-x64": "linux-x64", "osx-x64": "osx-x64", "osx-arm64": "osx-arm64"}[rid]
        bp_src = pkg / "runtimes" / rid_dir / "native" / bp_name

    if bp_src.exists():
        bp_dir = OUTPUT_DIR / info["dir"] / "CefGlueBrowserProcess"
        bp_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(bp_src, bp_dir / bp_name)
        if not rid.startswith("win"):
            os.chmod(bp_dir / bp_name, 0o755)
        print(f"  {bp_name}")
    else:
        print(f"  [SKIP] {bp_name} not found in package")


def copy_gdextension_lib(rid):
    info = PLATFORMS[rid]
    publish = EXTENSION_DIR / "bin" / "Release" / "net10.0" / rid / "publish"
    if not publish.exists():
        print(f"  [ERROR] Publish directory not found: {publish}")
        return False

    # Try multiple naming patterns (AOT output varies by platform)
    candidates = list(publish.glob("GDCefGlueExtension*")) + list(publish.glob("libGDCefGlueExtension*"))
    if not candidates:
        print(f"  [ERROR] GDCefGlueExtension native library not found in {publish}")
        for f in publish.iterdir():
            print(f"    {f.name}")
        return False

    lib_src = candidates[0]
    dst = OUTPUT_DIR / info["dir"]
    dst.mkdir(parents=True, exist_ok=True)
    shutil.copy2(lib_src, dst / lib_src.name)
    print(f"  {lib_src.name}")
    return True


def main():
    parser = argparse.ArgumentParser(description="Build GDCefGlue GDExtension for current platform")
    parser.add_argument("--no-build", action="store_true", help="Skip dotnet publish")
    parser.add_argument("--rid", choices=list(PLATFORMS.keys()), help="Override RID detection")
    parser.add_argument("--nuget-cache", help="NuGet global packages path")
    args = parser.parse_args()

    rid = args.rid or detect_rid()
    if not rid or rid not in PLATFORMS:
        print(f"Unsupported platform: {sys.platform}")
        sys.exit(1)

    platform_dir = PLATFORMS[rid]["dir"]
    nuget_cache = args.nuget_cache or get_nuget_cache()
    print(f"Platform: {rid} → {platform_dir}")
    print(f"NuGet:    {nuget_cache}")

    # Step 1: Build
    if not args.no_build:
        print("\n=== dotnet publish ===")
        run(["dotnet", "publish", str(EXTENSION_DIR / "GDCefGlueExtension.csproj"),
             "-c", "Release", "-r", rid, "-p:PublishAot=true"],
            cwd=str(EXTENSION_DIR))

    # Step 2: Assemble
    print("\n=== Collecting artifacts ===")
    if not copy_gdextension_lib(rid):
        sys.exit(1)
    copy_cef_files(platform_dir, nuget_cache)
    copy_browser_process(rid, nuget_cache)

    print(f"\n=== Package ready: {OUTPUT_DIR / platform_dir} ===")
    print("To distribute all platforms, run this script on each OS and merge the outputs.")


if __name__ == "__main__":
    main()
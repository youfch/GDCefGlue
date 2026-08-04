import sys
p = "/home/rootu/WorkSpace/Hub/Hub_GDCefGlue/godot_gde.log"
with open(p, "r", errors="replace") as f:
    lines = f.readlines()
print("=== Total lines:", len(lines), "===")
for i, l in enumerate(lines):
    if "Embedded" in l or "CefGlueControl" in l or "CefInitializer" in l or "BadWindow" in l or "error" in l.lower() or "Window" in l:
        print(f"{i+1}: {l.rstrip()}")
print("=== Last 10 ===")
print("".join(lines[-10:]))

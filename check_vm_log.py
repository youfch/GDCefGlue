import sys
p = "/home/rootu/WorkSpace/Hub/Hub_GDCefGlue/godot_gde.log"
with open(p, "r", errors="replace") as f:
    lines = f.readlines()
print("=== Total lines:", len(lines), "===")
for i, l in enumerate(lines):
    if "BadWindow" in l or "XServer" in l or "error" in l.lower() or "Error" in l:
        print(f"{i+1}: {l.rstrip()}")
print("=== Last 20 ===")
print("".join(lines[-20:]))

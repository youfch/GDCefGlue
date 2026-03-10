using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;

[assembly: DisableGodotEntryPointGeneration]
[assembly: DisableRuntimeMarshalling]

namespace GDCefGlueExtension;

public class Main
{
    public static void InitializeCefGlueTypes(InitializationLevel level)
    {
        if (level != InitializationLevel.Scene)
        {
            return;
        }

        GodotRegistry.RegisterClass<CefGlueControl>(CefGlueControl.BindMembers);
    }

    public static void DeinitializeCefGlueTypes(InitializationLevel level)
    {
        if (level != InitializationLevel.Scene)
        {
            return;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "gdcefglue_library_init")]
    public static bool GDCefGlueLibraryInit(nint getProcAddress, nint library, nint initialization)
    {
        GodotBridge.Initialize(getProcAddress, library, initialization, config =>
        {
            config.SetMinimumLibraryInitializationLevel(InitializationLevel.Scene);
            config.RegisterInitializer(InitializeCefGlueTypes);
            config.RegisterTerminator(DeinitializeCefGlueTypes);
        });

        return true;
    }
}

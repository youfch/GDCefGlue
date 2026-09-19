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

        // 编辑器进程内把 gdcefglue/* 注册进「项目设置」面板。
        // GDExtension 构建不需要 plugin.cfg、也不需要用户手动启用任何东西 ——
        // 原生库本来就会被编辑器加载，这里注册即落到编辑器的 ProjectSettings 上。
        // 注册失败绝不能影响扩展初始化，故整体 try/catch。
        if (Godot.Engine.Singleton.IsEditorHint())
        {
            try
            {
                CefInitializer.RegisterProjectSettings();
            }
            catch (System.Exception ex)
            {
                GD.PushWarning($"[GDCefGlue] 注册项目设置失败: {ex.Message}");
            }
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

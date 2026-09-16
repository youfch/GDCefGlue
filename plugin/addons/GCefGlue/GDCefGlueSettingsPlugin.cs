#if TOOLS
using Godot;

namespace GDCefGlue
{
    /// <summary>
    /// 唯一职责：把 gdcefglue/* 注册进「项目设置」面板，使其带正确的类型与范围提示。
    ///
    /// 严格边界：
    /// - 不碰 CEF，不初始化 CEF，不创建节点，不注册 autoload，不导出任何 [Export] 属性。
    /// - 不调用 ProjectSettings.Save()，不删除任何 key。
    /// - 未启用本插件时，CefGlueControl 的行为与之前完全一致
    ///   （plugin.cfg 存在 ≠ 必须启用；Godot 只在启用了才会加载它）。
    /// - 导出构建中因未定义 TOOLS（Godot.NET.Sdk 仅在 Configuration=Debug 时定义）
    ///   而整段被剔除，不会有编辑器代码进入发布包。
    /// </summary>
    [Tool]
    public partial class GDCefGlueSettingsPlugin : EditorPlugin
    {
        public override void _EnterTree()
        {
            try
            {
                // custom_prop_info 是内存态，每次编辑器会话都要重新注册。
                CefInitializer.RegisterProjectSettings();
            }
            catch (System.Exception ex)
            {
                // 注册失败不应让插件加载失败。
                GD.PushWarning($"[GDCefGlue] 注册项目设置失败: {ex.Message}");
            }
        }

        // 不实现 _ExitTree：
        // 已注册的 key 是用户可能在编辑的值，退出时删除会毁掉它。
    }
}
#endif

using Godot;
using Godot.Bridge;

namespace GDCefGlueExtension;

// ── Inspector 属性动态显隐 ──────────────────────────────────
// 注意: 此 fork 的 Godot.Bridge.PropertyInfo.Usage 是 init-only，
// 不能在 _ValidateProperty 内修改。属性显隐通过 BindMembers 中的
// PropertyUsageFlags 初始值控制，动态切换需后续适配。
public partial class CefGlueControl
{
    // TODO: 当 Godot.Bridge.PropertyInfo.Usage 支持运行时修改后，
    // 恢复 _ValidateProperty 实现（参考 plugin 版本）
}
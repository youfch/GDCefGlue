using Godot;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Inspector 属性可见性控制
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        /// <summary>
        /// 根据 Mode 控制 Inspector 属性显隐：
        /// - EmbeddedWindow: 显示 ForwardInputEvents，隐藏 SyncCursor
        /// - OSR: 显示 SyncCursor，隐藏 ForwardInputEvents
        /// </summary>
        public override void _ValidateProperty(Godot.Collections.Dictionary property)
        {
            var propName = property["name"].AsStringName();

            if (propName == nameof(SyncCursor))
            {
                if (_mode == RenderMode.EmbeddedWindow)
                    property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }
            else if (propName == "Embedded Mode" || propName == nameof(ForwardInputEvents))
            {
                if (_mode != RenderMode.EmbeddedWindow)
                    property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }
        }
    }
}
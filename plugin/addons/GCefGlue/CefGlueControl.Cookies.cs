using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Cookie 管理（OSR + EmbeddedWindow 通用）
    //  ──────────────────────────────────────────────────────────────
    //  线程安全模型:
    //    CefCookieManager 方法可在任意线程调用（同步返回 bool）
    //    Visitor/Callback 回调在 CEF UI 线程触发
    //    CEF UI 线程 ≠ Godot 主线程 → 回调中使用 TaskCompletionSource
    //    (RunContinuationsAsynchronously) 异步完成 Task
    //    Godot 信号通过 CallDeferred marshal 到主线程
    //
    //  API 设计:
    //    C# 用户: GetCookiesAsync() → Task<List<CookieInfo>>
    //    GDScript/事件: CookiesVisited 事件 (CallDeferred 到主线程)
    //    CookieInfo 是 CefCookie 的安全快照 DTO，可跨线程持有
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        // ══════════════════════════════════════════════════════════════
        //  DTO: CookieInfo — CefCookie 的安全快照
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cookie 数据快照。可安全跨线程传递和长期持有。
        /// </summary>
        public sealed class CookieInfo
        {
            /// <summary>Cookie 名称</summary>
            public string Name { get; init; }
            /// <summary>Cookie 值</summary>
            public string Value { get; init; }
            /// <summary>域。空=host cookie；前导"."=domain cookie</summary>
            public string Domain { get; init; }
            /// <summary>路径限制</summary>
            public string Path { get; init; }
            /// <summary>仅 HTTPS</summary>
            public bool Secure { get; init; }
            /// <summary>仅 HTTP 请求（JS 不可读）</summary>
            public bool HttpOnly { get; init; }
            /// <summary>创建时间 (UTC)</summary>
            public DateTime Creation { get; init; }
            /// <summary>最后访问时间 (UTC)</summary>
            public DateTime LastAccess { get; init; }
            /// <summary>过期时间。null=会话 Cookie</summary>
            public DateTime? Expires { get; init; }
            /// <summary>SameSite 策略</summary>
            public CefCookieSameSite SameSite { get; init; }
            /// <summary>优先级</summary>
            public CefCookiePriority Priority { get; init; }

            internal CookieInfo(CefCookie c)
            {
                Name = c.Name ?? string.Empty;
                Value = c.Value ?? string.Empty;
                Domain = c.Domain ?? string.Empty;
                Path = c.Path ?? string.Empty;
                Secure = c.Secure;
                HttpOnly = c.HttpOnly;
                Creation = CefBaseTimeToUtc(c.Creation);
                LastAccess = CefBaseTimeToUtc(c.LastAccess);
                Expires = c.Expires.HasValue ? CefBaseTimeToUtc(c.Expires.Value) : null;
                SameSite = c.SameSite;
                Priority = c.Priority;
            }

            /// <summary>
            /// CefBaseTime (microseconds since 1601-01-01 UTC) → DateTime (UTC)
            /// </summary>
            private static DateTime CefBaseTimeToUtc(CefBaseTime t)
            {
                if (t.Ticks == 0) return DateTime.MinValue;
                try
                {
                    return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddTicks(t.Ticks * 10); // µs → 100-ns ticks (×10)
                }
                catch
                {
                    return DateTime.MinValue;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  内部回调实现
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// CefCookieVisitor 实现：收集所有 cookie 到 List，通过 TaskCompletionSource 异步完成。
        /// CefCookieVisitor 无 OnComplete 回调，通过 count == total-1 或 0 cookie 检测完成。
        /// </summary>
        private sealed class GodotCookieVisitor : CefCookieVisitor
        {
            private readonly TaskCompletionSource<List<CookieInfo>> _tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<CookieInfo> _cookies = new();
            private int _tcsSet; // 0=未完成, 1=已完成 (Interlocked 防重复)

            public Task<List<CookieInfo>> Task => _tcs.Task;

            protected override bool Visit(CefCookie cookie, int count, int total, out bool delete)
            {
                delete = false;
                _cookies.Add(new CookieInfo(cookie));

                // 最后一个 cookie → 完成 Task
                if (count == total - 1)
                    SignalComplete();

                return true; // 继续遍历
            }

            /// <summary>从外部调用以处理 "0 cookie" 场景（Visit 永不被调用时）</summary>
            internal void SignalComplete()
            {
                if (System.Threading.Interlocked.Exchange(ref _tcsSet, 1) == 0)
                    _tcs.TrySetResult(_cookies);
            }
        }

        private sealed class GodotSetCookieCallback : CefSetCookieCallback
        {
            private readonly TaskCompletionSource<bool> _tcs;
            public GodotSetCookieCallback(TaskCompletionSource<bool> tcs) => _tcs = tcs;
            protected override void OnComplete(bool success) => _tcs.TrySetResult(success);
        }

        private sealed class GodotDeleteCookiesCallback : CefDeleteCookiesCallback
        {
            private readonly TaskCompletionSource<int> _tcs;
            public GodotDeleteCookiesCallback(TaskCompletionSource<int> tcs) => _tcs = tcs;
            protected override void OnComplete(int numDeleted) => _tcs.TrySetResult(numDeleted);
        }

        private sealed class GodotCompletionCallback : CefCompletionCallback
        {
            private readonly TaskCompletionSource<bool> _tcs;
            public GodotCompletionCallback(TaskCompletionSource<bool> tcs) => _tcs = tcs;
            protected override void OnComplete() => _tcs.TrySetResult(true);
        }

        // ══════════════════════════════════════════════════════════════
        //  公开 API — Task-based (C# 异步)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取所有 Cookie。在 CEF UI 线程异步遍历，完成后 Task 完成。
        /// </summary>
        /// <returns>Cookie 快照列表；无 cookie 或不可访问时返回空列表。</returns>
        public Task<List<CookieInfo>> GetCookiesAsync()
        {
            var manager = GetCookieManagerOrNull();
            if (manager == null)
                return Task.FromResult(new List<CookieInfo>());

            var visitor = new GodotCookieVisitor();
            bool started = manager.VisitAllCookies(visitor);

            if (!started)
            {
                // Cookie 不可访问（如无存储）
                return Task.FromResult(new List<CookieInfo>());
            }

            // CefCookieVisitor 无 OnComplete；若 0 cookie 则 Visit 永不被调用。
            // 延迟检查：若 Task 未在短时间后完成，手动 SignalComplete。
            Task.Delay(500).ContinueWith(_ => visitor.SignalComplete(),
                TaskScheduler.Default);

            // 同时在 Visit 最后一个 cookie 时也会 SignalComplete
            // Interlocked.Exchange 保证只完成一次
            return visitor.Task;
        }

        /// <summary>
        /// 获取指定 URL 的 Cookie。
        /// </summary>
        /// <param name="url">目标 URL</param>
        /// <param name="includeHttpOnly">是否包含 HttpOnly Cookie</param>
        public Task<List<CookieInfo>> GetCookiesForUrlAsync(string url, bool includeHttpOnly = false)
        {
            var manager = GetCookieManagerOrNull();
            if (manager == null)
                return Task.FromResult(new List<CookieInfo>());

            var visitor = new GodotCookieVisitor();
            bool started = manager.VisitUrlCookies(url, includeHttpOnly, visitor);

            if (!started)
                return Task.FromResult(new List<CookieInfo>());

            Task.Delay(500).ContinueWith(_ => visitor.SignalComplete(),
                TaskScheduler.Default);

            return visitor.Task;
        }

        /// <summary>
        /// 设置 Cookie。完成后 Task 返回是否成功。
        /// </summary>
        public Task<bool> SetCookieAsync(string url, CookieInfo cookie)
        {
            var manager = GetCookieManagerOrNull();
            if (manager == null) return Task.FromResult(false);

            var cefCookie = new CefCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
                SameSite = cookie.SameSite,
                Priority = cookie.Priority,
            };

            if (cookie.Expires.HasValue)
                cefCookie.Expires = UtcToCefBaseTime(cookie.Expires.Value);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new GodotSetCookieCallback(tcs);
            bool started = manager.SetCookie(url, cefCookie, callback);

            if (!started)
            {
                return Task.FromResult(false);
            }

            return tcs.Task;
        }

        /// <summary>
        /// 删除 Cookie。
        /// <para>url + name 同时指定：删除匹配 host+domain 的同名 cookie</para>
        /// <para>仅 url 指定：删除该 URL 的所有 host cookie（不含 domain cookie）</para>
        /// <para>url 为空/null：删除所有 host 和 domain 的全部 cookie</para>
        /// </summary>
        /// <returns>删除的 cookie 数量</returns>
        public Task<int> DeleteCookiesAsync(string url, string cookieName)
        {
            var manager = GetCookieManagerOrNull();
            if (manager == null) return Task.FromResult(0);

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new GodotDeleteCookiesCallback(tcs);
            bool started = manager.DeleteCookies(url, cookieName, callback);

            if (!started)
            {
                return Task.FromResult(0);
            }

            return tcs.Task;
        }

        /// <summary>
        /// 删除所有 Cookie。等价于 DeleteCookiesAsync(null, null)。
        /// </summary>
        public Task<int> DeleteAllCookiesAsync() => DeleteCookiesAsync(null, null);

        /// <summary>
        /// 将 Cookie 存储刷盘到磁盘。确保 cookie 持久化。
        /// </summary>
        public Task<bool> FlushCookieStoreAsync()
        {
            var manager = GetCookieManagerOrNull();
            if (manager == null) return Task.FromResult(false);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new GodotCompletionCallback(tcs);
            bool started = manager.FlushStore(callback);

            if (!started)
            {
                return Task.FromResult(false);
            }

            return tcs.Task;
        }

        // ══════════════════════════════════════════════════════════════
        //  事件驱动 API (GDScript 兼容 / CallDeferred 到 Godot 主线程)
        // ══════════════════════════════════════════════════════════════

        // 字段存储跨线程数据（与 ContextMenu 模式一致，避免 Variant 序列化）
        private List<CookieInfo> _pendingCookiesResult;
        private bool _pendingSetCookieResult;
        private int _pendingDeleteCookiesResult;

        /// <summary>
        /// 获取所有 Cookie，完成后通过 <see cref="CookiesVisited"/> 事件通知（主线程）。
        /// 适合无法 await 的 GDScript 调用方。
        /// </summary>
        public void GetCookies() => GetCookiesAsync().ContinueWith(t =>
        {
            _pendingCookiesResult = t.GetAwaiter().GetResult() ?? new List<CookieInfo>();
            CallDeferred(nameof(NotifyCookiesVisited));
        }, TaskScheduler.Default);

        /// <summary>
        /// 获取指定 URL 的 Cookie，完成后通过 <see cref="CookiesVisited"/> 事件通知（主线程）。
        /// </summary>
        public void GetCookiesForUrl(string url, bool includeHttpOnly = false)
            => GetCookiesForUrlAsync(url, includeHttpOnly).ContinueWith(t =>
            {
                _pendingCookiesResult = t.GetAwaiter().GetResult() ?? new List<CookieInfo>();
                CallDeferred(nameof(NotifyCookiesVisited));
            }, TaskScheduler.Default);

        /// <summary>
        /// 设置 Cookie，完成后通过 <see cref="SetCookieCompleted"/> 事件通知（主线程）。
        /// </summary>
        public void SetCookie(string url, CookieInfo cookie) =>
            SetCookieAsync(url, cookie).ContinueWith(t =>
            {
                _pendingSetCookieResult = t.GetAwaiter().GetResult();
                CallDeferred(nameof(NotifySetCookieCompleted));
            }, TaskScheduler.Default);

        /// <summary>
        /// 删除 Cookie，完成后通过 <see cref="DeleteCookiesCompleted"/> 事件通知（主线程）。
        /// </summary>
        public void DeleteCookies(string url, string cookieName) =>
            DeleteCookiesAsync(url, cookieName).ContinueWith(t =>
            {
                _pendingDeleteCookiesResult = t.GetAwaiter().GetResult();
                CallDeferred(nameof(NotifyDeleteCookiesCompleted));
            }, TaskScheduler.Default);

        // ── CallDeferred 目标方法（Godot 主线程执行） ──

        private void NotifyCookiesVisited()
        {
            var cookies = _pendingCookiesResult;
            _pendingCookiesResult = null;
            CookiesVisited?.Invoke(cookies);
        }

        private void NotifySetCookieCompleted()
        {
            SetCookieCompleted?.Invoke(_pendingSetCookieResult);
        }

        private void NotifyDeleteCookiesCompleted()
        {
            DeleteCookiesCompleted?.Invoke(_pendingDeleteCookiesResult);
        }

        // ══════════════════════════════════════════════════════════════
        //  内部辅助
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取当前浏览器的 CookieManager。可在任意线程调用。
        /// 路径: browser → host → request context → cookie manager
        /// </summary>
        private CefCookieManager GetCookieManagerOrNull()
        {
            try
            {
                return _browser?.GetHost()?.GetRequestContext()?.GetCookieManager(null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// DateTime (UTC) → CefBaseTime (microseconds since 1601-01-01)
        /// </summary>
        private static CefBaseTime UtcToCefBaseTime(DateTime utc)
        {
            if (utc == DateTime.MinValue) return default;
            var epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long microSeconds = (utc.Ticks - epoch.Ticks) / 10; // 100-ns ticks → µs
            return new CefBaseTime(microSeconds);
        }
    }
}

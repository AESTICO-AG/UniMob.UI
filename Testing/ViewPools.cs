using UniMob.UI.Internal.Pooling;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     The views UniMob keeps for reuse. The pool lives on <c>DontDestroyOnLoad</c>, so the views an
    ///     app recycled are still queued after its scenes are gone, and the next mount anywhere in the
    ///     play session is handed the oldest of them.
    /// </summary>
    public static class ViewPools
    {
        /// <summary>
        ///     Destroys every idle pooled view, so the next mount builds a fresh one. Views in use are
        ///     untouched and still return to their pool when recycled.
        /// </summary>
        public static void DestroyIdle() => GameObjectPool.DestroyIdle();
    }
}

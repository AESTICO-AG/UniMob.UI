using System.Collections.Generic;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    /// Keeps the on-screen boxes that have been asked for current. Unity does not signal a widget when
    /// it merely moves (e.g. an ancestor scrolls) without resizing, so at the end of every frame this
    /// re-measures each tracked box and stores the result, which invalidates the box's
    /// <c>GlobalGeometry</c> atom only when it changed: a box standing still queues nothing, and one
    /// that moved wakes its followers on the next frame. A box is tracked from the first read of its
    /// geometry until its state is disposed.
    /// </summary>
    internal static class WidgetGeometryTicker
    {
        private static readonly HashSet<ViewState> Tracked = new HashSet<ViewState>();

        // The boxes one tick measures, copied out first: storing a measurement can reach code that
        // disposes a state, and a disposed state leaves the set while it is being walked.
        private static readonly List<ViewState> Pass = new List<ViewState>();

        private static WidgetGeometryTickerDriver? _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Tracked.Clear();
            Pass.Clear();
        }

        /// <summary>
        /// Re-measures <paramref name="state"/> every frame until its state lifetime is disposed.
        /// </summary>
        public static void Track(ViewState state)
        {
            EnsureStarted();

            if (Tracked.Add(state))
            {
                state.StateLifetime.Register(() => Tracked.Remove(state));
            }
        }

        private static void EnsureStarted()
        {
            if (_driver != null)
            {
                return;
            }

            var go = new GameObject(nameof(WidgetGeometryTickerDriver))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<WidgetGeometryTickerDriver>();
        }

        internal static void Tick()
        {
            if (Tracked.Count == 0)
            {
                return;
            }

            foreach (var state in Tracked)
            {
                Pass.Add(state);
            }

            try
            {
                foreach (var state in Pass)
                {
                    if (!state.StateLifetime.IsDisposed)
                    {
                        state.RemeasureGlobalGeometry();
                    }
                }
            }
            finally
            {
                Pass.Clear();
            }
        }
    }

    internal sealed class WidgetGeometryTickerDriver : MonoBehaviour
    {
        private void LateUpdate() => WidgetGeometryTicker.Tick();
    }
}

using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.Assertions;

namespace UniMob.UI
{
    public abstract class ViewState : State, IViewState
    {
        private LifetimeController? _mountLifetimeController;

        private IView? _mountedView;
        private Canvas? _rootCanvas;
        private MutableAtom<WidgetGeometry>? _globalGeometry;

        public abstract WidgetViewReference View { get; }

        public sealed override IViewState InnerViewState => this;

        /// <inheritdoc/>
        public IView? MountedView => _mountedView;

        public Lifetime MountLifetime
        {
            get
            {
                if (_mountLifetimeController == null)
                {
                    _mountLifetimeController = new LifetimeController();
                }

                return _mountLifetimeController.Lifetime;
            }
        }

        public virtual void DidViewMount(IView view)
        {
            Assert.IsNull(Atom.CurrentScope);

            _mountedView = view;
            _rootCanvas = ResolveRootCanvas(view);
        }

        public virtual void DidViewUnmount(IView view)
        {
            Assert.IsNull(Atom.CurrentScope);

            _mountedView = null;
            _rootCanvas = null;

            _mountLifetimeController?.Dispose();
        }

        // Local geometry is render-tree truth, so it is read off RenderObject rather than mirrored
        // here. Any state can answer it without an interface.
        //
        // Reach for RenderObject.WatchedSize(), not RenderObject.PeekSize(): the peek is the raw
        // result of the last pass, so it is whatever was current when someone last laid this out,
        // and reading it subscribes to nothing. WatchedSize both runs the pass if it is due and
        // re-runs the caller when the answer changes, which is what anything outside a layout pass
        // actually wants.

        /// <summary>
        /// Reads this widget's current on-screen box in canvas space. Returns <c>false</c> (and
        /// <see cref="WidgetGeometry.Empty"/>) while the widget is not mounted to a view. Cheap and
        /// non-reactive -- suited to one-shot queries (e.g. on a tap).
        /// </summary>
        public bool TryGetGlobalGeometry(out WidgetGeometry geometry)
        {
            if (_mountedView == null || _mountedView.IsDestroyed)
            {
                geometry = WidgetGeometry.Empty;
                return false;
            }

            return WidgetGeometryUtility.TryCompute(
                _mountedView.rectTransform,
                _rootCanvas,
                out geometry
            );
        }

        /// <summary>
        /// <b>[Atom]</b> This widget's on-screen box in canvas space. From its first read until this
        /// state is disposed it is re-measured at the end of every frame and changes only when the box
        /// actually moved or resized, so followers track movement (scroll/animation) that Unity never
        /// signals, and a box standing still leaves nothing queued.
        /// <see cref="WidgetGeometry.Empty"/> while the widget is not mounted.
        /// </summary>
        public WidgetGeometry GlobalGeometry
        {
            get
            {
                if (_globalGeometry == null)
                {
                    _globalGeometry = Atom.Value(
                        StateLifetime,
                        MeasureGlobalGeometry(),
                        debugName: "ViewState.GlobalGeometry"
                    );
                    WidgetGeometryTicker.Track(this);
                }

                return _globalGeometry.Value;
            }
        }

        /// <summary>
        /// Measures the box again and stores it, which invalidates <see cref="GlobalGeometry"/> only
        /// when the measurement differs. Called by <see cref="WidgetGeometryTicker"/>.
        /// </summary>
        internal void RemeasureGlobalGeometry()
        {
            if (_globalGeometry != null)
            {
                _globalGeometry.Value = MeasureGlobalGeometry();
            }
        }

        private WidgetGeometry MeasureGlobalGeometry() =>
            TryGetGlobalGeometry(out var geometry) ? geometry : WidgetGeometry.Empty;

        private static Canvas? ResolveRootCanvas(IView view)
        {
            var rt = view?.rectTransform;
            if (rt == null)
            {
                return null;
            }

            var canvas = rt.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.rootCanvas : null;
        }
    }
}

using System.Linq;
using NUnit.Framework;
using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     That <see cref="RenderAnchoredBox"/> insists on a bounded frame.
    /// </summary>
    /// <remarks>
    ///     Its own size is the box "keep inside" refers to, so an unbounded axis does not merely make
    ///     it bigger: the clamp silently stops clamping, and the child is offered infinite room. The
    ///     failure then reads as a positioning bug wherever the child ends up, a long way from the
    ///     parent that forgot to give it a frame.
    ///     <para>
    ///         Reachable from ordinary code since a ZStack lays a positioned child out unbounded on
    ///         any axis it does not pin, so <c>Positioned { Left, Top }</c> around an anchored box
    ///         hands it exactly this. Every such site in the app today uses <c>Positioned.Fill</c>,
    ///         and these tests are what keeps the next one from being silent.
    ///     </para>
    ///     <para>
    ///         Asserted against a recording reporter rather than Unity's log. A silence assertion made
    ///         with <c>LogAssert.NoUnexpectedReceived</c> would go vacuous the moment reports stopped
    ///         reaching Unity's log -- passing just as happily while the box screamed into a recorder
    ///         nobody read -- and a test that stops testing without failing is worse than no test.
    ///     </para>
    /// </remarks>
    public class RenderAnchoredBoxTests
    {
        private class FakeAnchoredBoxState : FakeSingleChildLayoutState, IAnchoredBoxState
        {
            public WidgetGeometry AnchorGeometry { get; set; } = WidgetGeometry.Empty;
            public WidgetGeometry SelfGeometry { get; set; } = WidgetGeometry.Empty;
            public Alignment TargetAnchor { get; set; } = Alignment.BottomLeft;
            public Alignment ChildAnchor { get; set; } = Alignment.TopLeft;
            public Vector2 Offset { get; set; }
            public float? KeepInsidePadding { get; set; } = 36f;
            public bool MatchAnchorWidth { get; set; }
            public Axis? FlipToFit { get; set; }
        }

        private static RenderAnchoredBox Box() =>
            new RenderAnchoredBox(
                new FakeAnchoredBoxState
                {
                    Child = TestHarness.Mount(new FixedSizeBox { Size = new Vector2(30, 40) }),
                }
            );

        // Structural, not textual: the assertion is that the box refuses the axis and says how to
        // give it one, which survives the sentence being rewritten.
        [Test]
        public void UnboundedWidth_IsReported()
        {
            using var log = RecordingReporter.Capture();

            Box().Layout(new LayoutConstraints(0, 0, float.PositiveInfinity, 200));

            var issue = log.Single();
            Assert.AreEqual(LayoutIssueCode.UnboundedConstraint, issue.Code);
            Assert.AreEqual(LayoutAxes.Horizontal, issue.Axes);
            Assert.IsNotNull(issue.Remedy);
        }

        [Test]
        public void UnboundedHeight_IsReported()
        {
            using var log = RecordingReporter.Capture();

            Box().Layout(new LayoutConstraints(0, 0, 200, float.PositiveInfinity));

            Assert.AreEqual(LayoutAxes.Vertical, log.Single().Axes);
        }

        // Axis, not Axes: the enum the rest of layout uses cannot say "both", which is exactly what
        // this case is.
        [Test]
        public void FullyUnboundedConstraints_AreReportedOnce_NamingBothAxes()
        {
            using var log = RecordingReporter.Capture();

            Box().Layout(LayoutConstraints.Unbounded());

            Assert.AreEqual(LayoutAxes.Both, log.Single().Axes);
        }

        /// <summary>
        ///     The shape every anchored box in the app is actually built with. It must stay silent,
        ///     or the diagnostic is noise and will be muted.
        /// </summary>
        [Test]
        public void ATightFrame_IsNotReported()
        {
            using var log = RecordingReporter.Capture();
            var box = Box();

            box.Layout(LayoutConstraints.Tight(400, 300));

            Assert.AreEqual(new Vector2(400, 300), box.PeekSize());
            Assert.IsEmpty(log);
        }

        [Test]
        public void ALooseButBoundedFrame_IsNotReported()
        {
            using var log = RecordingReporter.Capture();
            var box = Box();

            box.Layout(LayoutConstraints.Loose(400, 300));

            Assert.AreEqual(
                new Vector2(400, 300),
                box.PeekSize(),
                "the box fills whatever bounded room it is given: that room is the keep-inside frame."
            );
            Assert.IsEmpty(log);
        }

        // == The room beside the anchor ============================================================
        //
        // A child that opens beside its anchor is capped at the room on the roomier side of it, not at
        // the whole frame. Capped at the frame, a child too wide for either side is slid back inside
        // over the very anchor it points at; capped at a side, content that wraps fits there.
        //
        // Geometry is canvas space, y up from the bottom-left, as WidgetGeometry carries it.

        private const float Padding = 36f;
        private const float Gap = 36f;

        private static WidgetGeometry Geometry(float xMin, float yMin, float xMax, float yMax)
        {
            var quad = new Quad(
                new Vector2(xMin, yMin),
                new Vector2(xMin, yMax),
                new Vector2(xMax, yMax),
                new Vector2(xMax, yMin)
            );
            return new WidgetGeometry(quad, quad);
        }

        /// <summary>
        ///     Lays an anchored box out over a frame at the origin with a child that would take any
        ///     room it is given, and hands back the child's render object to read what it was offered.
        /// </summary>
        private static (RenderAnchoredBox box, RenderCountingBox child) LayOut(
            Vector2 frame,
            WidgetGeometry anchor,
            Alignment target,
            Alignment childAnchor,
            Vector2 offset,
            Axis? flipToFit,
            bool matchAnchorWidth = false
        )
        {
            var childState = TestHarness.Mount(
                new CountingBox { BoxSize = new Vector2(100000, 100000) }
            );
            var box = new RenderAnchoredBox(
                new FakeAnchoredBoxState
                {
                    Child = childState,
                    AnchorGeometry = anchor,
                    SelfGeometry = Geometry(0, 0, frame.x, frame.y),
                    TargetAnchor = target,
                    ChildAnchor = childAnchor,
                    Offset = offset,
                    KeepInsidePadding = Padding,
                    FlipToFit = flipToFit,
                    MatchAnchorWidth = matchAnchorWidth,
                }
            );

            box.Layout(LayoutConstraints.Tight(frame.x, frame.y));

            return (box, (RenderCountingBox)childState.InnerViewState.RenderObject);
        }

        /// <summary>
        ///     The case that covered its anchor: an info icon near the right edge of a portrait
        ///     screen, its callout opening west.
        /// </summary>
        [Test]
        public void OpeningWest_FromAnAnchorNearTheRightEdge_TheChildGetsTheRoomOnTheWest()
        {
            var (box, child) = LayOut(
                frame: new Vector2(1460, 2440),
                anchor: Geometry(1300, 1700, 1360, 1760),
                target: Alignment.CenterLeft,
                childAnchor: Alignment.CenterRight,
                offset: new Vector2(-Gap, 0),
                flipToFit: Axis.Horizontal
            );

            Assert.AreEqual(1300 - Gap - Padding, child.LastConstraints.MaxWidth);
            Assert.LessOrEqual(
                box.ChildPosition.x + box.ChildSize.x,
                1300 - Gap,
                "a child that fits the west side should end before the anchor, not over it."
            );
        }

        [Test]
        public void TheOppositeSideIsRoomier_TheChildGetsThatRoom_AndFlipsThere()
        {
            var (box, child) = LayOut(
                frame: new Vector2(1460, 2440),
                anchor: Geometry(100, 1700, 160, 1760),
                target: Alignment.CenterLeft,
                childAnchor: Alignment.CenterRight,
                offset: new Vector2(-Gap, 0),
                flipToFit: Axis.Horizontal
            );

            Assert.AreEqual(1460 - 160 - Gap - Padding, child.LastConstraints.MaxWidth);
            Assert.GreaterOrEqual(
                box.ChildPosition.x,
                160 + Gap,
                "the child should flip east, where the room it was sized for is."
            );
        }

        /// <summary>
        ///     A menu opening below its field cannot wrap its height to fit, and a column of entries
        ///     capped at the room below would lose its last ones. It keeps the frame.
        /// </summary>
        [Test]
        public void OpeningSouth_TheChildKeepsTheFrame()
        {
            // Near the top of the frame: canvas y is up, so the anchor sits high.
            var (_, child) = LayOut(
                frame: new Vector2(1000, 2000),
                anchor: Geometry(400, 1800, 600, 1860),
                target: Alignment.BottomCenter,
                childAnchor: Alignment.TopCenter,
                offset: new Vector2(0, Gap),
                flipToFit: Axis.Vertical
            );

            Assert.AreEqual(2000 - 2 * Padding, child.LastConstraints.MaxHeight);
            Assert.AreEqual(1000 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        /// <summary>
        ///     The threshold compares like with like: the room left after offset and padding against
        ///     half the frame's usable width. Against half the whole frame, this anchor would fall back
        ///     to the frame and be covered.
        /// </summary>
        [Test]
        public void AnAnchorJustOffCentre_StillCapsAtItsWiderSide()
        {
            var (_, child) = LayOut(
                frame: new Vector2(1460, 2440),
                anchor: Geometry(780, 1700, 840, 1760),
                target: Alignment.CenterLeft,
                childAnchor: Alignment.CenterRight,
                offset: new Vector2(-Gap, 0),
                flipToFit: Axis.Horizontal
            );

            Assert.AreEqual(780 - Gap - Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void MatchingTheAnchorsWidth_IsNotUndercutByTheSideRoom()
        {
            var (_, child) = LayOut(
                frame: new Vector2(1460, 2440),
                anchor: Geometry(100, 1700, 1300, 1760),
                target: Alignment.CenterLeft,
                childAnchor: Alignment.CenterRight,
                offset: new Vector2(-Gap, 0),
                flipToFit: Axis.Horizontal,
                matchAnchorWidth: true
            );

            Assert.AreEqual(1200, child.LastConstraints.MinWidth);
            Assert.AreEqual(1200, child.LastConstraints.MaxWidth);
        }

        /// <summary>
        ///     Both sides thin: the child would be squeezed into a sliver, so it keeps the frame and may
        ///     overlap the anchor instead.
        /// </summary>
        [Test]
        public void BothSidesUnderHalfTheFrame_TheChildKeepsTheFrame()
        {
            var (_, child) = LayOut(
                frame: new Vector2(1000, 800),
                anchor: Geometry(480, 380, 520, 420),
                target: Alignment.CenterLeft,
                childAnchor: Alignment.CenterRight,
                offset: new Vector2(-Gap, 0),
                flipToFit: Axis.Horizontal
            );

            Assert.AreEqual(1000 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void WithoutAFlipAxis_TheChildKeepsTheFrame()
        {
            var (_, child) = LayOut(
                frame: new Vector2(1460, 2440),
                anchor: Geometry(1300, 1700, 1360, 1760),
                target: Alignment.CenterLeft,
                childAnchor: Alignment.CenterRight,
                offset: new Vector2(-Gap, 0),
                flipToFit: null
            );

            Assert.AreEqual(1460 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void WithoutKeepInside_TheChildIsStillUnbounded()
        {
            var childState = TestHarness.Mount(new CountingBox { BoxSize = new Vector2(30, 40) });
            var box = new RenderAnchoredBox(
                new FakeAnchoredBoxState
                {
                    Child = childState,
                    AnchorGeometry = Geometry(1300, 1700, 1360, 1760),
                    SelfGeometry = Geometry(0, 0, 1460, 2440),
                    TargetAnchor = Alignment.CenterLeft,
                    ChildAnchor = Alignment.CenterRight,
                    KeepInsidePadding = null,
                    FlipToFit = Axis.Horizontal,
                }
            );

            box.Layout(LayoutConstraints.Tight(1460, 2440));

            Assert.AreEqual(
                float.PositiveInfinity,
                ((RenderCountingBox)childState.InnerViewState.RenderObject).LastConstraints.MaxWidth
            );
        }
    }
}

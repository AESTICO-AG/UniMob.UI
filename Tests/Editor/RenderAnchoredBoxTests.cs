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
    ///     What <see cref="RenderAnchoredBox"/> offers its child: it insists on a bounded frame, and a
    ///     child opening east or west gets no more than the room on the wider side of its anchor.
    /// </summary>
    /// <remarks>
    ///     On the frame: its own size is the box "keep inside" refers to, so an unbounded axis does
    ///     not merely make it bigger: the clamp silently stops clamping, and the child is offered
    ///     infinite room. The failure then reads as a positioning bug wherever the child ends up, a
    ///     long way from the parent that forgot to give it a frame.
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
        // A child opening east or west is offered at most the room on the wider side of its anchor.
        // Geometry is canvas space, y up from the bottom-left. The gap and the padding differ so that
        // a test can tell which one was subtracted.

        private const float Padding = 36f;
        private const float Gap = 24f;

        private static readonly Vector2 PortraitFrame = new Vector2(1460, 2440);

        private static WidgetGeometry Geometry(float xMin, float yMin, float xMax, float yMax) =>
            WidgetGeometryTests.FromRect(Rect.MinMaxRect(xMin, yMin, xMax, yMax));

        /// <summary>An anchor near the right edge of <see cref="PortraitFrame"/>.</summary>
        private static readonly WidgetGeometry NearTheRightEdge = Geometry(1300, 1700, 1360, 1760);

        /// <summary>
        ///     Lays an anchored box out over a frame at the origin, with a child that takes any room it
        ///     is offered, and hands back the child's render object to read what it was offered.
        /// </summary>
        private static (RenderAnchoredBox box, RenderCountingBox child) LayOut(
            Vector2 frame,
            WidgetGeometry anchor,
            Alignment target,
            Alignment childAnchor,
            Vector2 offset,
            Axis? flipToFit,
            float? padding = Padding,
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
                    KeepInsidePadding = padding,
                    FlipToFit = flipToFit,
                    MatchAnchorWidth = matchAnchorWidth,
                }
            );

            box.Layout(LayoutConstraints.Tight(frame.x, frame.y));

            return (box, (RenderCountingBox)childState.InnerViewState.RenderObject);
        }

        /// <summary>Opening west, as a callout does from an info icon.</summary>
        private static (RenderAnchoredBox box, RenderCountingBox child) LayOutWest(
            WidgetGeometry anchor,
            Vector2? frame = null,
            Vector2? offset = null,
            Axis? flipToFit = Axis.Horizontal,
            float? padding = Padding,
            bool matchAnchorWidth = false
        ) =>
            LayOut(
                frame ?? PortraitFrame,
                anchor,
                Alignment.CenterLeft,
                Alignment.CenterRight,
                offset ?? new Vector2(-Gap, 0),
                flipToFit,
                padding,
                matchAnchorWidth
            );

        [Test]
        public void OpeningWest_NearTheRightEdge_TheChildGetsTheRoomOnTheWest_AndEndsBeforeTheAnchor()
        {
            var (box, child) = LayOutWest(NearTheRightEdge);

            Assert.AreEqual(1300 - Gap - Padding, child.LastConstraints.MaxWidth);
            Assert.LessOrEqual(box.ChildPosition.x + box.ChildSize.x, 1300 - Gap);
        }

        [Test]
        public void TheOppositeSideIsWider_TheChildGetsThatRoom_AndFlipsThere()
        {
            var (box, child) = LayOutWest(Geometry(100, 1700, 160, 1760));

            Assert.AreEqual(1460 - 160 - Gap - Padding, child.LastConstraints.MaxWidth);
            Assert.GreaterOrEqual(box.ChildPosition.x, 160 + Gap);
        }

        [Test]
        public void AnAnchorJustOffCentre_IsCappedAtItsWiderSide()
        {
            // West: 780 - 60 = 720, over half the usable width, (1460 - 72) / 2 = 694.
            var (_, child) = LayOutWest(Geometry(780, 1700, 840, 1760));

            Assert.AreEqual(780 - Gap - Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void BothSidesUnderHalfTheUsableWidth_TheChildKeepsTheFrame()
        {
            // Each side: 480 - 60 = 420, under (1000 - 72) / 2 = 464.
            var (_, child) = LayOutWest(
                Geometry(480, 380, 520, 420),
                frame: new Vector2(1000, 800)
            );

            Assert.AreEqual(1000 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void MatchingTheAnchorsWidth_IsNotUndercutByTheSideRoom()
        {
            // East: 1460 - 720 = 740, less the padding = 704, over half the usable width, 694.
            var (_, child) = LayOutWest(
                Geometry(0, 1700, 720, 1760),
                offset: Vector2.zero,
                matchAnchorWidth: true
            );

            Assert.AreEqual(720, child.LastConstraints.MinWidth);
            Assert.AreEqual(720, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void AVerticalFlipAxis_KeepsTheFrameWidth()
        {
            var (_, child) = LayOutWest(NearTheRightEdge, flipToFit: Axis.Vertical);

            Assert.AreEqual(1460 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void NoFlipAxis_KeepsTheFrameWidth()
        {
            var (_, child) = LayOutWest(NearTheRightEdge, flipToFit: null);

            Assert.AreEqual(1460 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void ACentredTargetPoint_KeepsTheFrameWidth()
        {
            var (_, child) = LayOut(
                PortraitFrame,
                NearTheRightEdge,
                Alignment.BottomCenter,
                Alignment.TopCenter,
                new Vector2(0, Gap),
                Axis.Horizontal
            );

            Assert.AreEqual(1460 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void AChildAlignedToTheAnchorsEdge_KeepsTheFrameWidth()
        {
            // The widget's default placement: the child's top-left at the anchor's bottom-left.
            var (_, child) = LayOut(
                new Vector2(1000, 800),
                Geometry(0, 380, 100, 420),
                Alignment.BottomLeft,
                Alignment.TopLeft,
                Vector2.zero,
                Axis.Horizontal
            );

            Assert.AreEqual(1000 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void AnUnmeasuredAnchor_KeepsTheFrameWidth()
        {
            // An empty anchor reads as a box at the origin, whose east side less this offset and the
            // padding (1224) would undercut the frame (1388) if it were used.
            var (_, child) = LayOutWest(WidgetGeometry.Empty, offset: new Vector2(-200, 0));

            Assert.AreEqual(1460 - 2 * Padding, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void AFrameNarrowerThanTwiceThePadding_OffersNoNegativeWidth()
        {
            // West: 25 - 36 = -11, east: 40 - 25 - 36 = -21. The wider side, -11, clears half the
            // usable width, (40 - 72) / 2 = -16, so only the room guard keeps it out.
            var (_, child) = LayOutWest(
                Geometry(25, 380, 25, 420),
                frame: new Vector2(40, 800),
                offset: Vector2.zero
            );

            Assert.AreEqual(0f, child.LastConstraints.MaxWidth);
        }

        [Test]
        public void WithoutKeepInside_TheChildIsUnbounded()
        {
            var (_, child) = LayOutWest(NearTheRightEdge, padding: null);

            Assert.AreEqual(float.PositiveInfinity, child.LastConstraints.MaxWidth);
        }
    }
}

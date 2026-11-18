using UniMob.UI;
using UniMob.UI.Diagnostics;
using UniMob.UI.Rendering;
using UnityEngine;

namespace UniMob.UI.Rendering
{
    /// <summary>
    /// Places a single child at a point derived from another widget's box, and keeps it inside its own
    /// bounds.
    /// </summary>
    /// <remarks>
    /// A render object rather than a composition of positioning primitives because every part of the
    /// job needs the child's MEASURED size: meeting the anchor point with the child's own anchor,
    /// choosing a side it fits on, and sliding it back inside when the anchor sits near an edge. A
    /// general-purpose positioner cannot own any of that -- the position is computed, so only whatever
    /// computed it knows whether the result may land off-screen. Here one widget owns one policy, at
    /// the one moment the sizes exist.
    /// </remarks>
    public class RenderAnchoredBox : SingleChildRenderObject
    {
        private readonly IAnchoredBoxState state;

        /// <summary>Anchoring a box to something else is placing it outside this one by definition.</summary>
        protected override bool ChildrenMayOverhang => true;

        public RenderAnchoredBox(IAnchoredBoxState state)
            : base(state)
        {
            this.state = state;
        }

        protected override Vector2 PerformSizing(LayoutConstraints constraints)
        {
            // Reported rather than repaired, because every repair is a lie: this widget's frame IS
            // the policy, so inventing one would place the child against a box the caller never
            // chose and keep-inside would enforce a boundary that is not on screen. An unbounded
            // axis makes `self` infinite below, which silently turns KeepInside's clamp into a
            // no-op and hands the child unbounded constraints of its own. Both look like a
            // positioning bug at the call site rather than a missing frame here.
            var unbounded = LayoutAxes.None;

            if (!constraints.HasBoundedWidth)
            {
                unbounded |= LayoutAxes.Horizontal;
            }

            if (!constraints.HasBoundedHeight)
            {
                unbounded |= LayoutAxes.Vertical;
            }

            if (unbounded != LayoutAxes.None)
            {
                ReportUnboundedConstraint(unbounded, constraints, GiveItAFrame);
            }

            // Fill what we are given: this widget IS the space the child is placed within, so its own
            // size is the frame that "keep inside" refers to.
            var self = constraints.Constrain(
                new Vector2(float.PositiveInfinity, float.PositiveInfinity)
            );

            if (this.Child == null)
            {
                this.ChildSize = Vector2.zero;
                return self;
            }

            // Always the real constraints, even before the anchor is known: squeezing the child to hide
            // it would make every flex row inside it report an overflow. The state substitutes a
            // placeholder child while there is nowhere to put it, so there is nothing to hide here.
            // Sizing reads the anchor's width for MatchAnchorWidth, and its position for a child
            // opening east or west, whose width is capped at the room beside it (RoomBesideAnchor).
            this.ChildSize = this.LayoutChild(this.Child, this.ChildConstraints(self));
            return self;
        }

        protected override void PerformPositioning(Vector2 size)
        {
            if (this.Child == null || !this.TryGetGeometry(out var anchor, out var origin))
            {
                this.ChildPosition = Vector2.zero;
                return;
            }

            var flipped = this.ShouldFlip(anchor);
            var targetAnchor = Mirror(this.state.TargetAnchor, this.state.FlipToFit, flipped);
            var childAnchor = Mirror(this.state.ChildAnchor, this.state.FlipToFit, flipped);
            var offset = Mirror(this.state.Offset, this.state.FlipToFit, flipped);

            var target = anchor.GetPoint(targetAnchor);

            // Canvas space is y-UP from the bottom-left; a layout position is y-DOWN from the top
            // edge. The x term subtracts, the y term reverses -- that asymmetry is the whole
            // conversion, and getting it backwards is the classic bug here.
            var position = new Vector2(
                target.x - origin.x + offset.x,
                origin.y - target.y + offset.y
            );

            // The point marks where the child's OWN anchor goes, not its top-left, so back off by
            // however far that anchor sits into the child. ResolveOffset (not ToAnchor) because this
            // is layout space: Alignment carries two mappers and they disagree on Y.
            position -= childAnchor.ResolveOffset(this.ChildSize, Vector2.zero);

            if (this.state.KeepInsidePadding is { } padding)
            {
                position = new Vector2(
                    KeepInside(position.x, this.ChildSize.x, size.x, padding),
                    KeepInside(position.y, this.ChildSize.y, size.y, padding)
                );
            }

            this.ChildPosition = position;
        }

        /// <summary>
        /// Whether the preferred side has to give way, because the child does not fit there and does
        /// fit opposite.
        /// </summary>
        /// <remarks>
        /// Both conditions matter. A screen with room for neither keeps the caller's side and lets
        /// the clamp slide the child inside: being cramped should not also make the direction
        /// unpredictable, and flipping would trade one overlap for an identical one.
        /// </remarks>
        private bool ShouldFlip(WidgetGeometry anchor)
        {
            if (this.state.FlipToFit is not { } axis)
                return false;

            // The alignment component that names the side. A centred anchor point names none, so
            // there is nothing to flip it away from.
            var side =
                axis == Axis.Horizontal ? this.state.TargetAnchor.X : this.state.TargetAnchor.Y;
            if (side == 0f)
                return false;

            var (towardsPositive, towardsNegative) = this.RoomOnEachSide(anchor, axis);
            var preferred = side > 0f ? towardsPositive : towardsNegative;
            var opposite = side > 0f ? towardsNegative : towardsPositive;
            var size = axis == Axis.Horizontal ? this.ChildSize.x : this.ChildSize.y;

            return preferred < size && opposite >= size;
        }

        /// <summary>
        /// The room a child has on each side of the anchor along <paramref name="axis"/>, within our
        /// own frame, less the offset and the keep-inside padding. Both the flip and the side cap read
        /// it, so a child sized to the cap is exactly what the flip counts as fitting.
        /// </summary>
        private (float towardsPositive, float towardsNegative) RoomOnEachSide(
            WidgetGeometry anchor,
            Axis axis
        )
        {
            var self = this.state.SelfGeometry.Aabb;
            var box = anchor.Aabb;
            var padding = this.state.KeepInsidePadding ?? 0f;

            if (axis == Axis.Horizontal)
            {
                // Alignment +X is east and canvas +x is east: the two agree, nothing reverses.
                var spent = Mathf.Abs(this.state.Offset.x) + padding;
                return (self.xMax - box.xMax - spent, box.xMin - self.xMin - spent);
            }
            else
            {
                // Alignment +Y is SOUTH while canvas +y points north, so the two room terms swap.
                // This is the disagreement between Alignment's two mappers, and it is why the
                // vertical half of the flip preview is the half worth reading.
                var spent = Mathf.Abs(this.state.Offset.y) + padding;
                return (box.yMin - self.yMin - spent, self.yMax - box.yMax - spent);
            }
        }

        private static Alignment Mirror(Alignment alignment, Axis? axis, bool flipped) =>
            !flipped ? alignment
            : axis == Axis.Horizontal ? new Alignment(-alignment.X, alignment.Y)
            : new Alignment(alignment.X, -alignment.Y);

        private static Vector2 Mirror(Vector2 offset, Axis? axis, bool flipped) =>
            !flipped ? offset
            : axis == Axis.Horizontal ? new Vector2(-offset.x, offset.y)
            : new Vector2(offset.x, -offset.y);

        /// <summary>
        /// The room on the wider side of an anchor the child opens east or west of, from
        /// <see cref="RoomOnEachSide"/>, or null to leave the frame as the bound.
        /// </summary>
        /// <remarks>
        /// The child opens east or west when
        /// <see cref="UniMob.UI.Widgets.AnchoredBox.FlipToFit"/> is horizontal, the target point is
        /// on a vertical edge of the anchor and the child's own point is on its opposite edge. Null
        /// when it does not, when <c>MatchAnchorWidth</c> sets the width, when the geometry is not
        /// measured yet, and when the wider side has no room at all or is under half the frame's
        /// usable width.
        /// </remarks>
        private float? RoomBesideAnchor(float padding)
        {
            var target = this.state.TargetAnchor.X;
            var opensBeside =
                this.state.FlipToFit == Axis.Horizontal
                && target != 0f
                && this.state.ChildAnchor.X == -target;

            if (!opensBeside || this.state.MatchAnchorWidth)
                return null;

            if (!this.TryGetGeometry(out var anchor, out _))
                return null;

            var (east, west) = this.RoomOnEachSide(anchor, Axis.Horizontal);
            var room = Mathf.Max(east, west);
            var usable = this.state.SelfGeometry.Aabb.width - (padding * 2f);

            // In a frame narrower than twice the padding both sides are negative, and so is half the
            // usable width, which would let a negative bound through.
            return room <= 0f || room < usable / 2f ? null : room;
        }

        private LayoutConstraints ChildConstraints(Vector2 self)
        {
            var width = this.state.MatchAnchorWidth
                ? (float?)this.state.AnchorGeometry.Size.x
                : null;

            if (this.state.KeepInsidePadding is not { } padding)
            {
                // Free to be any size, so the child hugs its content and may overflow -- the caller
                // asked for that by switching keep-inside off.
                return new LayoutConstraints(
                    width ?? 0f,
                    0f,
                    width ?? float.PositiveInfinity,
                    float.PositiveInfinity
                );
            }

            // Cap at what we can actually show, so that keeping the child inside is always possible.
            // A child bigger than its frame has nowhere legal to sit and would be parked against one
            // edge, overflowing the far one.
            //
            // This is a bound, not a size: content that sizes itself is untouched. Content that takes
            // whatever it is offered -- a Column stretching its cross axis -- takes ALL of it, which
            // is a wide callout rather than a broken one, and such content should state its own width
            // at the call site.
            var maxWidth = Mathf.Max(0f, self.x - (padding * 2f));
            var maxHeight = Mathf.Max(0f, self.y - (padding * 2f));

            // A child opening east or west gets at most the room on the wider side of its anchor, so
            // content that wraps fits there and the flip puts it on that side.
            if (this.RoomBesideAnchor(padding) is { } room)
                maxWidth = Mathf.Min(maxWidth, room);

            return new LayoutConstraints(
                width ?? 0f,
                0f,
                width.HasValue ? Mathf.Min(width.Value, maxWidth) : maxWidth,
                maxHeight
            );
        }

        private bool TryGetGeometry(out WidgetGeometry anchor, out Vector2 origin)
        {
            anchor = this.state.AnchorGeometry;
            var self = this.state.SelfGeometry;

            if (anchor.Equals(WidgetGeometry.Empty) || self.Equals(WidgetGeometry.Empty))
            {
                origin = Vector2.zero;
                return false;
            }

            origin = self.GetPoint(Alignment.TopLeft);
            return true;
        }

        private static float KeepInside(
            float position,
            float childExtent,
            float parentExtent,
            float padding
        )
        {
            var max = parentExtent - childExtent - padding;

            // Too big to fit even after the cap above (an unbounded frame, say): park it at the low
            // edge. Clamping to `max` would push the child PAST the near edge to bring its far edge
            // in, which reads as a positioning bug rather than as the overflow it is.
            if (max < padding)
                return padding;

            return Mathf.Clamp(position, padding, max);
        }

        private const string GiveItAFrame =
            "An AnchoredBox has no frame to place its child within, and keep-inside cannot hold "
            + "without one. Wrap it in Positioned.Fill, or pin the axis on the enclosing Positioned.";
    }
}

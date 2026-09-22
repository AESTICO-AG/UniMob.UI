using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UniMob.Core;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A watched <see cref="WidgetGeometryKey.GlobalGeometry"/> queues nothing while its box stands
    ///     still, and still follows a move Unity never reports.
    /// </summary>
    /// <remarks>
    ///     The standing-still check runs the ticker by hand right after the frame's scheduler pass, so
    ///     what the tick queues is the only work that can be pending. In a real frame the tick runs in
    ///     <c>LateUpdate</c>, and a harness waiting on <see cref="Zone.IsQuiescent"/> at the end of the
    ///     frame sees exactly that.
    /// </remarks>
    public class WidgetGeometryTrackingViewTests
    {
        private const int WatchedFrames = 10;

        private LifetimeController lifetime = null!;
        private GameObject canvasGo = null!;
        private RectTransform panelRect = null!;

        [SetUp]
        public void SetUp()
        {
            this.lifetime = new LifetimeController();

            this.canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            this.canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            this.lifetime.Dispose();
            Object.DestroyImmediate(this.canvasGo);
        }

        [UnityTest]
        public IEnumerator AWatchedBoxStandingStill_QueuesNothingWhenTicked()
        {
            var key = new WidgetGeometryKey();
            this.Host(key);
            yield return Settle();

            var observed = this.Watch(key);
            yield return Settle();

            var reactionsBefore = observed.Count;
            var pendingAfterTick = new List<bool>();

            for (var frame = 0; frame < WatchedFrames; frame++)
            {
                yield return null;

                WidgetGeometryTicker.Tick();
                pendingAfterTick.Add(AtomScheduler.HasPendingWork);
            }

            Assert.That(
                pendingAfterTick,
                Is.All.False,
                "the watched box never moved, yet ticking the geometry queued work; per frame: "
                    + string.Join(", ", pendingAfterTick)
            );
            Assert.AreEqual(
                reactionsBefore,
                observed.Count,
                "a box that did not move must not wake the code that follows it"
            );
        }

        [UnityTest]
        public IEnumerator AWatchedBoxMovedOutsideLayout_WakesItsFollowerOnTheNextFrame()
        {
            var key = new WidgetGeometryKey();
            this.Host(key);
            yield return Settle();

            var observed = this.Watch(key);
            yield return Settle();

            var before = observed[observed.Count - 1].Aabb.position;
            Assume.That(
                observed[observed.Count - 1],
                Is.Not.EqualTo(WidgetGeometry.Empty),
                "the box was never measured"
            );

            // A move no layout or render event reports: only a re-measure can notice it.
            this.panelRect.anchoredPosition += new Vector2(40f, 0f);

            yield return null;

            var after = observed[observed.Count - 1].Aabb.position;
            Assert.AreEqual(
                before.x + 40f,
                after.x,
                0.01f,
                $"the follower did not see the box move within a frame: {before} -> {after}"
            );
        }

        private List<WidgetGeometry> Watch(WidgetGeometryKey key)
        {
            var observed = new List<WidgetGeometry>();
            Atom.Reaction(
                this.lifetime.Lifetime,
                () => key.GlobalGeometry,
                geometry => observed.Add(geometry)
            );
            return observed;
        }

        private void Host(WidgetGeometryKey key)
        {
            var panelGo = new GameObject("ViewPanel", typeof(RectTransform));
            panelGo.transform.SetParent(this.canvasGo.transform, false);

            this.panelRect = (RectTransform)panelGo.transform;
            this.panelRect.anchorMin = Vector2.zero;
            this.panelRect.anchorMax = Vector2.one;
            this.panelRect.sizeDelta = Vector2.zero;

            var panel = panelGo.AddComponent<ViewPanel>();

            UniMobUI.RunApp(
                this.lifetime.Lifetime,
                panel,
                _ => new Align
                {
                    Alignment = Alignment.Center,
                    Child = new SizedBox
                    {
                        Key = key,
                        Width = 100,
                        Height = 50,
                        Child = new Container { BackgroundColor = Color.white },
                    },
                }
            );
        }

        private const int SettleFrames = 6;

        private static IEnumerator Settle()
        {
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }
        }
    }
}

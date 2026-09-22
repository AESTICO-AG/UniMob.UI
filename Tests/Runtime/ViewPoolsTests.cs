using System.Collections;
using NUnit.Framework;
using UniMob.UI.Internal.Pooling;
using UnityEngine;
using UnityEngine.TestTools;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     Pins what <see cref="ViewPools.DestroyIdle"/> removes from the pool and what it leaves.
    /// </summary>
    public class ViewPoolsTests
    {
        private GameObject _prefab = null!;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject(nameof(ViewPoolsTests) + "Prefab");
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(GameObjectPool.GetPool(_prefab).gameObject);
            Object.Destroy(_prefab);
        }

        [UnityTest]
        public IEnumerator AnIdleView_IsDestroyed_AndNeverHandedOutAgain()
        {
            var recycled = GameObjectPool.Instantiate(_prefab);
            GameObjectPool.Recycle(recycled);

            ViewPools.DestroyIdle();
            var next = GameObjectPool.Instantiate(_prefab);

            Assert.That(
                next,
                Is.Not.SameAs(recycled),
                "the pool handed out a view it was told to destroy."
            );

            yield return null;

            Assert.That(
                recycled == null,
                Is.True,
                "the idle view is still alive a frame after it was destroyed."
            );

            GameObjectPool.Recycle(next);
        }

        [UnityTest]
        public IEnumerator AViewInUse_Survives_AndStillReturnsToItsPool()
        {
            var inUse = GameObjectPool.Instantiate(_prefab);

            ViewPools.DestroyIdle();
            yield return null;

            Assert.That(inUse != null, Is.True, "a view in use was destroyed with the idle ones.");

            GameObjectPool.Recycle(inUse);
            var reused = GameObjectPool.Instantiate(_prefab);

            Assert.That(
                reused,
                Is.SameAs(inUse),
                "a view recycled after the pool was drained did not go back into it."
            );

            GameObjectPool.Recycle(reused);
        }
    }
}

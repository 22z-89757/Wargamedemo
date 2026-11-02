using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Wargame.Units;

namespace Wargame.Tests
{
    public sealed class Map1NavigationPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator LoadMap()
        {
            yield return SceneManager.LoadSceneAsync("Map1", LoadSceneMode.Single);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NavMeshHasDirectHalfBlockedAndFullBlockedRoutes()
        {
            AssertPath(new Vector3(2.5f, 0f, 2.5f), new Vector3(8.5f, 0f, 2.5f), 2);
            AssertPath(new Vector3(14.5f, 0f, 3.5f), new Vector3(16.5f, 0f, 3.5f), 3);
            AssertPath(new Vector3(8.5f, 0f, 7.5f), new Vector3(12.5f, 0f, 7.5f), 3);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ObstacleRigidbodiesRemainKinematicAndStationary()
        {
            Rigidbody[] bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(bodies.Length, Is.GreaterThan(0));
            Vector3[] positions = bodies.Select(body => body.position).ToArray();
            Assert.That(bodies.All(body => body.isKinematic && !body.useGravity), Is.True);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            for (int i = 0; i < bodies.Length; i++)
                Assert.That(Vector3.Distance(bodies[i].position, positions[i]), Is.LessThan(0.0001f));
        }

        [Test]
        public void PlayerAndEnemyPrefabsShareConfiguredShootVisuals()
        {
            Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(units.Any(unit => unit.Faction == Faction.Player &&
                unit.GetComponent<UnitShootVisualController>().ValidateConfiguration(out _)), Is.True);
            Assert.That(units.Any(unit => unit.Faction == Faction.Enemy &&
                unit.GetComponent<UnitShootVisualController>().ValidateConfiguration(out _)), Is.True);
        }

        private static void AssertPath(Vector3 start, Vector3 end, int minimumCorners)
        {
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(path.corners.Length, Is.GreaterThanOrEqualTo(minimumCorners));
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Wargame.Events;

namespace Wargame.Tests
{
    public sealed class BeginScenePlayModeTests
    {
        private readonly struct ProbeEvent { }

        private EventCenter events;
        private BeginSceneController beginController;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            EventCenter.ResetForTests();
            events = EventCenter.Instance;
            yield return SceneManager.LoadSceneAsync(GameFlowController.BeginSceneName, LoadSceneMode.Single);
            yield return null;
            beginController = Object.FindFirstObjectByType<BeginSceneController>();
            Assert.That(beginController, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene cleanup = SceneManager.CreateScene("BeginScenePlayModeCleanup");
            Scene active = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(cleanup);
            if (active.IsValid() && active != cleanup)
                yield return SceneManager.UnloadSceneAsync(active);
            EventCenter.ResetForTests();
        }

        [UnityTest]
        public IEnumerator OneButtonClickStartsMap1OnceAndRetainsUniqueEventCenter()
        {
            int startEvents = 0;
            int mapLoads = 0;
            System.Action<GameStartedEvent> onStarted = _ => startEvents++;
            UnityAction<Scene, LoadSceneMode> onSceneLoaded = (scene, _) =>
            {
                if (scene.name == GameFlowController.BattleSceneName)
                    mapLoads++;
            };
            events.Subscribe(onStarted);
            SceneManager.sceneLoaded += onSceneLoaded;
            GameFlowController flow = beginController.FlowController;

            beginController.StartButton.onClick.Invoke();
            Assert.That(flow.IsSceneTransitionPending, Is.True);
            for (int frame = 0; frame < 120 &&
                (mapLoads == 0 || startEvents == 0 || flow.HasPendingGameStart); frame++)
                yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameFlowController.BattleSceneName));
            Assert.That(mapLoads, Is.EqualTo(1));
            Assert.That(startEvents, Is.EqualTo(1));
            Assert.That(flow.HasPendingGameStart, Is.False);
            Assert.That(FindEventCenters(), Is.EqualTo(1));
            SceneManager.sceneLoaded -= onSceneLoaded;
            events.Unsubscribe(onStarted);
        }

        [UnityTest]
        public IEnumerator ReturningToBeginRemovesDuplicateRootAndKeepsSingleDispatch()
        {
            EventCenter primary = events;
            yield return SceneManager.LoadSceneAsync(GameFlowController.BattleSceneName, LoadSceneMode.Single);
            yield return SceneManager.LoadSceneAsync(GameFlowController.BeginSceneName, LoadSceneMode.Single);
            yield return null;

            beginController = Object.FindFirstObjectByType<BeginSceneController>();
            Assert.That(beginController, Is.Not.Null);
            Assert.That(beginController.FlowController, Is.SameAs(primary.GetComponent<GameFlowController>()));
            Assert.That(EventCenter.Instance, Is.SameAs(primary));
            Assert.That(FindEventCenters(), Is.EqualTo(1));

            int calls = 0;
            System.Action<ProbeEvent> listener = _ => calls++;
            primary.Subscribe(listener);
            primary.Publish(new ProbeEvent());
            primary.Unsubscribe(listener);
            Assert.That(calls, Is.EqualTo(1));
        }

        private static int FindEventCenters() => Object.FindObjectsByType<EventCenter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    }
}

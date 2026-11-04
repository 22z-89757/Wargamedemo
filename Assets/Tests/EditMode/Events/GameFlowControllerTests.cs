using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Wargame.Events;

namespace Wargame.Tests
{
    public sealed class GameFlowControllerTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            EventCenter.ResetForTests();
            var root = new GameObject("GameFlowControllerTests");
            created.Add(root);
            flow = root.AddComponent<GameFlowController>();
            flow.ConfigureForTests(_ => { });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in created)
                if (gameObject != null)
                    Object.DestroyImmediate(gameObject);
            created.Clear();
            EventCenter.ResetForTests();
        }

        [Test]
        public void GameEndedEventStoresResultBeforeRequestingEndScene()
        {
            GameOutcome? resultDuringLoad = null;
            string requestedScene = null;
            flow.ConfigureForTests(scene =>
            {
                requestedScene = scene;
                resultDuringLoad = flow.LastResult;
            });

            flow.HandleGameEndedForTests(new GameEndedEvent(GameOutcome.Victory));

            Assert.That(resultDuringLoad, Is.EqualTo(GameOutcome.Victory));
            Assert.That(requestedScene, Is.EqualTo(GameFlowController.EndSceneName));
            Assert.That(flow.LastResult, Is.EqualTo(GameOutcome.Victory));
        }

        [Test]
        public void GameStartedEventClearsResultAndRejectsDuplicateSceneRequests()
        {
            int loadRequests = 0;
            string requestedScene = null;
            flow.HandleGameEndedForTests(new GameEndedEvent(GameOutcome.Defeat));
            flow.ConfigureForTests(scene =>
            {
                loadRequests++;
                requestedScene = scene;
            });

            EventCenter.Instance.Publish(new GameStartedEvent());
            EventCenter.Instance.Publish(new GameStartedEvent());
            Assert.That(loadRequests, Is.EqualTo(1));
            Assert.That(requestedScene, Is.EqualTo(GameFlowController.BattleSceneName));
            Assert.That(flow.LastResult, Is.Null);
            Assert.That(flow.IsSceneTransitionPending, Is.True);
            Assert.That(flow.HasPendingGameStart, Is.True);
        }

        [Test]
        public void PendingGameStartIsConsumedOnlyOnce()
        {
            EventCenter.Instance.Publish(new GameStartedEvent());

            Assert.That(flow.TryConsumePendingGameStart(), Is.True);
            Assert.That(flow.HasPendingGameStart, Is.False);
            Assert.That(flow.TryConsumePendingGameStart(), Is.False);
        }

        [Test]
        public void ReturningToBeginClearsResultAndRequestsBeginScene()
        {
            string requestedScene = null;
            flow.HandleGameEndedForTests(new GameEndedEvent(GameOutcome.Draw));
            flow.ConfigureForTests(scene => requestedScene = scene);

            Assert.That(flow.ReturnToBeginScene(), Is.True);
            Assert.That(requestedScene, Is.EqualTo(GameFlowController.BeginSceneName));
            Assert.That(flow.LastResult, Is.Null);
            Assert.That(flow.HasPendingGameStart, Is.False);
        }

        [Test]
        public void FailedSceneRequestReleasesTransitionGuard()
        {
            int requests = 0;
            LogAssert.Expect(LogType.Error, "场景切换请求失败：test failure");
            flow.ConfigureForTests(_ =>
            {
                requests++;
                throw new System.InvalidOperationException("test failure");
            });

            EventCenter.Instance.Publish(new GameStartedEvent());
            Assert.That(flow.IsSceneTransitionPending, Is.False);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(flow.HasPendingGameStart, Is.False);
        }
    }
}

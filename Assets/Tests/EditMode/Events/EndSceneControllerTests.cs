using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Wargame.Events;
using Object = UnityEngine.Object;

namespace Wargame.Tests
{
    public sealed class EndSceneControllerTests
    {
        private GameObject root;
        private GameFlowController flow;
        private EndSceneController controller;
        private TMP_Text resultText;
        private Button returnButton;
        private int returnCalls;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("EndSceneControllerTests");
            flow = root.AddComponent<GameFlowController>();
            var labelObject = new GameObject("Result", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);
            resultText = labelObject.GetComponent<TMP_Text>();
            var buttonObject = new GameObject("Return", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(root.transform, false);
            returnButton = buttonObject.GetComponent<Button>();
            controller = root.AddComponent<EndSceneController>();
            controller.ConfigureForTests(flow, resultText, returnButton);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        [TestCase(GameOutcome.Victory, "胜利")]
        [TestCase(GameOutcome.Defeat, "失败")]
        [TestCase(GameOutcome.Draw, "平局")]
        public void RefreshResultMapsOutcomeToChineseLabel(GameOutcome outcome, string expected)
        {
            SetLastResult(outcome);

            controller.RefreshResult();

            Assert.That(resultText.text, Is.EqualTo(expected));
        }

        [Test]
        public void MissingResultShowsSafeTextAndReturnClearsResult()
        {
            flow.ConfigureForTests(_ => returnCalls++);
            controller.RefreshResult();

            Assert.That(resultText.text, Is.EqualTo("暂无本局结算数据"));
            flow.HandleGameEndedForTests(new GameEndedEvent(GameOutcome.Victory));
            Assert.That(controller.ReturnButton, Is.SameAs(returnButton));
            controller.ReturnToBegin();

            Assert.That(flow.LastResult, Is.Null);
            Assert.That(returnCalls, Is.EqualTo(1));
        }

        private void SetLastResult(GameOutcome outcome)
        {
            flow.ConfigureForTests(_ => { });
            flow.HandleGameEndedForTests(new GameEndedEvent(outcome));
            flow.ConfigureForTests(_ => { });
        }
    }
}

using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Wargame.Events;

namespace Wargame.Tests
{
    public sealed class BeginSceneValidationTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp()
        {
            EventCenter.ResetForTests();
            scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BeginScene.unity");
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid())
                EditorSceneManager.ClosePreviewScene(scene);
            EventCenter.ResetForTests();
        }

        [Test]
        public void BeginSceneHasPersistentRootAndFullyWiredStartUi()
        {
            GameObject root = null;
            Canvas canvas = null;
            EventSystem eventSystem = null;
            InputSystemUIInputModule inputModule = null;
            BeginSceneController controller = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == nameof(EventCenter)) root = candidate;
                if (candidate.TryGetComponent(out Canvas candidateCanvas)) canvas = candidateCanvas;
                if (candidate.TryGetComponent(out EventSystem candidateEventSystem)) eventSystem = candidateEventSystem;
                if (candidate.TryGetComponent(out InputSystemUIInputModule candidateInput)) inputModule = candidateInput;
                BeginSceneController candidateController = candidate.GetComponentInChildren<BeginSceneController>(true);
                if (candidateController != null) controller = candidateController;
            }

            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetComponent<EventCenter>(), Is.Not.Null);
            Assert.That(root.GetComponent<GameFlowController>(), Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(inputModule, Is.Not.Null);
            inputModule.AssignDefaultActions();
            Assert.That(inputModule.actionsAsset, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.FlowController, Is.SameAs(root.GetComponent<GameFlowController>()));
            Assert.That(controller.StartButton, Is.Not.Null);
            Assert.That(controller.StartButton.GetComponentInChildren<TMPro.TMP_Text>(), Is.Not.Null);
            Assert.That(controller.StartButton.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("开始游戏"));
            Assert.That(controller.StartButton.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(controller.StartButton.onClick.GetPersistentTarget(0), Is.SameAs(controller));
            Assert.That(controller.StartButton.onClick.GetPersistentMethodName(0), Is.EqualTo(nameof(BeginSceneController.StartGame)));
        }
    }
}

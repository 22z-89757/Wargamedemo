using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Wargame.Events;

namespace Wargame.Tests
{
    public sealed class EndSceneValidationTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/End.unity");

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid())
                EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void EndSceneHasCanvasEventSystemAndWiredResultControls()
        {
            Canvas canvas = null;
            EventSystem eventSystem = null;
            InputSystemUIInputModule input = null;
            EndSceneController controller = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out Canvas candidateCanvas)) canvas = candidateCanvas;
                if (root.TryGetComponent(out EventSystem candidateEventSystem)) eventSystem = candidateEventSystem;
                if (root.TryGetComponent(out InputSystemUIInputModule candidateInput)) input = candidateInput;
                EndSceneController candidateController = root.GetComponentInChildren<EndSceneController>(true);
                if (candidateController != null) controller = candidateController;
            }

            Assert.That(canvas, Is.Not.Null);
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.ResultText, Is.Not.Null);
            Assert.That(controller.ResultText.text, Is.EqualTo("暂无本局结算数据"));
            Assert.That(controller.ReturnButton, Is.Not.Null);
            Assert.That(controller.ReturnButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("返回开始界面"));
        }
    }
}

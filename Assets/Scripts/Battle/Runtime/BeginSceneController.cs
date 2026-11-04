using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Wargame.Events
{
    /// <summary>开始界面的按钮与游戏流程控制器之间的薄适配层。</summary>
    public sealed class BeginSceneController : MonoBehaviour
    {
        [SerializeField] private GameFlowController flowController;
        [SerializeField] private Button startButton;

        public GameFlowController FlowController => ResolveFlowController();
        public Button StartButton => startButton;

        private void Awake()
        {
            InputSystemUIInputModule inputModule = FindFirstObjectByType<InputSystemUIInputModule>();
            if (inputModule != null && inputModule.actionsAsset == null)
                inputModule.AssignDefaultActions();
        }

        public void StartGame()
        {
            GameFlowController flow = ResolveFlowController();
            if (flow == null || startButton == null || !startButton.interactable)
                return;

            EventCenter.Instance.Publish(new GameStartedEvent());
            if (flow.IsSceneTransitionPending)
                startButton.interactable = false;
        }

        private GameFlowController ResolveFlowController()
        {
            if (flowController != null)
                return flowController;

            EventCenter center = EventCenter.Instance;
            flowController = center.GetComponent<GameFlowController>();
            if (flowController == null)
                flowController = center.gameObject.AddComponent<GameFlowController>();
            return flowController;
        }
    }
}

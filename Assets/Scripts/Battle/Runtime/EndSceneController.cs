using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Wargame.Events
{
    /// <summary>读取已保存的结算结果并驱动结算界面。</summary>
    public sealed class EndSceneController : MonoBehaviour
    {
        [SerializeField] private GameFlowController flowController;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private Button returnButton;

        public GameFlowController FlowController => flowController;
        public TMP_Text ResultText => resultText;
        public Button ReturnButton => returnButton;

        private void OnEnable()
        {
            if (flowController == null)
            {
                EventCenter center = EventCenter.Instance;
                flowController = center.GetComponent<GameFlowController>();
                if (flowController == null)
                    flowController = center.gameObject.AddComponent<GameFlowController>();
            }

            if (returnButton != null)
                returnButton.onClick.AddListener(ReturnToBegin);
            RefreshResult();
        }

        private void OnDisable()
        {
            if (returnButton != null)
                returnButton.onClick.RemoveListener(ReturnToBegin);
        }

        public void RefreshResult()
        {
            if (resultText == null)
                return;

            resultText.text = flowController == null || !flowController.LastResult.HasValue
                ? "暂无本局结算数据"
                : GetResultText(flowController.LastResult.Value);
        }

        public void ReturnToBegin()
        {
            flowController?.ReturnToBeginScene();
        }

        public static string GetResultText(GameOutcome outcome)
        {
            switch (outcome)
            {
                case GameOutcome.Victory: return "胜利";
                case GameOutcome.Defeat: return "失败";
                case GameOutcome.Draw: return "平局";
                default: return "暂无本局结算数据";
            }
        }

        internal void ConfigureForTests(GameFlowController flow, TMP_Text label, Button backButton)
        {
            flowController = flow;
            resultText = label;
            returnButton = backButton;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wargame.Events
{
    /// <summary>跨场景保存单局结算并协调开始、结束界面的导航。</summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowController : MonoBehaviour
    {
        public const string BeginSceneName = "BeginScene";
        public const string BattleSceneName = "Map1";
        public const string EndSceneName = "End";

        private Action<string> sceneLoader;
        private EventCenter subscribedCenter;
        private bool initialized;
        private bool sceneTransitionPending;
        private bool pendingGameStart;

        public GameOutcome? LastResult { get; private set; }
        public bool HasLastResult => LastResult.HasValue;
        public bool IsSceneTransitionPending => sceneTransitionPending;
        public bool HasPendingGameStart => pendingGameStart;

        private void Awake() => Initialize();

        private void OnEnable() => Initialize();

        private void OnDisable()
        {
            if (!initialized)
                return;

            if (subscribedCenter != null)
            {
                subscribedCenter.Unsubscribe<GameStartedEvent>(HandleGameStarted);
                subscribedCenter.Unsubscribe<GameEndedEvent>(HandleGameEnded);
            }
            subscribedCenter = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            initialized = false;
        }

        private void Initialize()
        {
            if (initialized)
                return;

            subscribedCenter = EventCenter.Instance;
            subscribedCenter.Subscribe<GameStartedEvent>(HandleGameStarted);
            subscribedCenter.Subscribe<GameEndedEvent>(HandleGameEnded);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            initialized = true;
        }

        private void HandleGameStarted(GameStartedEvent gameStarted)
        {
            if (sceneTransitionPending)
                return;

            LastResult = null;
            pendingGameStart = true;
            if (!RequestSceneLoad(BattleSceneName))
                pendingGameStart = false;
        }

        public bool ReturnToBeginScene()
        {
            LastResult = null;
            pendingGameStart = false;
            return RequestSceneLoad(BeginSceneName);
        }

        /// <summary>仅在战斗场景初始化成功后消费由开始请求设置的单局状态。</summary>
        public bool TryConsumePendingGameStart()
        {
            if (!pendingGameStart)
                return false;

            pendingGameStart = false;
            return true;
        }

        private void HandleGameEnded(GameEndedEvent gameEnded)
        {
            LastResult = gameEnded.Outcome;
            RequestSceneLoad(EndSceneName);
        }

        private bool RequestSceneLoad(string sceneName)
        {
            if (sceneTransitionPending)
                return false;

            sceneTransitionPending = true;
            try
            {
                if (sceneLoader != null)
                    sceneLoader(sceneName);
                else if (SceneManager.LoadSceneAsync(sceneName) == null)
                    throw new InvalidOperationException($"无法加载场景 {sceneName}。");
                return true;
            }
            catch (Exception exception)
            {
                sceneTransitionPending = false;
                Debug.LogError($"场景切换请求失败：{exception.Message}", this);
                return false;
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => sceneTransitionPending = false;

        internal void ConfigureForTests(Action<string> loader)
        {
            sceneLoader = loader;
            sceneTransitionPending = false;
            Initialize();
        }

        internal void HandleGameEndedForTests(GameEndedEvent gameEnded) => HandleGameEnded(gameEnded);
    }
}

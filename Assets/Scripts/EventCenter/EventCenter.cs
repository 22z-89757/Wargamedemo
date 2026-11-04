using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wargame.Events
{
    /// <summary>以事件数据类型为标识、跨场景存活的全局事件中心。</summary>
    [DisallowMultipleComponent]
    public sealed class EventCenter : MonoBehaviour
    {
        private readonly Dictionary<Type, List<Delegate>> listeners =
            new Dictionary<Type, List<Delegate>>();
        private static EventCenter instance;

        /// <summary>取得唯一事件中心；场景未配置实例时会创建运行时实例。</summary>
        public static EventCenter Instance
        {
            get
            {
                if (instance != null)
                    return instance;

                instance = FindFirstObjectByType<EventCenter>(FindObjectsInactive.Include);
                if (instance != null)
                    return instance;

                var root = new GameObject(nameof(EventCenter));
                instance = root.AddComponent<EventCenter>();
                return instance;
            }
        }

        /// <summary>当前对象是否是全局保留的实例。</summary>
        public bool IsPrimaryInstance => instance == this;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                if (GetComponent<GameFlowController>() != null &&
                    instance.GetComponent<GameFlowController>() == null)
                    instance.gameObject.AddComponent<GameFlowController>();

                if (Application.isPlaying)
                    Destroy(gameObject);
                else
                    DestroyImmediate(gameObject);
                return;
            }

            instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        /// <summary>订阅一种事件；同一回调的重复订阅会被忽略。</summary>
        public void Subscribe<TEvent>(Action<TEvent> listener)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            Type eventType = typeof(TEvent);
            if (!listeners.TryGetValue(eventType, out List<Delegate> eventListeners))
            {
                eventListeners = new List<Delegate>();
                listeners.Add(eventType, eventListeners);
            }

            if (!eventListeners.Contains(listener))
                eventListeners.Add(listener);
        }

        /// <summary>退订一种事件；回调不存在时不执行任何操作。</summary>
        public void Unsubscribe<TEvent>(Action<TEvent> listener)
        {
            if (listener == null)
                return;

            Type eventType = typeof(TEvent);
            if (!listeners.TryGetValue(eventType, out List<Delegate> eventListeners))
                return;

            eventListeners.Remove(listener);
            if (eventListeners.Count == 0)
                listeners.Remove(eventType);
        }

        /// <summary>向发布开始时存在的全部同类型监听者分发事件。</summary>
        public void Publish<TEvent>(TEvent eventData)
        {
            if (!listeners.TryGetValue(typeof(TEvent), out List<Delegate> eventListeners))
                return;

            Delegate[] snapshot = eventListeners.ToArray();
            foreach (Delegate callback in snapshot)
                ((Action<TEvent>)callback).Invoke(eventData);
        }

        /// <summary>清空全部监听，仅用于受控关闭或测试隔离。</summary>
        public void ClearAllListeners() => listeners.Clear();

        private void OnDestroy()
        {
            if (instance != this)
                return;

            listeners.Clear();
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        internal static void ResetForTests()
        {
            EventCenter[] existing = FindObjectsByType<EventCenter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EventCenter center in existing)
            {
                center.ClearAllListeners();
                if (center.gameObject != null)
                    DestroyImmediate(center.gameObject);
            }
            instance = null;
        }
    }
}

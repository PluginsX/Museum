using UnityEngine;
using UnityEngine.Events;

namespace Museum.Component
{
    /// <summary>
    /// 生命周期事件组件
    /// 暴露出GameObject的生命周期事件，允许用户在Inspector中绑定自定义方法
    /// </summary>
    [AddComponentMenu("Component/LifeCycleEvent")]
    public class LifeCycleEvent : MonoBehaviour
    {
        #region 生命周期事件定义
        [Header("初始化事件")]
        [Tooltip("Awake事件：在对象创建时触发（场景加载时）")]
        public UnityEvent onAwake = new UnityEvent();

        [Tooltip("Start事件：在第一次Update之前触发")]
        public UnityEvent onStart = new UnityEvent();

        [Header("启用/禁用事件")]
        [Tooltip("OnEnable事件：对象被启用时触发")]
        public UnityEvent onEnable = new UnityEvent();

        [Tooltip("OnDisable事件：对象被禁用时触发")]
        public UnityEvent onDisable = new UnityEvent();

        [Header("更新事件")]
        [Tooltip("Update事件：每帧更新时触发")]
        public UnityEvent onUpdate = new UnityEvent();

        [Tooltip("FixedUpdate事件：固定时间间隔更新时触发")]
        public UnityEvent onFixedUpdate = new UnityEvent();

        [Tooltip("LateUpdate事件：所有Update执行完毕后触发")]
        public UnityEvent onLateUpdate = new UnityEvent();

        [Header("销毁事件")]
        [Tooltip("OnDestroy事件：对象被销毁时触发")]
        public UnityEvent onDestroy = new UnityEvent();

        [Header("碰撞事件")]
        [Tooltip("OnCollisionEnter事件：开始碰撞时触发")]
        public UnityEvent<Collision> onCollisionEnter = new UnityEvent<Collision>();

        [Tooltip("OnCollisionStay事件：碰撞持续时触发")]
        public UnityEvent<Collision> onCollisionStay = new UnityEvent<Collision>();

        [Tooltip("OnCollisionExit事件：结束碰撞时触发")]
        public UnityEvent<Collision> onCollisionExit = new UnityEvent<Collision>();

        [Header("触发器事件")]
        [Tooltip("OnTriggerEnter事件：进入触发器时触发")]
        public UnityEvent<Collider> onTriggerEnter = new UnityEvent<Collider>();

        [Tooltip("OnTriggerStay事件：停留在触发器内时触发")]
        public UnityEvent<Collider> onTriggerStay = new UnityEvent<Collider>();

        [Tooltip("OnTriggerExit事件：离开触发器时触发")]
        public UnityEvent<Collider> onTriggerExit = new UnityEvent<Collider>();
        #endregion

        #region Unity生命周期方法
        private void Awake()
        {
            onAwake?.Invoke();
        }

        private void Start()
        {
            onStart?.Invoke();
        }

        private void OnEnable()
        {
            onEnable?.Invoke();
        }

        private void OnDisable()
        {
            onDisable?.Invoke();
        }

        private void Update()
        {
            onUpdate?.Invoke();
        }

        private void FixedUpdate()
        {
            onFixedUpdate?.Invoke();
        }

        private void LateUpdate()
        {
            onLateUpdate?.Invoke();
        }

        private void OnDestroy()
        {
            onDestroy?.Invoke();
        }

        private void OnCollisionEnter(Collision collision)
        {
            onCollisionEnter?.Invoke(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            onCollisionStay?.Invoke(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            onCollisionExit?.Invoke(collision);
        }

        private void OnTriggerEnter(Collider other)
        {
            onTriggerEnter?.Invoke(other);
        }

        private void OnTriggerStay(Collider other)
        {
            onTriggerStay?.Invoke(other);
        }

        private void OnTriggerExit(Collider other)
        {
            onTriggerExit?.Invoke(other);
        }
        #endregion
    }
}

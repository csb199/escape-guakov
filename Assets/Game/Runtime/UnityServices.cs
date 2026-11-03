using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Runtime
{
    public sealed class UnityJson : IJsonCodec
    {
        public string Encode<T>(T value) => JsonUtility.ToJson(value);
        public T Decode<T>(string json) => JsonUtility.FromJson<T>(json);
    }
    [Serializable] public sealed class GameConfig
    {
        public string clientBuild = "energy-demo-2022.3.62-win64-v1";
        public string platform = "StandaloneWindows64";
        public string remoteDirectory = "";
        public string publicKey;
        public int developmentLogLevel = 0, releaseLogLevel = 1;
        public UpdateCachePolicy updateCache = new UpdateCachePolicy();
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(clientBuild) || string.IsNullOrWhiteSpace(publicKey) ||
                developmentLogLevel < 0 || developmentLogLevel > 4 || releaseLogLevel < 0 || releaseLogLevel > 4)
                throw new InvalidOperationException("Invalid game configuration");
            if (updateCache == null) updateCache = new UpdateCachePolicy();
            updateCache.Validate();
        }
    }
    public static class UnityTask
    {
        // Unity AsyncOperation cannot be aborted. Await completion before cancellation cleanup.
        public static async Task Complete(AsyncOperation operation)
        { while (!operation.isDone) await Task.Yield(); }
        public static async Task NextFrame()
        {
            int frame = Time.frameCount;
            // A single Task.Yield from Update may resume later in the same frame, before Destroy is processed.
            while (Time.frameCount == frame) await Task.Yield();
        }
    }
    public sealed class AssetLease<T> : IDisposable where T : Object
    {
        Action release;
        public T Asset { get; }
        public AssetLease(T asset, Action release) { Asset = asset; this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }
    public sealed class GameObjectPool : IDisposable
    {
        readonly GameObject prefab;
        readonly Transform root;
        readonly int capacity;
        readonly Stack<GameObject> idle = new Stack<GameObject>();
        readonly HashSet<GameObject> active = new HashSet<GameObject>();
        public int ActiveCount => active.Count;
        public int CreatedCount { get; private set; }
        public GameObjectPool(GameObject prefab, Transform parent, int capacity)
        {
            this.prefab = prefab; this.capacity = capacity;
            root = new GameObject("OrbPool").transform; root.SetParent(parent);
        }
        public GameObject Rent(Vector3 position)
        {
            if (active.Count >= capacity) throw new InvalidOperationException("Pool capacity reached");
            GameObject value;
            if (idle.Count != 0) value = idle.Pop();
            else { value = Object.Instantiate(prefab, root); CreatedCount++; }
            value.transform.position = position; value.SetActive(true); active.Add(value); return value;
        }
        public void Return(GameObject value)
        {
            if (!active.Remove(value)) throw new InvalidOperationException("Foreign or already returned object");
            value.SetActive(false); value.transform.SetParent(root); idle.Push(value);
        }
        public void ReturnAll() { foreach (var value in new List<GameObject>(active)) Return(value); }
        public void Dispose()
        {
            ReturnAll(); foreach (var value in idle) Object.Destroy(value); idle.Clear(); Object.Destroy(root.gameObject);
        }
    }
    public enum UiLayer { Screen = 0, Popup = 100, Overlay = 200 }
    public sealed partial class UiService
    {
        public RectTransform Panel(Transform parent, string name, Rect rect, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image)); gameObject.transform.SetParent(parent, false);
            var transform = (RectTransform)gameObject.transform; Place(transform, rect); gameObject.GetComponent<Image>().color = color; return transform;
        }
        public Text Label(Transform parent, string text, Rect rect, int size = 20, Color? color = null)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(Text)); gameObject.transform.SetParent(parent, false);
            var label = gameObject.GetComponent<Text>(); label.font = font; label.text = text; label.fontSize = size;
            label.color = color ?? new Color(0.87f, 0.93f, 0.97f); label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
            Place((RectTransform)gameObject.transform, rect); return label;
        }
        public Button Button(Transform parent, string text, Rect rect, Action clicked, bool primary = false)
        {
            var panel = Panel(parent, text, rect, primary ? new Color(0.1f, 0.65f, 0.65f) : new Color(0.12f, 0.19f, 0.27f));
            var button = panel.gameObject.AddComponent<Button>(); button.targetGraphic = panel.GetComponent<Image>();
            button.onClick.AddListener(() => clicked());
            var label = Label(panel, text, new Rect(0, 0, rect.width, rect.height), 18); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        public Slider Slider(Transform parent, Rect rect, float value, Action<float> changed)
        {
            var track = Panel(parent, "Volume", rect, new Color(0.12f, 0.19f, 0.27f));
            var handle = Panel(track, "Handle", new Rect(0, 0, 20, rect.height), new Color(0.2f, 0.9f, 0.8f));
            var slider = track.gameObject.AddComponent<Slider>(); slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>(); slider.value = value; slider.onValueChanged.AddListener(v => changed(v)); return slider;
        }
        static void Place(RectTransform target, Rect rect)
        {
            target.anchorMin = target.anchorMax = target.pivot = new Vector2(0, 1);
            target.anchoredPosition = new Vector2(rect.x, -rect.y); target.sizeDelta = rect.size;
        }

    }
}

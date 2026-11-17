using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Game.HotUpdate.Barrack
{
    public sealed class BarrackView : MonoBehaviour
    {
        BarrackPreview host;
        Font font;
        RectTransform canvasRect;
        GameObject content;
        Text status;
        Button returnButton;
        readonly Dictionary<BarrackAction, Button> actions = new Dictionary<BarrackAction, Button>();
        readonly List<(string bone, RectTransform marker)> markers = new List<(string, RectTransform)>();
        readonly List<Toggle> switches = new List<Toggle>();
        bool visible = true;
        GameObject sourceRoot; bool diagnostics;
        public int ActionButtonCount => actions.Count;
        public void Initialize(BarrackPreview host)
        {
            this.host = host;
            RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
            var canvasObject = new GameObject("Barrack UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;canvas.pixelPerfect=true;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            if (EventSystem.current == null)
            {
                var events = new GameObject("Barrack Input", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(transform, false);
            }
            var surface = Rect("Viewport Input", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = surface.gameObject.AddComponent<Image>(); image.color = Color.clear;
            surface.gameObject.AddComponent<BarrackInputSurface>().host = host;
            content = Rect("Panels", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var header = Panel("Identity", content.transform, new Vector2(0, 1), new Vector2(36, -36), new Vector2(390, 116));
            Label(header, "整备室", 23, new Vector2(24, -16), new Vector2(330, 34), new Color(0.75f, 0.8f, 0.82f));
            Label(header, "角色展示", 34, new Vector2(24, -52), new Vector2(350, 50), Color.white);
            var accent = Rect("Accent", header, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(4, 116));
            accent.gameObject.AddComponent<Image>().color = new Color(0.92f, 0.49f, 0.24f);
            var panel = Panel("Controls", content.transform, new Vector2(1, 1), new Vector2(-36, -160), new Vector2(300, 760));
            Label(panel, "角色互动", 24, new Vector2(22, -18), new Vector2(256, 36), Color.white);
            var entries = new[] { (BarrackAction.Stretch, "舒展"), (BarrackAction.Inspect, "观察动作"), (BarrackAction.InspectWeapon, "专属检视"), (BarrackAction.Head, "头部互动"), (BarrackAction.Weapon, "武器互动"), (BarrackAction.Pocket, "口袋互动"), (BarrackAction.Around, "环绕镜头"), (BarrackAction.Look, "面部特写") };
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; var button = MakeButton(panel, entry.Item2, new Vector2(22, -70 - i * 57), new Vector2(256, 46));
                button.onClick.AddListener(() => { if (host.Ready) host.Interaction.Request(entry.Item1); }); actions.Add(entry.Item1, button);
            }
            returnButton = MakeButton(panel, "返回默认视角", new Vector2(22, -540), new Vector2(256, 46));
            returnButton.onClick.AddListener(() => host.Interaction?.Return());
            AddToggle(panel, "实时阴影", new Vector2(22, -612), p => p.ShadowsEnabled, (p, value) => p.ShadowsEnabled = value);
            AddToggle(panel, "环境与地面反射", new Vector2(22, -646), p => p.ReflectionsEnabled, (p, value) => p.ReflectionsEnabled = value);
            AddToggle(panel, "环境遮蔽", new Vector2(22, -680), p => p.AmbientOcclusionEnabled, (p, value) => p.AmbientOcclusionEnabled = value);
            AddToggle(panel, "后处理", new Vector2(22, -714), p => p.PostProcessingEnabled, (p, value) => p.PostProcessingEnabled = value);
            var footer = Panel("Status", content.transform, new Vector2(0, 0), new Vector2(36, 112), new Vector2(800, 78));
            status = Label(footer, "正在载入整备室…", 22, new Vector2(20, -12), new Vector2(750, 30), Color.white);
            Label(footer, "拖动旋转 · 滚轮缩放 · 点击头部、右手或口袋互动", 18, new Vector2(20, -43), new Vector2(760, 26), new Color(0.74f, 0.78f, 0.8f));
            var toggleUI = MakeButton(content.GetComponent<RectTransform>(), "显示 / 隐藏界面", new Vector2(-260, -36), new Vector2(224, 46));
            var toggleRect = toggleUI.GetComponent<RectTransform>(); toggleRect.anchorMin = toggleRect.anchorMax = Vector2.one;
            toggleUI.onClick.AddListener(ToggleVisibility);
            foreach (var name in new[] { "Head_M", "Wrist_R", "Hip_R" })
            {
                var marker = Rect("Touch " + name, content.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12));
                var graphic = marker.gameObject.AddComponent<Image>(); graphic.color = new Color(1, 0.65f, 0.35f, 0.65f); graphic.raycastTarget = false; markers.Add((name, marker));
            }
            if(host.stageDefinition.sourceUi != null) { sourceRoot = Rect("Source UI", canvasRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject; gameObject.AddComponent<BarrackSourceUi>().Build(host,sourceRoot.transform,font); content.SetActive(false); }
            Refresh();
        }
        public void InvokeReturn() { if (returnButton.interactable) returnButton.onClick.Invoke(); }
        public void SetVisible(bool value) { visible = value; content.SetActive(value && diagnostics); if(sourceRoot!=null)sourceRoot.SetActive(value && !diagnostics); }
        public void InvokeAction(BarrackAction action) { if (actions.TryGetValue(action, out var button) && button.interactable) button.onClick.Invoke(); }
        public void Refresh()
        {
            if (host == null || status == null) return;
            bool interactive = host.Ready && host.Interaction.CanInteract;
            foreach (var pair in actions) { bool supported=host.Interaction?.Supports(pair.Key)??false;pair.Value.gameObject.SetActive(supported);pair.Value.interactable=interactive&&supported; }
            returnButton.interactable = host.Ready && host.Interaction.Mode != BarrackInteractionMode.Returning;
            foreach (var toggle in switches) toggle.interactable = host.Ready;
            if (host.Failure != null) status.text = "整备室加载失败：" + host.Failure;
            else if (!host.Ready) status.text = "正在载入整备室…";
            else status.text = host.Interaction.Mode switch {
                BarrackInteractionMode.Entrance => host.CharacterName+"正在进入整备室", BarrackInteractionMode.Idle => "默认整备室 · 待机",
                BarrackInteractionMode.Orbit => "自由观察 · 返回可恢复默认视角", BarrackInteractionMode.Returning => "正在返回待机", _ => "互动中" };
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) { diagnostics=!diagnostics;SetVisible(visible); }
            if (Input.GetKeyDown(KeyCode.Tab)) ToggleVisibility();
            if (Input.GetKeyDown(KeyCode.Escape)) host?.Interaction?.Return();
        }
        void BeforeCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (host == null || camera != host.displayCamera) return;
            UpdateMarkers(); Canvas.ForceUpdateCanvases();
        }
        void UpdateMarkers()
        {
            if (host == null || !host.Ready) return;
            foreach (var marker in markers)
            {
                var bone = host.Bone(marker.bone); bool show = visible && host.Interaction.CanInteract && bone != null;
                marker.marker.gameObject.SetActive(show); if (!show) continue;
                var screen = host.displayCamera.WorldToViewportPoint(bone.position);
                if (screen.z <= 0) { marker.marker.gameObject.SetActive(false); continue; }
                marker.marker.anchorMin = marker.marker.anchorMax = new Vector2(screen.x, screen.y); marker.marker.anchoredPosition = Vector2.zero;
            }
        }
        void ToggleVisibility() { SetVisible(!visible); }
        void AddToggle(Transform parent, string title, Vector2 position, Func<BarrackRenderPipeline, bool> get, Action<BarrackRenderPipeline, bool> set)
        {
            var rect = Rect(title, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(256, 30));
            var toggle = rect.gameObject.AddComponent<Toggle>(); var box = Rect("Box", rect, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(22, 22));
            var background = box.gameObject.AddComponent<Image>(); background.color = new Color(0.25f, 0.29f, 0.31f);
            var check = Rect("Check", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12));
            var checkImage = check.gameObject.AddComponent<Image>(); checkImage.color = new Color(0.95f, 0.6f, 0.3f);
            toggle.targetGraphic = background; toggle.graphic = checkImage; toggle.isOn = true;
            Label(rect, title, 18, new Vector2(34, 0), new Vector2(220, 30), new Color(0.8f, 0.84f, 0.86f));
            toggle.onValueChanged.AddListener(value => { if (RenderPipelineManager.currentPipeline is BarrackRenderPipeline pipeline) set(pipeline, value); }); switches.Add(toggle);
        }
        RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        { var rect = Rect(name, parent, anchor, anchor, new Vector2(anchor.x, 1), position, size); var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.045f, 0.06f, 0.075f, 0.85f); return rect; }
        Button MakeButton(Transform parent, string title, Vector2 position, Vector2 size)
        {
            var rect = Rect(title, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.18f, 0.22f, 0.25f, 0.95f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1, 0.78f, 0.58f); colors.pressedColor = new Color(0.8f, 0.5f, 0.3f); colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.65f); button.colors = colors;
            var text = Label(rect, title, 21, new Vector2(14, 0), new Vector2(size.x - 28, size.y), Color.white); text.alignment = TextAnchor.MiddleLeft; return button;
        }
        Text Label(Transform parent, string text, int size, Vector2 position, Vector2 dimensions, Color color)
        {
            var rect = Rect("Label", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), position, dimensions);
            var label = rect.gameObject.AddComponent<Text>(); label.font = font; label.text = text; label.fontSize = size; label.color = color; label.raycastTarget = false; label.alignment = TextAnchor.MiddleLeft; return label;
        }
        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2? dimensions = null)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = dimensions ?? Vector2.zero; return rect; }
        void OnDestroy() { RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering; if (font != null) Destroy(font); }
    }
    public sealed class BarrackInputSurface : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public BarrackPreview host;
        bool dragged,pressed;
        int pointerId;
        BarrackInteraction pressedInteraction;
        bool Owns(PointerEventData data)=>pressed&&data.pointerId==pointerId&&data.button==PointerEventData.InputButton.Left&&host.Ready&&!host.Switching&&host.Interaction==pressedInteraction;
        public void OnPointerDown(PointerEventData data)
        {
            if(data.button!=PointerEventData.InputButton.Left)return;
            pressed=host.Ready&&!host.Switching&&host.Interaction.CanInteract;dragged=false;pointerId=data.pointerId;pressedInteraction=host.Interaction;
        }
        public void OnPointerClick(PointerEventData data)
        {
            if(!Owns(data))return;
            bool touch=!dragged;pressed=false;
            if(touch)host.TryTouch(data.position);
        }
        public void OnBeginDrag(PointerEventData data)
        {if(!Owns(data))return;dragged=true;data.eligibleForClick=false;host.Interaction.BeginOrbit();}
        public void OnDrag(PointerEventData data)
        {if(Owns(data)&&dragged&&host.Interaction.Mode==BarrackInteractionMode.Orbit)host.CameraRig.Orbit(data.delta);}
        public void OnEndDrag(PointerEventData data){if(data.pointerId==pointerId){pressed=false;dragged=true;}}
        public void OnScroll(PointerEventData data)
        {if(host.Ready&&!host.Switching&&data.scrollDelta.y!=0&&host.Interaction.BeginOrbit()){dragged=true;host.CameraRig.Zoom(data.scrollDelta.y);}}
        void OnDisable(){pressed=false;dragged=false;pressedInteraction=null;}
    }
}


using System;
using System.Threading.Tasks;
using Game.Core;
using Game.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate
{
    public sealed class DemoView : UiFormLogic
    {
        readonly GameRuntime game;
        readonly DemoServices demo;
        Text diagnostics;
        float nextDiagnostics;
        public RectTransform Panel { get; private set; }
        public Text Notice { get; private set; }
        static readonly Color Background = new Color(0.035f, 0.055f, 0.09f);
        public static readonly Color Accent = new Color(0.25f, 0.92f, 0.8f);
        public sealed class Content { public string Title, Subtitle; }
        public int Covers { get; private set; }
        public int Reveals { get; private set; }
        public Task SettingsTask { get; private set; } = Task.CompletedTask;
        public DemoView(DemoContext context) { game = context.Game; demo = context.Demo; }
        public override void OnCover() { Covers++; }
        public override void OnReveal() { Reveals++; }
        public override Task OnOpenAsync(object data) { var content = (Content)data; Build(content.Title, content.Subtitle); return Task.CompletedTask; }
        public void Button(string label, Rect rect, Action clicked, bool primary = false) =>
            game.Ui.Button(Panel, label, rect, () => { if (Scope.IsActive && !Form.IsCovered) clicked(); }, primary);
        public void UpdateDiagnostics(string procedure)
        {
            if (Time.unscaledTime < nextDiagnostics) return;
            nextDiagnostics = Time.unscaledTime + 0.2f;
            diagnostics.text = "BUILD " + game.Version + " / " + game.Scenes.Current +
                "\nFLOW " + procedure + " / TASKS " + Scope.PendingCount +
                "\nENTITIES " + game.Entities.ActiveCount + " active / " + game.Entities.InstanceCount + " pooled" +
                "\nASSETS " + game.Resources.ReferenceCount + " leases / EVENTS " + game.Events.SubscriptionCount + " / UI " + game.Ui.OpenCount;
        }
        public static Camera CreateWorld(Material orbMaterial)
        {
            var cameraObject = new GameObject("WorldCamera", typeof(Camera), typeof(AudioListener));
            var worldCamera = cameraObject.GetComponent<Camera>(); worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = Background; worldCamera.orthographic = true; worldCamera.orthographicSize = 5.4f;
            worldCamera.rect = new Rect(0.34f, 0, 0.66f, 1); worldCamera.transform.position = new Vector3(0, 8, -7);
            worldCamera.transform.LookAt(Vector3.zero);
            var light = new GameObject("KeyLight", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientLight = new Color(0.35f, 0.45f, 0.55f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "ArenaFloor";
            floor.transform.position = new Vector3(0, -0.7f, 0); floor.transform.localScale = new Vector3(11, 0.2f, 8);
            // Use a scene-owned material, released on scene destruction through this component.
            var material = new Material(orbMaterial); material.DisableKeyword("_EMISSION"); material.color = new Color(0.08f, 0.14f, 0.2f);
            floor.GetComponent<Renderer>().sharedMaterial = material; floor.AddComponent<OwnedMaterial>().Value = material;
            return worldCamera;
        }
        public void Build(string title, string subtitle)
        {
            var root = Form.Root;
            Panel = game.Ui.Panel(root, "Sidebar", new Rect(0, 0, 430, 720), Background);
            game.Ui.Label(Panel, "NOVA / FRAMEWORK LAB", new Rect(32, 26, 360, 30), 16, Accent);
            game.Ui.Label(Panel, title, new Rect(32, 78, 360, 110), 45);
            game.Ui.Label(Panel, subtitle, new Rect(32, 190, 360, 72), 17, new Color(0.6f, 0.72f, 0.8f));
            game.Ui.Panel(Panel, "Divider", new Rect(32, 281, 360, 2), new Color(0.15f, 0.25f, 0.32f));
            Notice = game.Ui.Label(Panel, "", new Rect(32, 510, 360, 70), 15, Accent);
            diagnostics = game.Ui.Label(Panel, "", new Rect(32, 592, 365, 112), 13, new Color(0.5f, 0.65f, 0.73f));
            game.Ui.Label(root, DemoController.CodeFlavor, new Rect(474, 28, 640, 35), 18, Accent);
        }
        public void OpenSettings()
        {
            if (!Scope.IsActive || !SettingsTask.IsCompleted || game.Ui.TryGet("Demo.Settings", out _)) return;
            SettingsTask = Scope.Run(async token =>
            {
                await game.Ui.OpenFormAsync("Demo.Settings", UiLayer.Popup, Scope, () => new SettingsForm(game, demo), prefab: "ui-shell");
            });
        }
        sealed class SettingsForm : UiFormLogic
        {
            readonly GameRuntime game; readonly DemoServices demo;
            public SettingsForm(GameRuntime game, DemoServices demo) { this.game = game; this.demo = demo; }
            public override async Task OnOpenAsync(object data)
            {
                var root = Form.Root;
                game.Ui.Panel(root, "Shade", new Rect(0, 0, 1280, 720), new Color(0, 0, 0, 0.75f));
                var panel = game.Ui.Panel(root, "SettingsPanel", new Rect(410, 170, 460, 340), new Color(0.055f, 0.09f, 0.14f));
                game.Ui.Label(panel, "AUDIO SETTINGS", new Rect(32, 28, 396, 48), 30, Accent);
                game.Ui.Label(panel, "Volume / changes are saved immediately", new Rect(32, 100, 396, 45), 16);
                game.Ui.Slider(panel, new Rect(32, 158, 396, 24), demo.Save.volume, volume => { if (Scope.IsActive) demo.SaveSettings(volume, demo.Save.mute); });
                Button mute = null;
                mute = game.Ui.Button(panel, demo.Save.mute ? "UNMUTE" : "MUTE", new Rect(32, 220, 185, 48), () =>
                {
                    if (!Scope.IsActive) return; demo.SaveSettings(demo.Save.volume, !demo.Save.mute);
                    mute.GetComponentInChildren<Text>().text = demo.Save.mute ? "UNMUTE" : "MUTE";
                });
                game.Ui.Button(panel, "CLOSE", new Rect(243, 220, 185, 48), () => Form.RequestClose(), true);
                await game.Audio.PlayAsync("chime", "UI", Scope);
            }
        }
    }
    public sealed class OwnedMaterial : MonoBehaviour
    {
        public Material Value;
        void OnDestroy() { if (Value != null) Destroy(Value); }
    }
}

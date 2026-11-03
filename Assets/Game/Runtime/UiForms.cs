using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Runtime
{
    public abstract class UiFormLogic
    {
        public UiFormHandle Form { get; internal set; }
        public StateScope Scope => Form.Scope;
        public virtual Task OnOpenAsync(object data) => Task.CompletedTask;
        public virtual Task OnCloseAsync() => Task.CompletedTask;
        public virtual void OnCover() { }
        public virtual void OnReveal() { }
    }
    public sealed class UiFormHandle
    {
        readonly UiService service;
        internal readonly TaskCompletionSource<bool> opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task closing;
        internal AssetLease<GameObject> lease;
        internal GameObject instance;
        internal IDisposable ownership;
        internal bool shown, covered;
        internal readonly bool blocksBelow;
        public string Id { get; }
        public UiLayer Layer { get; }
        public int Serial { get; }
        public RectTransform Root { get; internal set; }
        public UiFormLogic Logic { get; internal set; }
        public StateScope Scope { get; }
        public bool IsOpen => shown && closing == null && Scope.IsActive;
        public bool IsCovered => covered;
        internal UiFormHandle(UiService service, string id, UiLayer layer, int serial, StateScope scope, bool blocksBelow)
        { this.service = service; Id = id; Layer = layer; Serial = serial; Scope = scope; this.blocksBelow = blocksBelow; }
        public Task CloseAsync() => service.CloseAsync(this);
        public void RequestClose() => service.RequestClose(this);
    }
    public sealed partial class UiService : IModule
    {
        readonly Transform root;
        readonly ResourceService resources;
        readonly IGameLog log;
        readonly Font font;
        GameObject staging;
        readonly Dictionary<string, UiFormHandle> forms = new Dictionary<string, UiFormHandle>();
        readonly HashSet<Task> operations = new HashSet<Task>();
        Task stopTask;
        bool stopped;
        bool refreshing, stackDirty;
        int serial;
        public string Id => "ui";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "resources", "presentation" };
        public int OpenCount => forms.Count;
        public UiService(Transform root, ResourceService resources, IGameLog log)
        { this.root = root; this.resources = resources; this.log = log; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        public bool TryGet(string id, out UiFormHandle form) => forms.TryGetValue(id, out form);
        public Task InitializeAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            staging = new GameObject("UI staging"); staging.SetActive(false); staging.transform.SetParent(root, false);
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken cancellation) => Task.CompletedTask;
        public Task<UiFormHandle> OpenFormAsync(string id, UiLayer layer, StateScope owner, Func<UiFormLogic> factory,
            object data = null, string prefab = null, bool blocksBelow = true)
        {
            if (stopped || !owner.IsActive) throw new InvalidOperationException("UI owner is closing");
            if (string.IsNullOrWhiteSpace(id) || factory == null || forms.ContainsKey(id)) throw new InvalidOperationException("Invalid or duplicate UI form: " + id);
            var handle = new UiFormHandle(this, id, layer, checked(++serial), owner.CreateChild("ui/" + id), blocksBelow);
            forms.Add(id, handle); handle.ownership = owner.OwnAsync(handle.CloseAsync);
            return Track(OpenCoreAsync(handle, factory, data, prefab));
        }
        async Task<UiFormHandle> OpenCoreAsync(UiFormHandle form, Func<UiFormLogic> factory, object data, string prefab)
        {
            Exception failure = null;
            try
            {
                if (prefab != null) form.lease = await resources.LoadAsync<GameObject>(prefab, form.Scope.Token);
                form.Scope.Token.ThrowIfCancellationRequested();
                GameObject view = form.lease == null ? new GameObject(form.Id, typeof(RectTransform)) : UnityEngine.Object.Instantiate(form.lease.Asset, staging.transform, false);
                form.instance = view;
                view.SetActive(false); view.name = form.Id; view.transform.SetParent(root, false);
                form.Root = view.transform as RectTransform ?? throw new InvalidOperationException("UI prefab requires RectTransform");
                // Unity's missing components use a native null sentinel in Editor; do not use ?? here.
                var canvas = view.GetComponent<Canvas>(); if (canvas == null) canvas = view.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = view.GetComponent<CanvasScaler>(); if (scaler == null) scaler = view.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
                if (view.GetComponent<GraphicRaycaster>() == null) view.AddComponent<GraphicRaycaster>();
                if (view.GetComponent<CanvasGroup>() == null) view.AddComponent<CanvasGroup>();
                form.Logic = factory() ?? throw new InvalidOperationException("Null UI logic"); form.Logic.Form = form;
                await form.Scope.Run(_ => form.Logic.OnOpenAsync(data)); form.Scope.Token.ThrowIfCancellationRequested();
                form.shown = true; view.SetActive(true); RefreshStack();
                return form;
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                form.opened.TrySetResult(true);
                if (failure != null) await CloseAsync(form);
            }
        }
        public Task CloseAsync(UiFormHandle form)
        {
            form.Scope.EnsureCanDispose();
            return BeginClose(form);
        }
        Task BeginClose(UiFormHandle form)
        {
            if (!forms.TryGetValue(form.Id, out var current) || current != form) return Task.CompletedTask;
            if (form.closing != null) return form.closing;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            form.closing = completion.Task; form.Scope.Cancel();
            if (form.Root != null) form.Root.gameObject.SetActive(false);
            StateScope.Drive(() => CloseCoreAsync(form, completion)); Track(completion.Task); return completion.Task;
        }
        public void RequestClose(UiFormHandle form) { _ = Observe(BeginClose(form)); }
        async Task Observe(Task task) { try { await task; } catch (Exception e) { log.Write(GameLogLevel.Error, "ui", "ui.close.failed", e.Message, exception: e); } }
        async Task CloseCoreAsync(UiFormHandle form, TaskCompletionSource<bool> completion)
        {
            await Task.Yield(); var errors = new List<Exception>();
            await form.opened.Task;
            try { await form.Scope.DisposeAsync(); } catch (Exception e) { errors.Add(e); }
            try { if (form.Logic != null) await form.Logic.OnCloseAsync(); } catch (Exception e) { errors.Add(e); }
            if (form.instance != null) UnityEngine.Object.Destroy(form.instance);
            form.lease?.Dispose(); form.lease = null; form.shown = false; forms.Remove(form.Id);
            form.ownership.Dispose();
            try { RefreshStack(); } catch (Exception e) { errors.Add(e); }
            if (errors.Count == 0) completion.TrySetResult(true);
            else completion.TrySetException(new AggregateException("UI close failed: " + form.Id, errors));
        }
        void RefreshStack()
        {
            stackDirty = true;
            if (refreshing) return;
            refreshing = true;
            try
            {
            while (stackDirty)
            {
            stackDirty = false;
            bool blocked = false; int depth = forms.Count;
            foreach (var form in forms.Values.Where(f => f.IsOpen).OrderByDescending(f => f.Layer).ThenByDescending(f => f.Serial).ToArray())
            {
                if (!form.IsOpen) continue;
                form.Root.GetComponent<Canvas>().sortingOrder = depth--;
                var group = form.Root.GetComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = !blocked;
                if (form.covered != blocked)
                {
                    form.covered = blocked;
                    try { if (blocked) form.Logic.OnCover(); else form.Logic.OnReveal(); }
                    catch (Exception e) { log.Write(GameLogLevel.Error, "ui", "ui.cover.failed", form.Id, exception: e); }
                }
                blocked |= form.blocksBelow;
            }
            }
            }
            finally { refreshing = false; }
        }
        Task<T> Track<T>(Task<T> task) { Track((Task)task); return task; }
        void Track(Task task)
        {
            operations.Add(task); _ = FinishTracking(task);
        }
        async Task FinishTracking(Task task) { try { await task; } catch { } finally { operations.Remove(task); } }
        public async Task WhenIdleAsync() { while (operations.Count > 0) await Task.WhenAll(operations.ToArray()); }
        public Task StopAsync(CancellationToken cancellation)
        {
            if (stopTask != null) return stopTask;
            stopped = true; stopTask = StopCoreAsync(); return stopTask;
        }
        async Task StopCoreAsync()
        {
            var closing = forms.Values.ToArray().Select(CloseAsync).ToArray();
            var errors = new List<Exception>();
            try { await Task.WhenAll(closing); } catch (Exception e) { errors.Add(e); }
            try { await WhenIdleAsync(); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 0) throw new AggregateException("UI shutdown failed", errors);
        }
        public void Dispose()
        {
            if (forms.Count != 0 || operations.Count != 0) throw new InvalidOperationException("UI must stop before disposal");
            if (staging != null) { UnityEngine.Object.Destroy(staging); staging = null; }
        }
    }
}

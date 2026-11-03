using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

    // {{ AOT assemblies
    public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
    {
        "Game.Core.dll",
        "Game.Runtime.dll",
        "System.Core.dll",
        "UnityEngine.CoreModule.dll",
        "UnityEngine.JSONSerializeModule.dll",
        "mscorlib.dll",
    };
    // }}

    // {{ constraint implement type
    // }} 

    // {{ AOT generic types
    // Game.Core.EventBus.<>c__DisplayClass3_0<Game.HotUpdate.EnergyCollected>
    // Game.Core.Fsm.<>c__DisplayClass22_0<object,object>
    // Game.Core.StateScope.<>c__DisplayClass29_0<object>
    // Game.Core.StateScope.<>c__DisplayClass31_0<Game.HotUpdate.EnergyCollected>
    // Game.Runtime.AssetLease<object>
    // System.Action<Game.HotUpdate.EnergyCollected>
    // System.Action<System.Threading.AsyncLocalValueChangedArgs<object>>
    // System.Action<float>
    // System.Action<object,object>
    // System.Action<object>
    // System.Collections.Generic.ArraySortHelper<object>
    // System.Collections.Generic.Comparer<object>
    // System.Collections.Generic.Dictionary.Enumerator<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.Dictionary.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.KeyCollection<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.Dictionary.KeyCollection<int,object>
    // System.Collections.Generic.Dictionary.KeyCollection<object,int>
    // System.Collections.Generic.Dictionary.KeyCollection<object,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,int>
    // System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
    // System.Collections.Generic.Dictionary.ValueCollection<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.Dictionary.ValueCollection<int,object>
    // System.Collections.Generic.Dictionary.ValueCollection<object,int>
    // System.Collections.Generic.Dictionary.ValueCollection<object,object>
    // System.Collections.Generic.Dictionary<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.Dictionary<int,object>
    // System.Collections.Generic.Dictionary<object,int>
    // System.Collections.Generic.Dictionary<object,object>
    // System.Collections.Generic.EqualityComparer<System.ValueTuple<object,object>>
    // System.Collections.Generic.EqualityComparer<int>
    // System.Collections.Generic.EqualityComparer<object>
    // System.Collections.Generic.HashSet.Enumerator<object>
    // System.Collections.Generic.HashSet<object>
    // System.Collections.Generic.HashSetEqualityComparer<object>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.ICollection<object>
    // System.Collections.Generic.IComparer<object>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IEnumerable<int>
    // System.Collections.Generic.IEnumerable<object>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,int>>
    // System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
    // System.Collections.Generic.IEnumerator<int>
    // System.Collections.Generic.IEnumerator<object>
    // System.Collections.Generic.IEqualityComparer<System.ValueTuple<object,object>>
    // System.Collections.Generic.IEqualityComparer<int>
    // System.Collections.Generic.IEqualityComparer<object>
    // System.Collections.Generic.IList<object>
    // System.Collections.Generic.IReadOnlyDictionary<object,int>
    // System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>
    // System.Collections.Generic.KeyValuePair<int,object>
    // System.Collections.Generic.KeyValuePair<object,int>
    // System.Collections.Generic.KeyValuePair<object,object>
    // System.Collections.Generic.List.Enumerator<object>
    // System.Collections.Generic.List<object>
    // System.Collections.Generic.LowLevelList<object>
    // System.Collections.Generic.LowLevelListWithIList.Enumerator<object>
    // System.Collections.Generic.LowLevelListWithIList<object>
    // System.Collections.Generic.ObjectComparer<object>
    // System.Collections.Generic.ObjectEqualityComparer<System.ValueTuple<object,object>>
    // System.Collections.Generic.ObjectEqualityComparer<int>
    // System.Collections.Generic.ObjectEqualityComparer<object>
    // System.Collections.Generic.Stack.Enumerator<object>
    // System.Collections.Generic.Stack<object>
    // System.Collections.ObjectModel.ReadOnlyCollection<object>
    // System.Comparison<object>
    // System.Func<System.Threading.CancellationToken,object>
    // System.Func<System.Threading.Tasks.VoidTaskResult>
    // System.Func<byte>
    // System.Func<double>
    // System.Func<int,byte>
    // System.Func<int>
    // System.Func<object,System.Threading.Tasks.VoidTaskResult>
    // System.Func<object,byte>
    // System.Func<object,double>
    // System.Func<object,int>
    // System.Func<object,object,object>
    // System.Func<object,object>
    // System.Func<object>
    // System.Nullable<UnityEngine.Color>
    // System.Predicate<object>
    // System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>
    // System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<byte>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<int>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<object>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<byte>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<int>
    // System.Runtime.CompilerServices.ConfiguredTaskAwaitable<object>
    // System.Runtime.CompilerServices.TaskAwaiter<System.Threading.Tasks.VoidTaskResult>
    // System.Runtime.CompilerServices.TaskAwaiter<byte>
    // System.Runtime.CompilerServices.TaskAwaiter<int>
    // System.Runtime.CompilerServices.TaskAwaiter<object>
    // System.Threading.AsyncLocal<object>
    // System.Threading.AsyncLocalValueChangedArgs<object>
    // System.Threading.Tasks.ContinuationTaskFromResultTask<System.Threading.Tasks.VoidTaskResult>
    // System.Threading.Tasks.ContinuationTaskFromResultTask<byte>
    // System.Threading.Tasks.ContinuationTaskFromResultTask<int>
    // System.Threading.Tasks.ContinuationTaskFromResultTask<object>
    // System.Threading.Tasks.Task.WhenAllPromise<int>
    // System.Threading.Tasks.Task<System.Threading.Tasks.VoidTaskResult>
    // System.Threading.Tasks.Task<byte>
    // System.Threading.Tasks.Task<int>
    // System.Threading.Tasks.Task<object>
    // System.Threading.Tasks.TaskCompletionSource<byte>
    // System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<System.Threading.Tasks.VoidTaskResult>
    // System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<byte>
    // System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<int>
    // System.Threading.Tasks.TaskFactory.<>c__DisplayClass35_0<object>
    // System.Threading.Tasks.TaskFactory<System.Threading.Tasks.VoidTaskResult>
    // System.Threading.Tasks.TaskFactory<byte>
    // System.Threading.Tasks.TaskFactory<int>
    // System.Threading.Tasks.TaskFactory<object>
    // }}

    public void RefMethods()
    {
        // System.Threading.Tasks.Task<Game.Core.EntityHandle<object>> Game.Core.EntityOwner<object>.ShowAsync<object>(int,string,string,System.Func<object>,object,System.Threading.CancellationToken)
        // System.Void Game.Core.EventBus.Publish<Game.HotUpdate.EnergyCollected>(Game.HotUpdate.EnergyCollected)
        // System.IDisposable Game.Core.EventBus.Subscribe<Game.HotUpdate.EnergyCollected>(System.Action<Game.HotUpdate.EnergyCollected>)
        // System.Threading.Tasks.Task Game.Core.Fsm<object>.BeginChange<object>(System.Threading.CancellationToken)
        // System.Threading.Tasks.Task Game.Core.Fsm<object>.ChangeAsync<object>(System.Threading.CancellationToken)
        // bool Game.Core.Fsm<object>.RequestChange<object>(Game.Core.StateScope)
        // System.Threading.Tasks.Task Game.Core.ProcedureManager.ChangeAsync<object>(System.Threading.CancellationToken)
        // bool Game.Core.ProcedureManager.RequestChange<object>(Game.Core.StateScope)
        // object Game.Core.StateScope.Own<object>(object)
        // System.IDisposable Game.Core.StateScope.Subscribe<Game.HotUpdate.EnergyCollected>(Game.Core.EventBus,System.Action<Game.HotUpdate.EnergyCollected>)
        // System.Threading.Tasks.Task<Game.Runtime.AssetLease<object>> Game.Runtime.ResourceService.LoadAsync<object>(string,System.Threading.CancellationToken)
        // int[] System.Array.Empty<int>()
        // bool System.Linq.Enumerable.All<int>(System.Collections.Generic.IEnumerable<int>,System.Func<int,bool>)
        // bool System.Linq.Enumerable.All<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
        // bool System.Linq.Enumerable.Any<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
        // bool System.Linq.Enumerable.Contains<object>(System.Collections.Generic.IEnumerable<object>,object)
        // bool System.Linq.Enumerable.Contains<object>(System.Collections.Generic.IEnumerable<object>,object,System.Collections.Generic.IEqualityComparer<object>)
        // object System.Linq.Enumerable.Single<object>(System.Collections.Generic.IEnumerable<object>)
        // object System.Linq.Enumerable.Single<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.DemoController.<InitializeAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.DemoController.<InitializeAsync>d__6&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.DemoController.<StopAsync>d__12>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.DemoController.<StopAsync>d__12&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.GameEntry.<StartAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.GameEntry.<StartAsync>d__6&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>g__ReleaseNextFrame|1>d>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>g__ReleaseNextFrame|1>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ServicesValidation.ProbeForm.<OnOpenAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ServicesValidation.ProbeForm.<OnOpenAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ShutdownProbe.<StopAsync>d__10>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ShutdownProbe.<StopAsync>d__10&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__4>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__4>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__5>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__5>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>b__3>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>b__3>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.ResultProcedure.<>c__DisplayClass5_0.<<EnterAsync>b__1>d>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.ResultProcedure.<>c__DisplayClass5_0.<<EnterAsync>b__1>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoController.<ObserveInteractiveCheckAsync>d__9>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoController.<ObserveInteractiveCheckAsync>d__9&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoProcedure.<OpenViewAsync>d__18>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoProcedure.<OpenViewAsync>d__18&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoServices.<InitializeAsync>d__28>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoServices.<InitializeAsync>d__28&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoView.<<OpenSettings>b__35_0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoView.<<OpenSettings>b__35_0>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoView.SettingsForm.<OnOpenAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoView.SettingsForm.<OnOpenAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.EnergyOrbLogic.<>c__DisplayClass5_0.<<OnInteract>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.EnergyOrbLogic.<>c__DisplayClass5_0.<<OnInteract>b__0>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.LobbyProcedure.<>c__DisplayClass5_1.<<EnterAsync>b__3>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.LobbyProcedure.<>c__DisplayClass5_1.<<EnterAsync>b__3>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.DemoController.<InitializeAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.DemoController.<InitializeAsync>d__6&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.DemoController.<StopAsync>d__12>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.DemoController.<StopAsync>d__12&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.GameEntry.<StartAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.GameEntry.<StartAsync>d__6&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>g__ReleaseNextFrame|1>d>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>g__ReleaseNextFrame|1>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ServicesValidation.ProbeForm.<OnOpenAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ServicesValidation.ProbeForm.<OnOpenAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ShutdownProbe.<StopAsync>d__10>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ShutdownProbe.<StopAsync>d__10&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__4>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__4>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__5>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__5>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>b__3>d>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>b__3>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.ResultProcedure.<>c__DisplayClass5_0.<<EnterAsync>b__1>d>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.ResultProcedure.<>c__DisplayClass5_0.<<EnterAsync>b__1>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.BattleProcedure.<EnterAsync>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoController.<ObserveInteractiveCheckAsync>d__9>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoController.<ObserveInteractiveCheckAsync>d__9&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoProcedure.<OpenViewAsync>d__18>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoProcedure.<OpenViewAsync>d__18&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoServices.<InitializeAsync>d__28>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoServices.<InitializeAsync>d__28&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoView.<<OpenSettings>b__35_0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoView.<<OpenSettings>b__35_0>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoView.SettingsForm.<OnOpenAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoView.SettingsForm.<OnOpenAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.EnergyOrbLogic.<>c__DisplayClass5_0.<<OnInteract>b__0>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.EnergyOrbLogic.<>c__DisplayClass5_0.<<OnInteract>b__0>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.LobbyProcedure.<>c__DisplayClass5_1.<<EnterAsync>b__3>d>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.LobbyProcedure.<>c__DisplayClass5_1.<<EnterAsync>b__3>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ResultProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,Game.HotUpdate.WorldValidation.<RunAsync>d__1>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.MockScoreTransport.<SubmitAsync>d__5>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.MockScoreTransport.<SubmitAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.ScoreClient.<SubmitAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.ScoreClient.<SubmitAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.ScoreClient.<SubmitAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.ScoreClient.<SubmitAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,Game.HotUpdate.DemoValidation.<RunAsync>d__0>(System.Runtime.CompilerServices.TaskAwaiter&,Game.HotUpdate.DemoValidation.<RunAsync>d__0&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>,Game.HotUpdate.DemoValidation.<RunAsync>d__0>(System.Runtime.CompilerServices.TaskAwaiter<int>&,Game.HotUpdate.DemoValidation.<RunAsync>d__0&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoController.<ValidateCoreAsync>d__11>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoController.<ValidateCoreAsync>d__11&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,Game.HotUpdate.DemoValidation.<RunAsync>d__0>(System.Runtime.CompilerServices.TaskAwaiter<object>&,Game.HotUpdate.DemoValidation.<RunAsync>d__0&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,Game.HotUpdate.DemoValidation.<RunAsync>d__0>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,Game.HotUpdate.DemoValidation.<RunAsync>d__0&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.BattleProcedure.<EnterAsync>d__14>(Game.HotUpdate.BattleProcedure.<EnterAsync>d__14&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoController.<InitializeAsync>d__6>(Game.HotUpdate.DemoController.<InitializeAsync>d__6&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoController.<ObserveInteractiveCheckAsync>d__9>(Game.HotUpdate.DemoController.<ObserveInteractiveCheckAsync>d__9&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoController.<StopAsync>d__12>(Game.HotUpdate.DemoController.<StopAsync>d__12&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoProcedure.<OpenViewAsync>d__18>(Game.HotUpdate.DemoProcedure.<OpenViewAsync>d__18&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoServices.<InitializeAsync>d__28>(Game.HotUpdate.DemoServices.<InitializeAsync>d__28&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__4>d>(Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__4>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__5>d>(Game.HotUpdate.DemoValidation.<>c__DisplayClass0_0.<<RunAsync>b__5>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoView.<<OpenSettings>b__35_0>d>(Game.HotUpdate.DemoView.<<OpenSettings>b__35_0>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.DemoView.SettingsForm.<OnOpenAsync>d__3>(Game.HotUpdate.DemoView.SettingsForm.<OnOpenAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.EnergyOrbLogic.<>c__DisplayClass5_0.<<OnInteract>b__0>d>(Game.HotUpdate.EnergyOrbLogic.<>c__DisplayClass5_0.<<OnInteract>b__0>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.GameEntry.<StartAsync>d__6>(Game.HotUpdate.GameEntry.<StartAsync>d__6&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.LobbyProcedure.<>c__DisplayClass5_1.<<EnterAsync>b__3>d>(Game.HotUpdate.LobbyProcedure.<>c__DisplayClass5_1.<<EnterAsync>b__3>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5>(Game.HotUpdate.LobbyProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ResultProcedure.<>c__DisplayClass5_0.<<EnterAsync>b__1>d>(Game.HotUpdate.ResultProcedure.<>c__DisplayClass5_0.<<EnterAsync>b__1>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ResultProcedure.<EnterAsync>d__5>(Game.HotUpdate.ResultProcedure.<EnterAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>b__3>d>(Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>b__3>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>g__ReleaseNextFrame|1>d>(Game.HotUpdate.ServicesValidation.<>c__DisplayClass1_0.<<RunAsync>g__ReleaseNextFrame|1>d&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ServicesValidation.<RunAsync>d__1>(Game.HotUpdate.ServicesValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ServicesValidation.ProbeForm.<OnOpenAsync>d__3>(Game.HotUpdate.ServicesValidation.ProbeForm.<OnOpenAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.ShutdownProbe.<StopAsync>d__10>(Game.HotUpdate.ShutdownProbe.<StopAsync>d__10&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<Game.HotUpdate.WorldValidation.<RunAsync>d__1>(Game.HotUpdate.WorldValidation.<RunAsync>d__1&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.Start<Game.HotUpdate.MockScoreTransport.<SubmitAsync>d__5>(Game.HotUpdate.MockScoreTransport.<SubmitAsync>d__5&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<int>.Start<Game.HotUpdate.ScoreClient.<SubmitAsync>d__3>(Game.HotUpdate.ScoreClient.<SubmitAsync>d__3&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.Core.EntityOwner.<ShowAsync>d__10<object,object>>(Game.Core.EntityOwner.<ShowAsync>d__10<object,object>&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.DemoController.<ValidateCoreAsync>d__11>(Game.HotUpdate.DemoController.<ValidateCoreAsync>d__11&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.HotUpdate.DemoValidation.<RunAsync>d__0>(Game.HotUpdate.DemoValidation.<RunAsync>d__0&)
        // System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<Game.Runtime.ResourceService.<LoadAsync>d__31<object>>(Game.Runtime.ResourceService.<LoadAsync>d__31<object>&)
        // System.Threading.Tasks.Task<int[]> System.Threading.Tasks.Task.InternalWhenAll<int>(System.Threading.Tasks.Task<int>[])
        // System.Threading.Tasks.Task<int[]> System.Threading.Tasks.Task.WhenAll<int>(System.Threading.Tasks.Task<int>[])
        // object UnityEngine.Component.GetComponentInChildren<object>()
        // object[] UnityEngine.Component.GetComponentsInChildren<object>()
        // object[] UnityEngine.Component.GetComponentsInChildren<object>(bool)
        // object UnityEngine.GameObject.AddComponent<object>()
        // object UnityEngine.GameObject.GetComponent<object>()
        // object[] UnityEngine.GameObject.GetComponentsInChildren<object>(bool)
        // object UnityEngine.JsonUtility.FromJson<object>(string)
    }
}

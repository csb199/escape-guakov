using System;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackReloadRecovery
    {
        const string ResumeKey="Barrack.Reload.Resume",CharacterKey="Barrack.Reload.Character";
        static bool stopping,locked,waitingForReload,restoring;
        static BarrackReloadRecovery()
        {
            CompilationPipeline.compilationStarted+=OnCompilationStarted;
            EditorApplication.playModeStateChanged+=OnPlayModeChanged;
            EditorApplication.update+=Tick;
        }
        static void OnCompilationStarted(object context)
        {
            if(!EditorApplication.isPlaying)return;
            var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
            if(host==null)return;
            waitingForReload=true;
            EditorApplication.LockReloadAssemblies();locked=true;
            Stop(host);
        }
        static async void Stop(BarrackPreview host)
        {
            if(stopping)return;
            stopping=true;
            SessionState.SetString(CharacterKey,host.CharacterId??(host.SelectedCharacter>=0&&host.SelectedCharacter<host.characters.Length?host.characters[host.SelectedCharacter].id:""));
            SessionState.SetBool(ResumeKey,true);
            try { await host.StopAsync(); }
            catch(Exception exception){Debug.LogException(exception);SessionState.SetBool(ResumeKey,false);}
            finally { EditorApplication.isPlaying=false; }
        }
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode)return;
            stopping=false;
            if(locked){locked=false;EditorApplication.UnlockReloadAssemblies();}
        }
        static async void Tick()
        {
            if(stopping||restoring||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            var host=EditorApplication.isPlaying?UnityEngine.Object.FindFirstObjectByType<BarrackPreview>():null;
            if(host!=null&&host.Ready&&host.Interaction==null){Stop(host);return;}
            if(waitingForReload||!SessionState.GetBool(ResumeKey,false))return;
            if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.isPlaying=true;return;}
            if(host==null||!host.Ready)return;
            restoring=true;
            SessionState.SetBool(ResumeKey,false);
            try
            {
                string character=SessionState.GetString(CharacterKey,"");
                int index=Array.FindIndex(host.characters,entry=>entry.id==character);
                if(index>=0)await host.SelectCharacterAsync(index);
            }
            catch(Exception exception){Debug.LogException(exception);}
            finally {restoring=false;}
        }
    }
}

using UnityEditor;
using UnityEngine;

// Oyun calisirken bir script degisirse Unity varsayilan olarak derleyip assembly'leri YENIDEN YUKLER
// ("Recompile And Continue Playing"). Mirror ve Steam statik durumu bunu kaldiramaz: sunucu thread'i
// olur, NetworkServer kapanir, Steam callback'leri her kare istisna atar ("Callback dispatcher is not
// initialized"), esya alinamaz, Ayril "NetworkServer.Destroy() called without an active server" verir.
// Bu koruma Play modunda yeniden yuklemeyi kilitler; degisiklikler Play'den cikinca uygulanir.
[InitializeOnLoad]
static class PlayModeReloadGuard
{
    static bool locked;

    static PlayModeReloadGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (EditorApplication.isPlaying) Lock(); // Play'e girerken yapilan yuklemeden sonra
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) Lock();
        else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) Unlock();
    }

    static void Lock()
    {
        if (locked) return;
        locked = true;
        EditorApplication.LockReloadAssemblies();
        Debug.Log("[ReloadGuard] Play modunda script yeniden yuklemesi kilitli; degisiklikler Play bitince uygulanir.");
    }

    static void Unlock()
    {
        if (!locked) return;
        locked = false;
        EditorApplication.UnlockReloadAssemblies();
    }
}

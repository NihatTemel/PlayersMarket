#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Gelistirme kaydi kisayollari (SADECE Editor, build'e girmez): F1 video baslat, F2 durdur, F8 ekran goruntusu.
// (F9 = PlayerInventory gelistirici altini; buraya alma.)
// Kaydi editor tarafi yapar (Assets/Editor/DevRecorder.cs, Unity Recorder paketi); burasi sadece tusu
// oyun dongusunde okur (Game View odakliyken klavye editor'e degil oyuna gider).
public class DevCaptureKeys : MonoBehaviour
{
    public static Action StartVideo;     // DevRecorder baglar
    public static Action StopVideo;
    public static Action TakeScreenshot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("[DevCapture] F1 kayit, F2 durdur, F8 foto");
        DontDestroyOnLoad(go);
        go.AddComponent<DevCaptureKeys>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f1Key.wasPressedThisFrame && StartVideo != null) StartVideo();
        if (kb.f2Key.wasPressedThisFrame && StopVideo != null) StopVideo();
        if (kb.f8Key.wasPressedThisFrame && TakeScreenshot != null) TakeScreenshot();
    }
}
#endif

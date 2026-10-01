using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

// Gelistirme kaydi (Unity Recorder paketi). Play modunda F1 = video baslat, F2 = durdur, F8 = ekran goruntusu
// (tuslar: Assets/Scripts/Dev/DevCaptureKeys.cs). Menu: PlayersMarket > Kayit.
// Dosyalar proje kokunde Recordings/<tarih>/<saat>_<sahne>.mp4|png (git'e girmez); her kayit
// Recordings/KAYITLAR.md tablosuna bir satir ekler ("Not" sutunu elle doldurulur).
// Kare hizi DEGISKEN: sabit modda Recorder Time.captureDeltaTime'i kilitler, FPS dusunce oyun zamani
// gercek zamandan kopar (Mirror/Steam zamanlamasi bozulur). Degiskende oyuna dokunulmaz.
// Recorder kayit boyunca Game View'i kayit cozunurlugune zorlar; kayit bitince eski boyut geri secilir.
// Kayit gostergesi Game View sekme basliginda ("● KAYIT 1:23") ve bildirimde: videoya girmez.
[InitializeOnLoad]
static class DevRecorder
{
    const string PrefAutoStart = "PlayersMarket.DevRecorder.AutoStart";
    const string PrefSmall = "PlayersMarket.DevRecorder.720p";
    const string MenuStart = "PlayersMarket/Kayit/Video Baslat (F1)";
    const string MenuStop = "PlayersMarket/Kayit/Video Durdur (F2)";
    const string MenuShot = "PlayersMarket/Kayit/Ekran Goruntusu (F8)";
    const string MenuFolder = "PlayersMarket/Kayit/Kayit Klasorunu Ac";
    const string MenuAuto = "PlayersMarket/Kayit/Play'e Girince Otomatik Baslat";
    const string MenuSmall = "PlayersMarket/Kayit/720p Kaydet (daha hafif)";

    static RecorderController controller;
    static string currentFile;   // .mp4 dahil tam yol
    static string currentScene;
    static DateTime startedAt;
    static double startTime;
    static int prevSizeIndex = -1;
    static GUIContent prevTitle;
    static int shownSeconds = -1;

    static bool IsRecording => controller != null;

    static DevRecorder()
    {
        DevCaptureKeys.StartVideo = StartRecording;
        DevCaptureKeys.StopVideo = StopRecording;
        DevCaptureKeys.TakeScreenshot = Screenshot;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(PrefAutoStart, false))
            EditorApplication.delayCall += StartRecording;
        else if (state == PlayModeStateChange.ExitingPlayMode)
            StopRecording(); // dosya Play bitmeden kapanmali
    }

    // ---- Video ----

    static void StartRecording()
    {
        if (!EditorApplication.isPlaying || IsRecording) return;
        try
        {
            startedAt = DateTime.Now;
            currentScene = SceneManager.GetActiveScene().name;
            string dir = DayFolder(startedAt);
            string outputBase = Path.Combine(dir, UniqueName(dir, startedAt, currentScene, ".mp4")).Replace('\\', '/');
            bool small = EditorPrefs.GetBool(PrefSmall, false);

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "DevRecorder";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
                Codec = CoreEncoderSettings.OutputCodec.MP4
            };
            movie.CaptureAlpha = false;
            // MP4 kodlayici sadece mono/stereo ses alir; 5.1/7.1'de ses kapatilmazsa kayit hic baslamaz
            bool audioOk = AudioSettings.speakerMode == AudioSpeakerMode.Mono || AudioSettings.speakerMode == AudioSpeakerMode.Stereo;
            movie.CaptureAudio = audioOk;
            if (!audioOk) Debug.LogWarning("[Kayit] Hoparlor modu " + AudioSettings.speakerMode + ": ses kaydedilmeyecek (sadece mono/stereo destekli).");
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = small ? 1280 : 1920,
                OutputHeight = small ? 720 : 1080
            };
            movie.OutputFile = outputBase; // uzantiyi Recorder ekler

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Variable;
            settings.FrameRate = 60f;
            settings.CapFrameRate = false;
            settings.ExitPlayMode = false;

            prevSizeIndex = GetGameViewSizeIndex();
            RecorderOptions.VerboseMode = false;
            controller = new RecorderController(settings);
            controller.PrepareRecording();
            if (!controller.StartRecording())
            {
                Debug.LogError("[Kayit] Recorder baslatilamadi (ayrinti icin ustteki Recorder loglarina bak).");
                controller = null;
                RestoreGameViewSize();
                return;
            }

            currentFile = outputBase + ".mp4";
            startTime = EditorApplication.timeSinceStartup;
            shownSeconds = -1;
            EditorApplication.update -= UpdateIndicator;
            EditorApplication.update += UpdateIndicator;
            Notify("● Kayıt başladı  (F2 durdurur)");
            Debug.Log("[Kayit] Basladi: " + currentFile);
        }
        catch (Exception e)
        {
            Debug.LogError("[Kayit] Baslatilamadi: " + e);
            controller = null;
            RestoreGameViewSize();
        }
    }

    static void StopRecording()
    {
        if (!IsRecording) return;
        string file = currentFile;
        string duration = FormatDuration(EditorApplication.timeSinceStartup - startTime);
        try { controller.StopRecording(); }
        catch (Exception e) { Debug.LogError("[Kayit] Durdurulurken hata: " + e); }
        controller = null;

        EditorApplication.update -= UpdateIndicator;
        RestoreTitle();
        RestoreGameViewSize();

        AppendLog(startedAt, "Video", duration, currentScene, file);
        Notify("■ Kayıt kaydedildi  (" + duration + ")");
        Debug.Log("[Kayit] Kaydedildi (" + duration + "): " + file);
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(file)) Debug.LogWarning("[Kayit] Video dosyasi olusmadi: " + file);
        };
    }

    // ---- Ekran goruntusu ----

    static void Screenshot()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            var now = DateTime.Now;
            string scene = SceneManager.GetActiveScene().name;
            string dir = DayFolder(now);
            string file = Path.Combine(dir, UniqueName(dir, now, scene, ".png") + ".png");
            ScreenCapture.CaptureScreenshot(file); // Game View cozunurlugunde (kare sonunda yazilir)
            AppendLog(now, "Foto", "-", scene, file);
            Notify("Ekran görüntüsü kaydedildi");
            Debug.Log("[Kayit] Foto: " + file);
        }
        catch (Exception e)
        {
            Debug.LogError("[Kayit] Foto alinamadi: " + e);
        }
    }

    // ---- Dosyalar ----

    static string RootFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));

    static string DayFolder(DateTime t)
    {
        string dir = Path.Combine(RootFolder(), t.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // "14-32-05_Game"; ayni saniyede ikinci dosya "_2" alir
    static string UniqueName(string dir, DateTime t, string scene, string ext)
    {
        string name = t.ToString("HH-mm-ss") + "_" + SafeName(scene);
        string result = name;
        for (int i = 2; File.Exists(Path.Combine(dir, result + ext)); i++) result = name + "_" + i;
        return result;
    }

    static string SafeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Sahne";
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // Recordings/KAYITLAR.md: tum kayitlarin tek tablosu (dosya linkleri tiklanabilir)
    static void AppendLog(DateTime t, string kind, string duration, string scene, string fullPath)
    {
        try
        {
            string root = RootFolder();
            Directory.CreateDirectory(root);
            string log = Path.Combine(root, "KAYITLAR.md");
            if (!File.Exists(log))
                File.WriteAllText(log,
                    "# Geliştirme kayıtları\n\n" +
                    "Play modunda F1 = video başlat, F2 = durdur, F8 = ekran görüntüsü. \"Not\" sütununu elle doldurabilirsin.\n\n" +
                    "| Tarih | Saat | Tür | Süre | Sahne | Dosya | Not |\n" +
                    "|---|---|---|---|---|---|---|\n");
            string rel = fullPath.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            File.AppendAllText(log, "| " + t.ToString("yyyy-MM-dd") + " | " + t.ToString("HH:mm") + " | " + kind + " | " +
                duration + " | " + scene + " | [" + Path.GetFileName(fullPath) + "](" + rel.Replace(" ", "%20") + ") | |\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Kayit] KAYITLAR.md yazilamadi: " + e.Message);
        }
    }

    // ---- Game View (gosterge, bildirim, cozunurluk geri alma) ----

    static void UpdateIndicator()
    {
        if (!IsRecording) return;
        int secs = (int)(EditorApplication.timeSinceStartup - startTime);
        if (secs == shownSeconds) return;
        shownSeconds = secs;
        var gv = GetMainGameView();
        if (gv == null) return;
        if (prevTitle == null) prevTitle = new GUIContent(gv.titleContent);
        gv.titleContent = new GUIContent("● KAYIT " + FormatDuration(secs), prevTitle.image);
    }

    static void RestoreTitle()
    {
        var gv = GetMainGameView();
        if (gv != null && prevTitle != null) gv.titleContent = prevTitle;
        prevTitle = null;
    }

    static void Notify(string message)
    {
        var gv = GetMainGameView();
        if (gv != null) gv.ShowNotification(new GUIContent(message), 1.5);
    }

    // Unity'nin ic API'si (PlayModeView/GameView) yansima ile: bulunamazsa sessizce atlanir
    static EditorWindow GetMainGameView()
    {
        try
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            var method = type == null ? null : type.GetMethod("GetMainPlayModeView", BindingFlags.NonPublic | BindingFlags.Static);
            return method == null ? null : method.Invoke(null, null) as EditorWindow;
        }
        catch
        {
            return null;
        }
    }

    static int GetGameViewSizeIndex()
    {
        try
        {
            var gv = GetMainGameView();
            if (gv == null) return -1;
            var prop = gv.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop == null ? -1 : (int)prop.GetValue(gv);
        }
        catch
        {
            return -1;
        }
    }

    static void RestoreGameViewSize()
    {
        int index = prevSizeIndex;
        prevSizeIndex = -1;
        if (index < 0) return;
        try
        {
            var gv = GetMainGameView();
            if (gv == null) return;
            var method = gv.GetType().GetMethod("SizeSelectionCallback", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null) method.Invoke(gv, new object[] { index, null });
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Kayit] Game View boyutu geri alinamadi (elle sec): " + e.Message);
        }
    }

    // ---- Menu ----

    [MenuItem(MenuStart, false, 100)]
    static void MenuStartVideo() => StartRecording();

    [MenuItem(MenuStart, true)]
    static bool MenuStartVideoValidate() => EditorApplication.isPlaying && !IsRecording;

    [MenuItem(MenuStop, false, 101)]
    static void MenuStopVideo() => StopRecording();

    [MenuItem(MenuStop, true)]
    static bool MenuStopVideoValidate() => IsRecording;

    [MenuItem(MenuShot, false, 102)]
    static void MenuScreenshot() => Screenshot();

    [MenuItem(MenuShot, true)]
    static bool MenuScreenshotValidate() => EditorApplication.isPlaying;

    [MenuItem(MenuFolder, false, 103)]
    static void MenuOpenFolder()
    {
        string root = RootFolder();
        Directory.CreateDirectory(root);
        EditorUtility.OpenWithDefaultApp(root);
    }

    [MenuItem(MenuAuto, false, 120)]
    static void MenuAutoStart() => EditorPrefs.SetBool(PrefAutoStart, !EditorPrefs.GetBool(PrefAutoStart, false));

    [MenuItem(MenuAuto, true)]
    static bool MenuAutoStartValidate()
    {
        Menu.SetChecked(MenuAuto, EditorPrefs.GetBool(PrefAutoStart, false));
        return true;
    }

    [MenuItem(MenuSmall, false, 121)]
    static void MenuSmallToggle() => EditorPrefs.SetBool(PrefSmall, !EditorPrefs.GetBool(PrefSmall, false));

    [MenuItem(MenuSmall, true)]
    static bool MenuSmallValidate()
    {
        Menu.SetChecked(MenuSmall, EditorPrefs.GetBool(PrefSmall, false));
        return !IsRecording;
    }
}

// Oyun ici bir panel (envanter, demirci, satici...) acikken: imlec serbest, kamera donmez,
// E/Q/firlatma calismaz. Paneller acilinca Push, kapaninca Pop cagirir.
public static class UIState
{
    static int openCount;

    public static bool Open => openCount > 0;
    public static void Push() => openCount++;
    public static void Pop() { if (openCount > 0) openCount--; }
    public static void Reset() => openCount = 0; // sahne degisiminde
}

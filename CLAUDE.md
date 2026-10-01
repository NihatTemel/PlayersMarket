# PlayersMarket — Claude icin proje notlari

Unity **6000.0.49f1** (URP), **Mirror 96** (host tabanli) + **Steamworks.NET** (FizzySteamworks).
Co-op/tek oyunculu: gunduz market isletme, gece zindandan esya toplama, sabah satma.
Kamera **3. sahis**. Ayni gelistiricinin onceki projesi: `C:\Users\user\BrawAnimals` (ag yapisi oradan uyarlandi).
Proje klasoru (yerel): **`C:\UnityProjects\PlayersMarket`**. Git: `github.com/NihatTemel/PlayersMarket`, dal `main`;
kullanici commit/push'u **Fork** ile yapar.
Kullanici Turkce konusur. Kod yorumlari **ASCII Turkce** (s, c, g, i, o, u — ozel harf yok);
arayuz metinleri Turkce karakterli olabilir.

## Calisma kurallari

- **Unity'de test edemiyoruz.** Her degisiklikten sonra derleme kontrolu yap ve kullaniciya
  oyunda neyi test etmesi gerektigini acikca soyle.
- Sahne/prefab'lar kullanicida acik olabilir: dosyadan duzenlersen "Reload" demesini soyle.
- Sahne/prefab'i mumkunse **editor builder** ile koddan uret (bkz. `NetworkSetupBuilder`), YAML'i elle yazma.

## Derleme kontrolu (Unity'siz)

```bash
bash Tools/compile-check.sh
```
`Assembly-CSharp errors: 0` ve `Assembly-CSharp-Editor errors: 0` gormelisin. Unity'nin `Library/Bee`
response dosyalarini + `Library/ScriptAssemblies` altindaki asmdef DLL'lerini (Mirror, Steamworks) kullanir.
Unity bunlari henuz derlemediyse: `EXTRA_REFS=/c/Users/user/BrawAnimals/Library/ScriptAssemblies bash Tools/compile-check.sh`

## Dosya kurallari

- `.cs` dosyalari **UTF-8 BOM + CRLF**. Write araci LF yazar → sonra cevir; `wc -l` ile `grep -c $'\r$'` ayni olmali.
- MSYS `perl -i` yanina `.bak` birakabilir → sil. `python` yok, betik icin `perl`.
- **TEHLIKE: `perl -0pi -e '... or die'` eslesmezse dosyayi BOS birakir** (InventoryUI.cs boyle sifirlandi, git'te
  yoktu, .bak da silinmisti). Coklu degisiklikte Edit aracini kullan ya da once `cp` ile scratchpad'e yedek al;
  `.bak` silmeden once dosyanin bos olmadigini kontrol et (`[ -s dosya ]`).

## Ag yapisi

- `Assets/Scripts/Network/MarketNetworkManager.cs` (NetworkManager, DDOL): `HostSteam`, `JoinSteamByCode`,
  `HostLocal`, `JoinLocal(ip)`, `LeaveGame`, `InviteFriends`. Steam = FizzySteamworks, yerel = Telepathy (7777).
  Arayuz referansi tutmaz; durum `StatusChanged` olayi + `LastStatus` ile yayinlanir.
- Steam App ID: **480 (Spacewar, test)** → `MarketNetworkManager.SteamAppId` + `steam_appid.txt`.
  480 paylasimli oldugu icin lobiler `game=PlayersMarket` etiketiyle filtrelenir. Kendi App ID alininca ikisini degistir.
- Lobi verisi: `HostAddress`, `RoomCode` (5 hane), `GameVersion` (= `Application.version`, farkliysa katilim reddedilir).
- Sahneler: `MainMenu` (offline, NetworkManager + `MainMenuUI`), `Game` (online, `GameHUD`, 4 `NetworkStartPosition`).
- Player prefab (`Assets/Prefabs/Player.prefab`, simdilik kapsul): `CharacterController` + `NetworkTransformReliable`
  (ClientToServer, yerel oyuncu yetkili) + `NetworkPlayer` (Steam adi, renk, isim etiketi) + `PlayerMovement`.
- `PlayerMovement`: sadece `isLocalPlayer`'da calisir. Kameraya gore yurume/kosma, ziplama (coyote time, jump buffer,
  degisken yukseklik), zemin = ayak altinda `OverlapSphere` (kendi collider'ini atlar). `IsGrounded`, `Velocity`,
  `Jumped`/`Landed` olaylari animasyon icin hazir. Isinlama: `Teleport()` (CC kapat → konumla → ac).
- `ThirdPersonCamera`: Main Camera'ya `AttachToMain` ile runtime'da eklenir (sahnede varsa onu kullanir).
  Fare/sag cubuk yorunge, tekerlek zoom, SphereCast carpisma (oyuncu CC'lerini yok sayar). Imlec: Esc serbest, tikla kilitle.
- `PlayerInputs`: proje genelindeki `InputSystem_Actions` (`InputSystem.actions`) Player haritasi: Move, Look, Jump, Sprint.
- `Assets/Editor/NetworkSetupBuilder.cs`: prefab + iki sahneyi + Build Settings'i uretir. `MainMenu.unity` yoksa
  acilista bir kez calisir; elle: menu **PlayersMarket > Ag Kurulumunu Yeniden Olustur** (sahneleri EZER).
  Kurulum varsa her acilista `UpgradePlayerPrefab` eksik oyuncu bilesenlerini mevcut prefab'a ekler (sahnelere dokunmaz).
  **Player'a yeni NetworkBehaviour eklerken `AddMissingPlayerComponents`'e ekle** — Mirror'da NetworkBehaviour
  runtime'da eklenemez, prefab'da olmali.

## Kasaba (greybox)

- Tek gelistirici (ekip yok). Sanat paketi: **EmaceArt "Slavic Medieval Village Free"** (`Assets/EmaceArt/`, URP Lit).
  Paket varsa `TownBlockoutBuilder` binalari (Demirci/Simyaci/Lonca/Tefeci + kenar mahalle), dogayi (sabit tohumlu
  agac halkasi, cali, ot, kaya), meydan (kuyu, bank, tezgah) ve dukkan susunu modelle kurar; dukkan kabugu, raf/kasa/
  yuvalar greybox kalir. Model on yonu belirsiz → `BuildTownBuildings`'teki yaw ile cevrilir; tabelalar ayakta direkte.
  Paket olcu/kucuk resim envanteri: `Logs/PackInventory/` (menu **Paket Envanteri Cikar**; git'e girmez).
- Zemin = **Terrain** (`Assets/Greybox/TownTerrain.asset`, her kurulumda yeniden): 240x240 m, oynanan alan (+-60)
  DUZ ve dunya y = 0; disarisi yumusak tepe + `Outer Forest`. Katmanlar: 0 cim, 1 toprak (yol/meydan, `DirtAreas`
  listesi), 2 tepe cimi. Terrain otu cim alanlarda, bina/agac/yol disinda. Yol/meydan eklerken `DirtAreas`'a ekle.
- Paket modellerindeki LODGroup bina olcegine gore: esyalarda (`WorldItem`) sadece LOD0 tutulur, yoksa kucuk esya kaybolur.
- Paketin LOD esikleri (0.6/0.35/0.18) cok agresif: `LodTuner` (Editor) sahnedeki kopyalarda gecisleri 0.12/0.04/0.012,
  gizlemeyi 0.004 yapar (paket dosyasina dokunmaz). Builder her `PlaceArt`'ta uygular; elle: menu **LOD Esiklerini Duzelt**.
- `Assets/Editor/TownBlockoutBuilder.cs`, menu **PlayersMarket > Kasaba Greybox Olustur**: Game sahnesine
  `Town (Greybox)` kokunu kurar (tekrar calisinca kok SILINIP yeniden kurulur; eski `Ground`/`Props` silinir,
  `SpawnPoints` dukkan onune tasinir). Varliklar `Assets/Greybox/` (grid dokusu, materyaller, tek mesh .asset).
- Duzen: meydan 40x40 merkez (0,0); dukkan kuzeyde: salon x[-9,9] z[22,37] + depo x[-16,-9], tavan 5 m (kapi guneye,
  arka kapi depodan), dogu duvari "yikilabilir" (genisleme x[9,19]). Olculer `BuildShop` basindaki sabitlerde. Demirci/Simyaci bati, Lonca dogu, Tefeci guney-dogu,
  zindan kapisi x=52 (dogu yolu), kasaba girisi z=-56 (musteri dogusu). Sinir: +-60 gorunmez duvar.
- 3. sahis olculeri: tavan 5, dukkan kapilari 3x3.4, koridor >= 3.3. Raf gozleri ve vitrin kaideleri `ItemSlot` icerir;
  depoda `TestLootSpawner` (zindan gelene kadar test esyasi doker). Kutular dunya olcekli UV (1 kare = 1 m).

## Esya sistemi

- `ItemData` (ScriptableObject, `Assets/Data/Items/Item_*.asset`): id (DEGISTIRME), ad, taban deger, boyut
  (Small = tek el/raf, Large = iki el/yavas/ziplamaz/kaide), model (`visualPrefab`) ya da yedek sekil.
  `ItemDatabase` = `Resources/ItemDatabase.asset`, `ItemDatabase.Get(id)`.
- `WorldItem` (tek ag prefab'i `Resources/Network/WorldItem.prefab`; `Resources/Network` altindakileri
  `MarketNetworkManager` otomatik kaydeder). SyncVar: `itemId`, `holderNetId`, `slotId`. Serbest = sunucuda fizik,
  istemcide kinematik + NetworkTransform; elde = fizik/collider/NT kapali, herkes tasiyanin el noktasina kendisi
  oturtur (child YAPILMAZ); rafta = kinematik, yuvaya oturur.
- `ItemSlot` (sahne objesi, ag degil): id tum makinelerde ayni olmali. Doluluk = esyalarin `slotId`'sinden.
- `PlayerCarry` (oyuncuda): hedef = bakis konisindeki en yakin esya/bos yuva. E al/koy, Q birak, sol tik firlat.
  Command'lar sunucuda menzil/sahiplik dogrular. `SpeedMultiplier`/`CanJump` -> `PlayerMovement`.
- `Assets/Editor/ItemSetupBuilder.cs` (menu **PlayersMarket > Esya Verilerini Guncelle**): eksik esya
  tanimlarini/veritabanini/WorldItem prefab'ini kurar; modeli bos esyalara EmaceArt Slavic paketinden model baglar.
  Elle degistirilen alanlari ezmez. Yeni varsayilan esya: `Defaults` dizisinin SONUNA ekle.
- Girdi: `InputSystem_Actions` Player haritasi; `Interact` (E, Hold KALDIRILDI), `Drop` (Q, yeni), `Attack` (sol tik).

## Envanter, ekipman, ekonomi (Asama 1)

- Kararlar (kullanici): ortak kasa; karakter (envanter+ekipman) OYUNCUDA saklanir (Valheim gibi); slot envanter;
  KO/Metin2 tarzi basma: tur basina sabit taban guc + demircide "+" (max **+10**), basarisiz = esya **YOK OLUR**,
  maliyet = altin + "Basma Tasi" (`upgrade_stone`). Kucuk esya envantere, buyuk esya (boss ganimeti) elde tasinir.
  Item gucu = takili 8 yuvanin gucu toplami / 8; zindan odalari bu guce gore kilitlenecek (baslangic ~50).
- `ItemStack` (id + plus + count; ag/kayit birimi), `ItemRules` (TUM tablolar: guc/fiyat carpani, basma sansi/
  maliyeti, yuva adlari — dengeleme sadece burada), `ItemData` (kategori, `equipSlot`, `weaponType`, `basePower`,
  `maxStack`, `icon`).
- `PlayerInventory` (oyuncuda, SyncList `bag` 24 + `equipment` 8, `gearScore`): sunucu yetkili Command'lar
  (Move/Equip/Unequip/Drop/TakeToHands/StowHeld), `TryAdd`/`TryConsume`/`ServerPickupToBag`, `autoPickup` esyalari
  yakindan toplar. Yerel oyuncu baglaninca `CharacterSave` dosyasini yollar ya da baslangic seti alir.
- `CharacterSave`: persistentDataPath/characters/<steamId|local>[_editor].json. `GameState` (Resources/Network,
  Game sahnesi yuklenince sunucu dogurur): ortak `gold`, `shopLevel` (fiyat carpani x1.00..x2.00), host'ta world.json.
- `InventoryUI` (kodla kurulur, Tab/I): surukle-birak, sag tik kusan/cikar (ekipman degilse yakin rafa DIZER),
  panel disina = yere at. Secili esya + yakinda bos raf gozu → "Rafa Diz" (`CmdPlaceFromBag`, 1 adet; menzil
  `PlayerCarry.reach + PlaceReachBonus`, hedef `FindNearestFreeSlot`, dunyada ▼ isareti).
  Panel acikken `UIState.Open` → kamera/imlec serbest, PlayerCarry E/Q/firlatma kapali.
- **Demirci (+ basma)**: `NpcStation` (sahne objesi, tur Blacksmith; builder `BuildNpcs` Demirci onune koyar) →
  PlayerCarry E → `BlacksmithUI` (KO tarzi "Esya Yukseltme" penceresi, 4x2 ors slotu) + envanter yaninda (docked,
  `InventoryUI.LockedOpen/RightClickHandler/IsMarked/DragItem/MarkDropHandled` ile). Esya envanterden istenen
  slota surukle-birak; envanterde sag tik = rastgele slot; ors slotunda sag tik = cikar. Ekipman her yerde "+0" dahil gorunur. Kullanici fikri: her basmada sunucu rastgele bir
  "kesin gecis" slotu secer; esya o slottaysa sans bakilmaz, kesin gecer; sonuc ne olursa olsun slot gosterilir.
  Sans tablosu `ItemRules.UpgradeChance` (orta zorluk: +1..+3 %100 → +10 %20), `ChanceWithLuckySlot`.
  `PlayerInventory.CmdUpgrade` (yakinlik/altin/tas dogrular, oder, zar atar, basarisiz = esya silinir) →
  `TargetUpgradeResult`/`TargetUpgradeRejected` → static olaylar. Animasyon sirasinda UI donuk (sonuc erken gorunmesin).
  Gelistirici (editor/dev build): **F9** +1000 altin +5 Basma Tasi, **F5** karakteri sifirla (baslangic seti),
  **F6** rastgele 6 ekipman +0 (`CmdDev*`; kasa/dunya F5 ile sifirlanmaz).
- Yol haritasi: **Asama 2** (demirci TAMAM) satici + dukkan gelistirme; **3** musteri/satis (fiyat etiketi);
  **4** zindan + dusman + savas (kilic/yay) + dusus/boss ganimeti + item gucu kapisi.

## Dukkan: fiyat + musteri (Asama 3)

- Kararlar (kullanici): **karma fiyat** (etiket piyasa fiyatiyla gelir, oyuncu %50-%200 oran secer), **basit pazarlik**
  (kasada teklif: E kabul / Q ret), **piyasa degeri gorunur**. Tum esik/tablolar `PricingRules` (dengeleme burada).
- `WorldItem.priceRatio` (SyncVar; etiket = piyasa x oran). Rafa koyarken `InventoryUI.PlaceRatio` (envanter detay
  panelindeki hizli butonlar); raftaki urune bakip **F** → `PriceTagUI` (`CmdSetPrice`/`CmdSetPriceAll`). Raflarda
  fiyat yazisi renkli (`PricingRules.RatioColor`).
- `CustomerAI` (Resources/Network/Customer, beyin SUNUCUDA NavMeshAgent; istemci NetworkTransform): giris → kapi →
  raf (rastgele urun, rezerve) → `Judge(oran, tip toleransi)`: ucuz/normal al, pahali = pazarlik, cok pahali = kizip git
  → kasa kuyrugu (`queuePos` SyncVar, `ShopLayout.QueuePoint`, L sekli) → oyuncu E ile satar (`PlayerCarry.CmdServe`).
  Sabir 45 sn; bitince urunu rafa birakir, un duser. Odeme `GameState.AddGold` + `reputation`.
  Satis hizi (kullanici: "cok seri satiliyordu"): vitrin gezgini orani (`WindowShopChance`), uygun fiyatta bile
  `BuyChance`, musteri basina 1-3 urune bakma (`RollLooks`), gelis araligi 15-60 sn. Musteri ~1.6 m; prefab
  `CustomerVersion` (ItemSetupBuilder) ile surumlu — duzeni degistirince artir.
- Musteri `IDamageable`: vurulunca "Ayy!" deyip kosarak kacar (itilir), odemediyse urunu rafina birakir, odediyse
  urun elinde kalir. **Un cezasi YOK** (kullanici karari).
- `GameState`: `reputation` (0-100) + `salesCount`/`lastSale`; musteri dogumu (sadece rafta urun varken,
  `SpawnInterval(un, seviye)`, `MaxCustomers`). `ShopLayout` + NavMesh (`Assets/Greybox/TownNavMesh.asset`)
  `TownBlockoutBuilder` kurar → kasabayi yeniden kurmak gerekir.
- Girdi: `Price` eylemi (**F** / gamepad d-pad yukari; R savasa gecti). `IItemHolder` (oyuncu ve musteri esya tutabilir).

## Statlar, siniflar, satici (Asama 4 hazirlik)

- **Sinif = elindeki silah** (sinif secimi yok): Kilic=Savasci (zirh +%20), Hancer=Hancerci (kritik +%10),
  Yay=Okcu (uzaktan +%10), Asa=Buyucu (skill bekleme −%15). `WeaponType` enum'una SONA ekle.
- Statlar esyadan OTOMATIK: `ItemRules.ItemStats` = item gucu x yuva katsayisi (silah saldiri, kafa/govde/eldiven/
  bot zirh+can, kolye can, kupe saldiri+krit, yuzuk krit). `CharacterStats` toplar + pasif. Zirh: hasar x 100/(100+zirh).
  Denge: ayni guçte saniyedeki hasar ±%15 (`WeaponDamageFactor` x `AttacksPerSecond`); fark risk/odul.
- **Savas** (`Assets/Scripts/Combat/`): tuslar **R veya 1 = duz vurus** (basili tutunca seri), **2, 3 = skill**
  (gamepad: sag tetik, sol omuz, sol tetik). Mana yok, sadece bekleme. Yetenekler + denge TEK YER: `AbilityBook`
  (index 0 duz vurus; Kilic: Darbe/Donen Kilic/Atilma, 3. vurus kombo x1.5 · Hancer: Darbe/Arkadan Vurus (arkadan x2)/
  Golge Adimi · Yay: Ok/Coklu Atis/Geri Sicrama · Asa: Buyu Topu (alan)/Ates Topu/Buz Halkasi · silahsiz: Yumruk).
  `PlayerCombat` (oyuncuda): yerel oyuncu bekleme tahmini + efekt + hareket (`PlayerMovement.Dash`, konum istemcide
  yetkili) → `CmdUse` → sunucu bekleme/konum dogrular, hasar = saldiri x carpan (kritik 1.5x, zirh), mermiler ucus
  suresi kadar gecikmeli; `RpcFx` digerlerine. Hedef = `IDamageable` (sunucuda). Oyuncular mermi yolunda yok sayilir.
  `CombatFx` (ilkel sekil efektleri, ItemFallback materyali), `DamageNumbers` (IMGUI ucan sayi), `SkillBarUI` (alt orta,
  dairesel bekleme). `TrainingDummy` (Resources/Network; 1000 can, 20 zirh, 5 sn hasar/sn olcer) meydan GD'deki
  `TrainingDummyPoint`'lerde GameState dogurur. Elde esya varken / panel acikken saldiri yok.
- **Satici** (`NpcType.Vendor`, meydan bati tezgahi): `VendorUI` (KO tarzi, envanter yaninda), stok
  `VendorRules.StarterStock` (her sinifa bir silah + deri/bakir set + basma tasi), fiyat = taban x2 (dukkandan
  bagimsiz; arbitraj olmasin). `PlayerInventory.CmdBuy` (yakinlik/altin/canta dogrular).

## Mirror tuzaklari (BrawAnimals'ta yasandi)

- Kapali ust objenin altindaki sahne NetworkIdentity'si SPAWN EDILMEZ. Kritik mantigi spawn'a baglama.
- Isleyicisi kayitli olmayan NetworkMessage gelirse istemci baglantisi KOPAR → isleyicileri DDOL bilesende kaydet.
- RPC isleyicisinde istisna = baglanti kopar. Kozmetik RPC'leri try/catch ile koru.
- Ek (additive) sahne yuklemesi `sceneLoaded` tetikler: `LoadSceneMode.Additive` ise yok say.
- Karakteri sunucuda `transform.root` ile bulma; sahibini `connectionToClient`/netId ile bul.

## Unity tuzaklari

- `FindObjectOfType` kapali objeleri bulmaz → `FindFirstObjectByType<T>(FindObjectsInactive.Include)`.
- `x ?? y` / `x?.` Unity objelerinde guvenilmez (sahte null) → acik `if (x == null)`.
- `CharacterController`'li karakteri isinlarken: kapat → konumla → ac.
- Canvas'lar `ScaleWithScreenSize` 1920x1080, **Expand** modunda.
- Serialize alan ekledikten sonra editor derlemesi bitmeden build alma ("script class layout is incompatible").
- **Play sirasinda domain reload = Mirror/Steam coker** (esya alinamaz, Steam "Callback dispatcher is not initialized"
  spam'i, Ayril'da "NetworkServer.Destroy() called without an active server"). `PlayModeReloadGuard` Play'de
  yeniden yuklemeyi kilitler. Log'da `Reloading assemblies after finishing script compilation` oyun icindeyse sebep budur.
- Input: `activeInputHandler = Both`; kodda Input System (`Keyboard.current`), UI'da `InputSystemUIInputModule`.

## Hata ayiklama

- Editor log: `%LOCALAPPDATA%\Unity\Editor\Editor.log` (ag loglari `[Network]`, builder `[NetworkSetup]`).

## Gelistirme kaydi (devlog)

- Paket `com.unity.recorder` 5.1.2. `Assets/Editor/DevRecorder.cs` + `Assets/Scripts/Dev/DevCaptureKeys.cs` (sadece Editor).
  Play'de **F1** video (MP4, 1080p ya da 720p, ses dahil) baslat, **F2** durdur, **F8** ekran goruntusu; menu **PlayersMarket > Kayit**.
  Cikti `Recordings/<tarih>/<saat>_<sahne>.mp4|png` (git'e girmez) + `Recordings/KAYITLAR.md` tablosu. Kare hizi
  DEGISKEN (sabit mod `Time.captureDeltaTime` kilitler → Mirror zamanlamasi bozulur). F1/F2/F8'i baska tusa baglama (F9 = gelistirici altini).
- Recorder paketi Unity'de import edilmeden `compile-check` DevRecorder icin "Recorder namespace yok" der (beklenen).

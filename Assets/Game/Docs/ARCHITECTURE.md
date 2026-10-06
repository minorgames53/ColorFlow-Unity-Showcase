# Mimari haritası

Kapsam: `Assets/Game` içindeki mevcut C# implementasyonu. Scene/prefab atamaları ve asset Inspector değerleri bu belgeden doğrulanamaz. Başlangıç: [AGENTS.md](../AGENTS.md).

## Public showcase yapılandırması

`Shared/Config/FeatureConfig.ExternalServicesEnabled` şu anda `false` döner. Mevcut servis girişleri Firebase/Tenjin/Meta, reklam/consent ve gerçek IAP bağlantısını başlatmaz. AnalyticsService boş provider listesiyle kalır; save, lives, yerel ödüller ve gameplay sahipliği değişmez. Reklam/purchase teklifleri kullanılamaz, sahte ödül üretilmez. Developer erişimi remote/cache okumadan reddedilir; cihaz UID'si ve support e-postası showcase akışında kullanılmaz. Serialized config'ler de kapalıdır; Android native başlangıç ve iOS plist collection ayarları ayrıca devre dışıdır. Aşağıdaki servis ayrıntıları, bu koruma dışında kalan entegrasyon implementasyonunu tarif eder. Yeniden etkinleştirme ve lisans sınırları için [kök README](../../../README.md) esas alınmalıdır.

## Katmanlar ve task yerleşimi

| Değişiklik konusu | Mevcut uygulama yeri / sınır |
|---|---|
| Board erişimi, source release, Marble fiziği | `Assets/Game/Scripts/Runtime/Features/SourceBoxes/` |
| Conveyor hareket/giriş/kapasite | `Assets/Game/Scripts/Runtime/Features/Conveyor/` |
| Target reserve/arrival, connected target, booster target işlemleri | `Assets/Game/Scripts/Runtime/Features/TargetBoxes/` |
| Crate, panel, arrow, key/lock, multiplier, connected source, gift | `Assets/Game/Scripts/Runtime/Features/BoardFeatures/` |
| Tile görünümü / source üretimi | `Assets/Game/Scripts/Runtime/Features/BoardTiles/` / `Spawners/` |
| Level verisi, build/session/result/recovery | `Assets/Game/Scripts/Runtime/Features/Levels/` |
| Booster koordinasyonu ve sunumu | `Assets/Game/Scripts/Runtime/Features/Boosters/`; target işlemleri ilgili TargetLaneController partial'ında |
| HUD, sonuç/offer ekranı, oyun shop'u, world mesajı | `Assets/Game/Scripts/Runtime/UI/HUD/`, `UI/Shop/`, `UI/World/` |
| Menu akışı / tutorial | `Assets/Game/Scripts/Runtime/Menu/` / `Assets/Game/Scripts/Runtime/Tutorial/` |
| Oyun analytics context'i ve progress hesabı | `Assets/Game/Scripts/Runtime/Analytics/` |
| Kalıcı servisler, save/store/ads, ortak UI/audio | `Assets/Game/Shared/` |
| Firebase, Tenjin, Remote Config, platform bağlantıları | `Assets/Game/Integrations/`; vendor ayrıntıları mevcut README'lerde |
| Level JSON import | `Assets/Game/Scripts/Editor/LevelManagement/`; ortak pencere `Assets/Game/Shared/Editor/LevelManagement/SharedLevelManagementWindow.cs` |

Namespace'ler genellikle gameplay için `Gameplay.*`, menu için `Game.Menu`, ortak altyapı için `Game.Shared.*` biçimindedir. Klasör adıyla birebir eşleşmeyen mevcut yerleşimler vardır; sınıfı ararken derlenen dosyayı kontrol et.

## Ana bağımlılıklar

```text
SharedSystemsBootstrap → SaveManager + Lives + Ads + Store + SceneLoader
LevelSessionController → LevelProgressController → LevelCatalog / LevelDefinition
                       → LevelBuildController
                           ├─ SourceBoxBoardController → source + board features + spawners
                           ├─ TargetLaneController → target queues + transfers
                           └─ conveyor başlangıcı / key-lock bağlantıları
GameplayInputController → source release → field / entry queue → ConveyorController
TargetLaneController ← conveyor bilyeleri / Hand-UFO transferleri
TargetLaneController → LevelResultFlowController → win veya recovery
                                                 ├─ sonuç UI / progression / reward
                                                 └─ FailRecoveryController ↔ Store
```

- Board feature controller'larının bir bölümü plain C# nesnesidir; `Controller` adı MonoBehaviour veya singleton anlamına gelmez.
- Genel bir event bus/DI katmanı yerine serialized referanslar, mevcut singleton'lar, C# event'leri ve bazı `FindFirstObjectByType` fallback'leri kullanılır.
- Build/clear, input ve transfer invariants için [Features kuralları](../Scripts/Runtime/Features/AGENTS.md); sonuç ve progression için [GAME_FLOW.md](GAME_FLOW.md); booster execution için [Boosters kuralları](../Scripts/Runtime/Features/Boosters/AGENTS.md).

## UI, Menu ve offers

- `Assets/Game/Shared/UI/Panels/PanelManager.cs` ve `UIPanel.cs` ortak ekran altyapısıdır. Blocking modal / deferred open ve input kuralları [Shared/AGENTS.md](../Shared/AGENTS.md) içindedir; bu kurallar onları kullanan oyun UI'ı için de geçerlidir.
- `Assets/Game/Scripts/Runtime/UI/HUD/GameplayHudController.cs` level/gold/capacity sunumunu, board-camera layout uyarlamasını ve Settings pause sahipliğini yürütür.
- Aynı HUD klasöründe `LevelCompleteCanvasView.cs` sonuç paneli, reward seçimi ve çıkışı; `GameplayGiveUpPanelController.cs` menüye vazgeçme onayını yönetir. Sonuç kararının sahibi bu view'lar değildir.
- `FailOfferPanelController.cs` preview video sunumunu yapar; cleanup, ödeme ve recovery kararı `Assets/Game/Scripts/Runtime/Features/Levels/FailRecoveryController.cs` içindedir.
- `Assets/Game/Scripts/Runtime/Menu/MenuPlayController.cs` can kontrolüyle Game'e geçer. `MenuLevelProgressController.cs` level yolu sunumu, `MenuBottomNavigationController.cs` tab geçişi, `MenuShopController.cs` shop ekranı sahibidir.
- `Assets/Game/Scripts/Runtime/Menu/MenuNoAdsOfferController.cs` Menu-ready, coin sunumu ve No Ads offer zamanlamasını; `NoAdsPanelController.cs` ürün panelini yönetir. Uygunluk `AdsService`, satın alma `StoreManager` üzerinden gelir.
- Game/Menu shop presenter'ları `Assets/Game/Shared/Store/UI/` altyapısını paylaşır. Gold uçuşu sahneler arasında `Assets/Game/Shared/UI/Display/CoinFlyPresentationHandoff.cs` → `Assets/Game/Scripts/Runtime/Menu/MenuCoinFlyHandoffPresenter.cs` ile taşınır.
- Can refill sunumu `LivesRefillPanelController` → popup üzerindeki local `HeartFlyAnimator` yolunu kullanır. Gerçek grant öncesi/sonrası lives farkı FX sayısını belirler; reward ve coin işlemi anında tamamlanır. Reklam FX'i başarılı reward ve reklam kapanışından sonra başlar; mevcut otomatik popup kapanışı sunum bitene kadar bekler. Menu HUD yalnızca sayı yazımını geçici tutar, son varış/iptalde `LivesService` üzerinden yeniler; ayrı lives sayacı yoktur. `LifePanel` prefabı source/heart prefab referanslarını, Menu instance'ı mevcut Coin Fly Animation root, HUD ve `UIInputLockService` bağlantılarını taşır. Disable/destroy tween, uçan objeler, scale ve yalnızca alınan input kilidini temizler.
- Tutorial input kısıtları `Assets/Game/Scripts/Runtime/Tutorial/LevelOneTapTutorialController.cs` ve `BoosterUnlockTutorialController.cs` içindedir. Unlock state, SaveManager ile bağlıdır.
- Ortak ekran uyarlamaları `Assets/Game/Shared/UI/TopBottomSafeArea.cs` ve `AdaptiveCanvasScalerMatch.cs`; kavisli TMP metni `Assets/Game/Shared/Helper/TMPArcText.cs` içindedir. Benzer yardımcı eklemeden önce bunları kontrol et.

## Localization

- Unity Localization'ın `General` String Table koleksiyonu ve tek Shared Table Data asset'i kaynak gerçektir. `en`, `tr`, `de`, `fr`, `pt-BR`, `es`, `it`, `pt-PT`, `ar`, `pl`, `id` locale'leri Addressables üzerinden yüklenir. Runtime çeviri üretimi veya ikinci bir metin deposu yoktur.
- Sabit TMP etiketleri `LocalizeStringEvent`, değişken metinler mevcut presenter'lardaki `LocalizedString`/StringDatabase yolunu kullanır. `level` ve `quantity` Smart String değişkenleri, `{0}`/`{1}` format argümanları korunur. Store/restore geçici mesajları mevcut `PanelManager` üzerinden `Localized Message` prefabına yazılır; Menu/Game referansları serialized olarak bağlıdır.
- Başlangıç sırası: command-line selector → `PortugueseLocaleSelector` → Unity system selector → English. `pt-BR` Brezilya'ya; `pt`, `pt-PT`, diğer Portekizce bölgeler Portekiz'e eşlenir. Projede manuel dil seçimi ekranı veya kalıcı locale tercihi bulunmaz; save modeli değiştirilmemiştir.
- `LocalizedArabicText`, serialized TMP etiketlerinin son sunumunu düzenler. Tablolar mantıksal Unicode sırasında kalır; harf birleşimi/lam-alef ve sayı/Latin run sırası hazırlanır, satır sarma TMP'nin RTL layout'una bırakılır. Normal `.text` ve `SetText` presenter akışları LateUpdate'te işlenir. Disable sırasında özgün metin/hizalama geri yüklenir. `TMPArcText`, birleşik Arapça harfleri ayırmamak için RTL satırlarını bükmez.
- Yedi Nunito TMP fontunun fallback'i, projeyle dağıtılan SIL OFL lisanslı `NotoSansArabic-Bold SDF` asset'idir. Runtime font araması veya Resources.Load kullanılmaz. Dar etiketler mevcut Auto Size yaklaşımıyla uyarlanmıştır.
- `Localization/Editor/LocalizationIntegration.cs` açıkça çağrılan kurulum ve statik doğrulama yardımcılarını içerir; import/build sırasında otomatik çalışmaz. `Translations.tsv` import girdisi, `IntegrationBaseline.tsv` önceki 284 entry için regresyon verisidir; ikisi de Editor kapsamındadır. Çalıştırılan kontroller ve kapsam sınırları [LOCALIZATION_VALIDATION.md](LOCALIZATION_VALIDATION.md) içindedir.

## Veri ve config sahipleri

| Kaynak | Sorumluluk |
|---|---|
| `Assets/Game/Data/Levels/LevelCatalog.asset` ve aynı klasördeki level asset'leri | Sıralı/loop içerik ve level tanımları; sınıflar `Assets/Game/Scripts/Runtime/Features/Levels/` altında |
| `Assets/Game/Data/BoardFeatureCatalog.asset` | Feature prefab ve ayar referansları; `BoardFeatureCatalog.cs` |
| `Assets/Game/Data/Marble Color Catalog.asset` | Renk başına source/target/marble görselleri ve tint; `MarbleColorCatalog.cs` |
| `Assets/Game/Data/Configs/` | Audio, UI, haptic, scene loading, ads, analytics, crash reporting, feature flag asset'leri |
| `Assets/Game/Prefabs/Grid/` | Core/feature/booster/tile prefab'ları; runtime sahipleri serialized prefab referanslarını kullanır |
| `Assets/Game/Localization/Tables/` | Yerelleştirme tabloları; presenter'lar LocalizedString kullanır |

- `Config` eki tek bir veri türü değildir: `Assets/Game/Scripts/Runtime/Features/Levels/LevelWinRewardConfig.cs`, `LivesEconomyConfig.cs` ve `Assets/Game/Shared/Store/Core/StoreCatalog.cs` kod içi static tanımlardır. `LivesConfig` ise `Assets/Game/Data/Configs/LivesConfig.asset` ScriptableObject'idir (enabled, max lives, refill minutes). Bootstrap'ın mevcut `SceneLoadingConfig` asset referansı üzerinden `SharedSystemsBootstrap` → `LivesService` yoluna aktarılır; ek scene referansı veya runtime asset araması yoktur. Disabled durumda giriş/spend kontrolleri bypass edilir ve otomatik refill/config-max normalizasyonu saved state'e dokunmaz; yeniden enabled olduğunda mevcut deadline üzerinden offline refill sürer.
- Level formatı değişikliğinin iki tüketicisi vardır: runtime validator/build ve Editor JSON import. `GameLevelImportAdapter` / `GameLevelDesignerJsonAdapter` mevcut import hattıdır.
- `FeatureConfig` içinde cloud-save/account gibi bayrak bulunması çalışan backend'in kanıtı değildir. Boş integration klasörlerinden çalışan service çıkarımı yapma.

## Audio

- API: `Assets/Game/Shared/Audio/AudioManager.cs` → `AudioManager.Instance?.PlaySfx(AudioKey.X)`; gerekirse pitch overload'u. `PlayMusic` ve stop API'leri de aynı sahibindedir.
- `Assets/Game/Shared/Audio/AudioKey.cs` kimlikleri `AudioCueCatalog` içindeki `AudioCue` ile `AudioClip`, volume ve minimum interval'a eşlenir. Clip'ler `Assets/Game/Audio/SFX/` altındadır; runtime çağrısına dosya yolu verilmez.
- Inspector referansları: AudioManager üzerinde `sfxSource`, `musicSource`, `cueCatalog`, `sfxPoolSize`; katalog asset'i `Assets/Game/Data/Configs/AudioCueCatalog.asset`.
- SFX round-robin AudioSource havuzu kullanır. `minimumInterval` unscaled zamanla tekrar çağrısını bastırır; oynatmayı geciktiren parametre değildir.
- Gecikme/stagger, çağıran animasyonun sorumluluğundadır. Örnek: `Assets/Game/Scripts/Runtime/Features/SourceBoxes/SourceBoxReleaseAnimator.cs` serialized release SFX sayısı/aralığını kendi sequence callback'lerine yerleştirir.
- Sound setting, SaveManager event'inden `SharedSystemsBootstrap` aracılığıyla AudioManager'a uygulanır. Yeni SFX için mevcut AudioKey/catalog hattını kullan; gameplay'e ayrı AudioSource/Resources path yüklemesi ekleme.
- Haptic feedback ayrı `Assets/Game/Shared/Haptics/HapticManager.cs` / `HapticType` API'sini, config ve platform provider'larını kullanır; sound açık/kapalı state'ine bağlanmaz.

## Animasyon ve zaman

- Conveyor `Movement / Max Speed` varsayılan 15 slot/s olan, gameplay çarpanından sonra uygulanan mutlak path hız sınırıdır. Base/leader, catch-up, giriş hızlandırması ve frame başına spacing/snap düzeltmeleri aynı sınıra tabidir; slot yolu da capped base hızını kullanır. Limitin kendisi fast-forward ile çarpılmaz; target/source transfer animasyonlarını kapsamaz.
- Conveyor `Warning Glow Enabled` checkbox'ı varsayılan kapalıdır; hem otomatik hem manuel fail uyarısını kapatır. Runtime Inspector değişikliği mevcut tween'i iptal edip alpha/state'i sıfırlar; yeniden açılması tek başına pulse başlatmaz, sonraki içerik değişimi veya manuel fail tetiklemesi beklenir.
- Conveyor warning glow, `ConveyorController` üzerindeki mevcut serialized `SpriteRenderer` ile yönetilir. Warning oranı `(MarbleCount - matchableCount) / SlotCount` olur; giriş rezervasyonları sayılmaz. `TargetLaneController.CountMatchableConveyorMarbles`, mevcut `GetValidConveyorMarble` ve `TryFindConsecutiveTargetForNormalClaim` → `TargetBox.CanReserve` / `AvailableReservationCount` kontrollerini kullanır; aynı boş target slotu iki kez sayılmaz, gerçek rezervasyon yapılmaz. Yalnız mevcut kabul edebilen aktif target'lar değerlendirilir; conveyor üzerindeki capture alanına varış mesafesi hesaba katılmaz. Target owner, mevcut conveyor referansından runtime bağlantıyı sağlar; yeni scene wiring veya arama yoktur. Hesap yalnız `AssignMarbleToSlot`, `RemoveMarble` ve mevcut null-slot temizliği gerçekten içeriği değiştirdiğinde çalışır; her-frame warning polling yoktur. Kod threshold varsayılanı 0.80'dir; mevcut serialized Inspector override'ı korunur. Scaled DOTween ile mevcut alpha/blink/pulse ayarları korunur (varsayılan 3 kez 0→255→0 ve sonunda alpha 0). Eşik altında/boş conveyor, `Clear` (build/retry/exit) ve disable tween'i iptal edip yeniden arm eder. RGB ve gameplay/fail kapasite hesabı değişmez.
- DOTween sequence/tween referansları ilgili controller/view tarafından tutulur; gameplay örnekleri `SetLink(..., KillOnDestroy)` ve explicit `Kill(false)` cleanup kullanır. Link, disable/reset cleanup'ının yerine geçmez.
- UI panel/button tween'leri `UIPanel` / `TweenButton` içinde `SetUpdate(true)` kullanır; `UIConfig` ortak süre/ease/scale kaynağıdır. `UIIdleTweenAnimator` kendi serialized zaman tercihini taşır.
- Gameplay'de tek evrensel tween zaman modu yoktur. Örneğin win panel delay scaled, cleanup offer delay unscaled çalışır. Mevcut owner'ın pause/exit/iptal davranışını koru.
- Gameplay hız çarpanı `Assets/Game/Scripts/Runtime/Features/Levels/GameplaySpeedController.cs` / `IGameplaySpeedTarget` üzerinden uygulanır. Settings pause ise session'ın sahip olduğu `Time.timeScale` askısıdır; ikisini birbirine dönüştürme.
- Animasyon completion callback'i bazı sistemlerde gameplay commit'idir. Süre, callback sırası veya `Kill(true)` değişikliğini yalnızca görsel değişiklik sayma.

## Save / servis sınırı

- Reklam initialization sahibi `SharedSystemsBootstrap` → `AdsService.InitializeSdkOnce` yoludur. Editor da aynı `MobileAds.Initialize` korumasını, `RewardedAd.Load` / `InterstitialAd.Load`, `CanShowAd` ve `Show` akışlarını kullanır; görünüm Google Mobile Ads Unity Plugin'in kendi `PlaceholderAds` prefab'larından gelir. Projeye ait Editor load/show simülasyonu yoktur. `AdsConfig.TryGetAdUnitId`, Editor'da `useTestAds` değerinden bağımsız Google sample ID'lerini seçer: iOS build target için iOS, diğer Editor target'ları için Android. Cihazda mevcut `useTestAds` / production ID seçimi ve consent, reward, No Ads, scheduling, analytics/revenue lifecycle'ı korunur.
- `Assets/Game/Shared/Save/SaveManager.cs` typed API ve değişim event'leri sunar; `GameSaveData` progression, gold, lives, booster, settings ve store kayıtlarını taşır.
- `Assets/Game/Shared/Save/Storage/JsonFileSaveStorage.cs`, `Application.persistentDataPath` altında `save.json`, `save.bak`, `save.tmp` kullanır. Load/normalize/backup ve unsupported-version write block SaveManager yolunun parçalarıdır.
- Mutation'ların çoğu dirty işaretler; pause/quit/owner destroy sırasında flush edilir. Bazı işlemler (ör. current level, settings, win reward, IAP commit) açıkça kaydeder.
- Analytics attempt snapshot'ı ve developer Remote Config cache'i ayrı PlayerPrefs kullanımlarıdır; gameplay save'iyle aynı storage sanma. Sözleşmeler: [Shared kuralları](../Shared/AGENTS.md), [ANALYTICS.md](ANALYTICS.md), [Remote Config README](../Integrations/RemoteConfig/README.md).

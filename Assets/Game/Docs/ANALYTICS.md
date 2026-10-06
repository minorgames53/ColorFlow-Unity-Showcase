# Analytics sözleşmesi

Public showcase'de `FeatureConfig.ExternalServicesEnabled=false`, collection ve provider config anahtarları kapalıdır. `AnalyticsBootstrap` yerel servisi boş provider listesiyle oluşturur; Firebase/Tenjin başlatılmaz ve event gönderilmez. Aşağıdaki sözleşme mevcut entegrasyon kodunun, servisler ayrıca yapılandırılarak etkinleştirildiğindeki davranışını açıklar. Native collection korumaları ve public ayarlar için [README](../../../README.md) bölümüne bakın.

## Merkezi API ve kaynaklar

- Gameplay/UI event'i: `AnalyticsEventFactory.Create...` → `AnalyticsBootstrap.Instance?.Track(...)` → `AnalyticsService` → Firebase provider.
- Factory, production event/parametre isimleri ve placement sabitleri aynı dosyada: `Assets/Game/Shared/Analytics/Core/AnalyticsEventNames.cs`.
- Bootstrap: `Assets/Game/Shared/Analytics/AnalyticsBootstrap.cs`; dispatch: `Assets/Game/Shared/Analytics/Core/AnalyticsService.cs`; Firebase adapter: `Assets/Game/Shared/Analytics/Providers/FirebaseAnalyticsProvider.cs`.
- Provider SDK'sını gameplay/UI'dan doğrudan çağırma. Var olan production isimlerini/parametre tiplerini değiştirme veya aynı eylem için ikinci event kaynağı oluşturma.
- Config sınıfı `Assets/Game/Shared/Analytics/Config/AnalyticsConfig.cs`, asset `Assets/Game/Data/Configs/Analytics.asset`. Collection/config, SDK readiness ve debug kayıtları ayrı durumlardır; log görünmesi sunucu teslimi kanıtı değildir.

## Event sahipliği

| Production event | Kaynak / gönderim sınırı |
|---|---|
| `boot_completed`, `boot_failed` | `Assets/Game/Shared/Bootstrap/SharedSystemsBootstrap.cs`; servis başlangıcı, sahnenin tamamen hazır olduğu anlamına gelmez |
| `level_started`, `level_completed`, `level_failed`, `level_exited` | `Assets/Game/Scripts/Runtime/Analytics/LevelAnalyticsTracker.cs`; attempt lifecycle ve snapshot recovery |
| `fail_offer_shown`, `fail_offer_clicked`, `cleanup_used` | `Assets/Game/Scripts/Runtime/Features/Levels/FailRecoveryController.cs` |
| `fail_offer_purchased`, `level_iap_purchase` | `Assets/Game/Shared/Store/Core/StoreManager.cs`; doğrulanmış purchase/transaction ledger yolu |
| `booster_used` | `Assets/Game/Scripts/Runtime/UI/HUD/BoosterHudController.cs`; tüketilmiş booster'ın başarılı completion'ı |
| `booster_purchased` | `Assets/Game/Scripts/Runtime/UI/HUD/AddBoosterPanelController.cs`; gold harcama + envantere ekleme |
| `rewarded_ad_requested`, `rewarded_ad_completed`, `rewarded_ad_failed` | Double gold için `Assets/Game/Scripts/Runtime/UI/HUD/LevelCompleteCanvasView.cs`; life için `Assets/Game/Shared/Lives/UI/LivesRefillPanelController.cs` |

`cleanup_used` her recovery için gönderilmez: Clean Up düğmesinden başlayan ve başarıyla tamamlanan işlemde işaretlenir; Fail Offer Play On/premium yolu bu işareti kurmaz.

## Level context ve attempt

- `level_displayed_number` oyuncu progression'ı, `level_number` içerik kimliğidir. Event üretirken `LevelAnalyticsTracker.TryGetLevelContext` kullan; loop'ta iki numarayı eşitleme.
- Factory level değerlerini int alır; Firebase provider bu iki level parametresini SDK payload'ında string'e çevirir. Diğer parametreleri keyfi olarak string'e dönüştürme.
- Tracker yeni build'de attempt sayısını displayed level'e göre ilerletir. Aynı attempt için en fazla bir normal terminal event gönderir.
- Fail/recovery ve yeni attempt sınırları [GAME_FLOW.md](GAME_FLOW.md) içindedir; panel açılışından terminal event üretme.
- Progress, `Assets/Game/Scripts/Runtime/Analytics/TargetLaneLevelProgressProvider.cs` ile target'lara gerçekten varmış bilyeler / toplam target kapasitesi olarak hesaplanır; reserved slot sayısı kullanılmaz.
- Süre realtime segmentlerinden gelir; uygulama background süresi dışlanır. Gameplay Settings pause'u için ayrı bir süre durdurma aboneliği yoktur; bunu yalnız aktif hamle süresi diye adlandırma.
- Aktif attempt snapshot'ı ve son attempt numarası tracker'ın PlayerPrefs key'lerindedir. Kapanış/teardown snapshot bırakır; `Awake` veya sonraki `BeginLevel` sırasında tamamlanmamış snapshot `level_exited` olarak kurtarılır. Menu'ye tıklama anında doğrudan exit event'i gönderildiğini varsayma.
- Debug level yüklemeleri suppression kullanır; snapshot/default context temizliği korunmalıdır.

## Asenkron işlemler ve provider ayrımı

- Rewarded, booster ve offer işleminde level context'i başlangıç/commit noktasında yakalanır; gecikmiş callback'te değişmiş current level'den tekrar hesaplama.
- Firebase default level context'i gameplay'de tracker, Menu'de `Assets/Game/Scripts/Runtime/Menu/MenuLevelProgressController.cs` tarafından owner ile yönetilir; eski sahibin teardown'ı yeni context'i temizlememelidir. Default parametreler SDK'nin sonraki event'lerine bağlam sağlar; ayrıca purchase event'i üretme yetkisi vermez.
- Firebase hazır değilken normal event'leri provider kuyruğa alabilir. Store confirmed IAP ledger'ı kendi retry/dispatch yolunu ve `TrackWithoutProviderQueue` kullanır; ikinci kuyruk veya UI'dan revenue gönderimi deduplication'ı bozabilir.
- Tenjin normal `AnalyticsService.Providers` listesine eklenmez; `AnalyticsBootstrap` üzerinden ayrı attribution/purchase/ad-revenue hattıdır. `LevelAnalyticsTracker.CompleteLevel`, `level_achieved` için displayed numarayı integer değer olarak yollar; normal terminal event guard'ından ayrı bir attribution guard'ı vardır.
- Tenjin tutorial event'i göndermez. Consent, confirmed transaction, at-most-once handoff, AdMob revenue ve log redaction ayrıntılarının sahibi mevcut [Tenjin README](../Integrations/Tenjin/README.md); burada yeniden tanımlama.
- Developer analytics ekranı/debug kayıtları, collection izni veya SDK teslim onayı değildir. Test-device erişimi için [Remote Config](../Integrations/RemoteConfig/README.md) ve [Lunar](../Integrations/LunarConsole/README.md) kurallarını kullan.

# Level ve sonuç akışı

Editor-only misket kimliği, transfer geçmişi, envanter ve sahiplik doğrulaması: [MARBLE_TRACKER.md](MARBLE_TRACKER.md). Pencere: `Tools → Color Flow → Marble Tracker`; gameplay sahipliği mevcut controller/collection'larda kalır.

`Tools → Save Debugger → Quick Actions → Level Result` içindeki Editor-only **Win / Lose** düğmeleri aktif Play Mode level'ında mevcut `LevelResultFlowController` sonuç akışını tetikler. Win, sealed-source kontrolünü ve sonuç paneli gecikmesini debug çağrısı boyunca atlar; normal progression/ödül hazırlığı ve win event'leri çalışır. Lose, Clean Up teklifini açmadan `FinalizePendingFail` üzerinden kesinleşir; normal can kaybı/lose event'leri çalışır. Mevcut developer adapter gibi attempt analytics'i suppress edilir. Bitmiş, paused veya recovery durumundaki oturumda ikinci sonuç tetiklenmez; Inspector ataması gerekmez.

Buradaki controller dosyaları aksi belirtilmedikçe `Assets/Game/Scripts/Runtime/Features/Levels/` altındadır. Board/transfer ayrıntıları [Features kurallarında](../Scripts/Runtime/Features/AGENTS.md); UI/servis yerleşimi [ARCHITECTURE.md](ARCHITECTURE.md) içindedir.

## Başlangıç ve build

1. `Assets/Game/Shared/Bootstrap/SharedSystemsBootstrap.cs` servisleri initialize eder; SaveManager'dan current level okunur.
2. `Assets/Game/Shared/Navigation/InitialSceneRouting.cs`: current level 11'den küçükse Game, aksi durumda Menu. Bu, sonuç ekranının dönüş kuralından ayrı bir boot kararıdır.
3. `Assets/Game/Shared/Navigation/SceneLoader.cs`, `SceneLoadingConfig` sahne isimleriyle async load ve transition ekranını yönetir; `SceneReadyNotifier` bildirimi veya timeout sonrasında geçişi bitirir. Sahne geçişi için doğrudan ikinci SceneManager akışı kurma.
4. Game'de `LevelSessionController.Start` → `StartCurrentLevel`: `SaveManager.CurrentLevel` displayed level olur; `LevelProgressController.ResolveLevel` içerik asset'ini seçer. Build öncesinde state `Playing` yapılır ve move/life flag'leri sıfırlanır.
5. `LevelBuildController.BuildLevel`: validator + controller/config kontrolleri → `ClearLevel` → key/lock prepare → source board build → target lanes build → initial conveyor marbles → GameStart SFX/haptic.
6. Session displayed-level event'ini yayınlar, tutorial başlangıçlarını ve `LevelAnalyticsTracker.BeginLevel(displayed, internal, suppress)` çağrısını yapar.

Başlangıç conveyor bilyeleri, mevcut hareket yönüne göre öndeki slot ilk eklenecek şekilde kaydedilir. Böylece catch-up'ın en eski girişten seçtiği lider grubun önünde olur ve Level 11'in dokuz mavi bilyesi başlangıçta ayrılmaz. Level verisindeki renk/slot eşlemesi ve kapasite rezervasyonu korunur.

`LevelBuildController.buildOnStart` bağımsız başlangıç seçeneği, session'da da yalnız SourceBoxBoardController ile build fallback'i vardır. Tam level taskında koordineli builder yolunu kullan; canlı wiring doğrulanmadan birden fazla başlangıç yolunu etkinleştirme.

## Displayed level ve içerik

- **Level Management → Play (Editor):** seçilen `LevelDefinition` mevcut Game sahnesinde doğrudan kurulur; scene reload, loading veya transition ekranı yoktur. `LevelSessionController` seçilen asset referansını kullanır, katalog/loop indeksine yeniden çözmez; HUD numarası asset'in `LevelNumber` değeridir. Build önkoşulları eski level temizlenmeden doğrulanır. Mevcut exit/clear yolu transfer, booster, recovery ve pause temizliğini yapar; sonuç view'ının gecikmeli/rewarded callback'leri iptal edilir, aynı sahnedeki PanelManager'lar açık/kapanan panelleri ve ertelenmiş istekleri temizler. Ardından normal session build, HUD bildirimi ve tutorial başlangıcı çalışır.
- Game sahnesinin Play Mode'da açık olması gerekir; Menu'den sahne geçişi başlatılmaz. Devam eden scene transition, satın alma veya blocking modal sırasında istek reddedilir. Bu debug yükleme SaveManager current level, gold veya can değerlerini sıfırlamaz; eski ve yeni attempt analytics'i suppressed kalır. Retry aynı asset'i korur; normal continue save/progression akışına döner. Win/reward/life sonuç kuralları değişmez. Scene/prefab/Inspector ataması gerekmez.
- Kontrol senaryoları: Game'de farklı numaralı asset aç; sahne handle'ı değişmeden ve loading görünmeden HUD/board seçimini doğrula. Settings/sonuç/cleanup/booster açıkken geçişte eski panel, pause, transfer veya input kilidi kalmamalı. Retry aynı asset'i açmalı; Menu'den ve blocking modal sırasında Play mevcut state'i değiştirmeden reddedilmeli. Manuel Play Mode testi bu değişiklik sırasında çalıştırılmadı; son Unity derleme/Console doğrulaması geliştiricide.

- Level 1'in ilk tap adımı mevcut `LevelOneTapTutorialController` tarafından yönetilir. `LevelSessionController` build sonrasında başlatır; mevcut `LevelOneTapTutorialCompleted` save key'i true ise sunum ve input kısıtı kurulmaz. Beklenen source, Inspector'daki `targetCellIndex` ile board'dan; hedef ise aynı renkli, aktif ve rezervasyon kabul eden `SpawnedTargetBoxes` içinden çözülür. Serialized `Tutorial Info`/TMP ve `General/tutorial.level1.match_colors` localization bağlantısı, source/target glow prefabları ve mevcut hand birlikte gösterilir. Glow alpha'sı scaled DOTween ile loop eder (0–255, tam pulse varsayılan 0.5 sn). Mevcut `ReleaseStarted` callback'i aynı gameplay tap'ini kesmeden adımı tamamlar ve aynı save key'ini yazar. `LevelClearing`, disable/destroy ve yeniden başlangıç; localization aboneliğini, tween'leri, runtime glow/hand instance'larını ve mevcut source bloklarını temizler. Tamamlanmamış retry yeniden başlar; tamamlanmış retry/reopen sunumu atlar.

- Displayed level oyuncunun ilerlemesidir; internal number `LevelDefinition.LevelNumber` değeridir. Loop başladıktan sonra eşit olmaları gerekmez.
- `LevelCatalog.GetOrderedLevels`, içerikleri internal level number'a göre sıralar. İlk geçiş bu sırayı izler.
- Katalog bittikten sonra `IncludeInLoop` içerikleri `LoopSeed + cycleIndex` ile deterministik karıştırılır; birden fazla farklı içerik varsa döngü sınırındaki doğrudan tekrar önlenir.
- Loop içeriği yoksa son sıralı level, tek içerik varsa o level tekrar kullanılır. Displayed numarayı asset adına veya rastgele yeni progression sayacına bağlama.

## Oynama ve session state

- UFO unlock/verme sunumu tamamlanıp `WaitForUfo` aşamasına girildiğinde ortak `Tutorial Info`, `General/tutorial.ufo.description` ile açılır. Buton aktivasyonunun mevcut state bildirimi ve `NotifyBoosterActivationAccepted(Ufo)` completion/cleanup yolu açıklamayı hemen kapatır; UFO işleminin bitişi beklenmez. Verilen miktar, unlock, consumption, input ve completion/save akışı değişmez. Tutorial reset/disable, level clear ve scene exit mevcut owner bazlı panel temizliğini kullanır.

- Shuffle unlock animasyonu bitip `WaitForShuffle` aşamasına girildiğinde, buton highlight ile birlikte ortak `Tutorial Info` alanı `General/tutorial.shuffle.description` metnini gösterir. Oyuncu Shuffle'ı aktive ettiğinde mevcut booster state bildirimi ve `NotifyBoosterActivationAccepted(Shuffle)` cleanup'ı açıklamayı kapatır; animasyon boyunca tekrar açılmaz. Completion/save ve owner disable zamanı değişmez. Level clear, tutorial reset/disable ve diğer owner'a geçişte mevcut gösterim temizliği kullanılır.

- Hand unlock tutorial, `NotifyBoosterActivationAccepted(Hand)` sonrasında `WaitForHandSourceBox` aşamasına girince ortak HUD `Tutorial Info` alanını `General/tutorial.hand.select_source` ile gösterir. Buton highlight/unlock aşamasında bu açıklama açılmaz. Geçerli source kabulü/completion, Hand targeting'den çıkış/iptal, `LevelClearing` ve tutorial cleanup/disable alanı kapatır. Panel/TMP referansları mevcut `LevelOneTapTutorialController` üzerinde kalır; gösterim owner ile açılıp kapatılır, başka tutorial'ın gösterimi eski owner cleanup'ıyla kapanmaz. Input, consumption ve booster completion/save akışı değişmez.

```text
Playing ↔ Paused
Playing → Won
Playing → Recovering → Playing (cleanup)
                     → Failed (offer kapatılıp fail kesinleşirse)
Her state → Exiting (TryBeginExit guard'ıyla)
```

- Başarılı normal source release ve booster'ların gerçek commit noktaları `HasCommittedPlayerMove` işaretler.
- `IsPlaying` kullanıcı aksiyonuna izin verir. `IsSimulationRunning`, `Playing`, `Recovering`, `Failed` için true'dur; fail/offer ekranı bütün fizik ve conveyor'un durduğu anlamına gelmez.
- Settings pause'u `GameplayHudController` sahiplenir; `LevelSessionController` timeScale'ı saklayıp askıya alır, resume/disable/exit yolunda geri bırakır.
- `LevelResultFlowController` oynanabilir source kalmadığında, pending spawner veya gift çözümü de yoksa `GameplaySpeedController` ile hızlandırabilir; bu win koşulu değildir.

## Win ve ödül

1. `Assets/Game/Scripts/Runtime/Features/TargetBoxes/TargetLaneController.cs` `AllLanesCompleted` yayınlar.
2. `LevelResultFlowController.BeginWin`, sealed source kalmadığını ve `Playing → Won` geçişini kontrol eder; tekrar sonucu engeller.
3. Ödül `LevelWinRewardConfig.GetGoldReward(Difficulty)` ile hazırlanır. Analytics complete ve `MarkCurrentLevelCompleted` çağrılır; SaveManager current level gerekirse displayed + 1'e ilerler ve kaydedilir.
4. Mantıksal kazanım ile panel sunumu ayrıdır: `AllLanesCompletionPresentationCompleted` → `winPanelDelay` → `WinAnimationTriggered`. Public `TriggerWin` gecikmeyi doğrudan başlatır.
5. `Assets/Game/Scripts/Runtime/UI/HUD/LevelCompleteCanvasView.cs`, `subscribeToResultFlow` açıksa result animation event'lerini dinler; `ShowWin` / `ShowLose` panel sunumunu başlatır. Gold yalnızca guarded base/double claim API'sinden verilir; progression ilerlemesiyle aynı işlem değildir.
6. Displayed level 1–9 kazanımlarında base reward panel açılınca otomatik claim edilir, continue aynı Game sahnesinde sonraki level'i kurar. 10 ve sonrasında continue Menu'ye döner; claim edilmiş gold'un yalnız sunumu coin handoff ile taşınır.
7. Double gold, `AdsService.ShowRewarded(RewardedDoubleGold)` earned callback'inde claim edilir; Menu'ye geçiş reklamın finished callback'ini bekler. Request version ve bir kez ödül guard'larını koru.

Interstitial yalnız `BeginWin` veya `FinalizePendingFail` sonucundan planlanır. `AdsConfig.InterstitialStartLevel` varsayılan **19** (minimum 1) ve dahil başlangıç sınırıdır: displayed level 1–18 sonuçlarında reklam yok; 19 ve sonrasında mevcut win/lose kuralları uygulanır. Sonuç sahibi displayed level numarasını AdsService'e aktarır; win ile save 18'den 19'a ilerlese bile 18. level sonucu reklam açamaz. Merkezi `CanShowInterstitial` ve doğrudan `ShowInterstitial` aynı eşiği kontrol eder; sonuç context'i yoksa mevcut save level'i kullanılır. iOS/Android aynı kuralı kullanır. `ShowInterstitialAfterWin` varsayılan true, `InterstitialLoseInterval` 4 (minimum 1). Her Win, reklamın uygunluğundan bağımsız olarak `SaveKeys.InterstitialLoseCount` (`interstitial_lose_count`) setting'ini sıfırlar; yalnız kesinleşen Lose artırır. Recovery, Clean Up, Give Up ve mantıksal fail sayılmaz. Sayaç level/retry/scene değişiminde korunur.

`AdsService` mevcut result owner için en fazla bir reklam denemesi tutar; `ContinueClaimedWin`, `TryAgain` veya sonuçla ilişkili `SceneLoader.LoadMenu` continuation'ını reklamın arkasına bağlar. Sonuç context'i olmayan Menu/scene geçişi reklam tetiklemez. Interstitial başlangıç ayarı, No Ads teklifinin level-19 milestone'undan ve rewarded unlock kuralından bağımsızdır. Double Gold earned callback'i çıkışı erken isterse mevcut fullscreen reklamın kapanışı beklenir; rewarded grant/callback kodu değişmez. Hazır olmayan reklam yüklemesini beklemeden continuation ilerler. SDK fullscreen-opened callback'i yalnız Lose reklamı için sayacı sıfırlar; failed/not-ready/disabled resetlemez. Gösterim callback timeout'u 60 saniyedir ve normal slot finish yolu fullscreen kilidini bırakır. No Ads reklamı atlar ve sayaç sıfır tutulur. Runtime result context'i reset/disable'da temizlenir; save counter etkilenmez.

Interstitial eşik kontrolü (cihaz/Play Mode testi çalıştırılmadı): varsayılan 19 ile level 18 Win → save 19 olsa da reklam yok; 18 Lose → sayaç dolsa da reklam yok; 19 Win → hazır reklam ve normal izinler varsa reklam; 19 Lose → normal lose interval koşulu geçerli. Eşiği 25 yaptığında 24/25 sınırında aynı sonuç beklenir. No Ads, doğrudan show çağrısı ve rewarded davranışı ayrıca doğrulanmalı. Inspector: mevcut AdsConfig asset'i → Interstitial → Interstitial Start Level.

Yerel doğrulama: kaynak koddaki eşik property'si izole C# harness içinde 9 sınır/context senaryosunu geçti (18/19, save'in win sonrası ilerlemesi, yüksek save ile düşük debug level, özel 25 eşiği ve doğrudan show context'i). Varsayılan 19, minimum 1 ve iki merkezi gösterim guard'ı statik olarak kontrol edildi. Unity MCP bu değişiklik sırasında erişilebilir değildi; bu test SDK veya Unity derleme/cihaz testi değildir.

Mevcut ad unit ID'lerini ve serialized placement uyumluluğunu korumak için `InterstitialGameToMenu` enum/asset alan adları tutulur; artık bu adlar bir trigger kuralı değildir. Level 19 No Ads sunum sırası, coin handoff ve Menu offer controller'ı değişmez.

## Fail → cleanup / offer

1. `IsLogicFailConditionMet`: conveyor full ve `TargetLaneController.CanConveyorStillFillAnyTarget(...)` false. Kontrol aktif target transferlerini, lane geçişlerini ve entry transferlerini de dikkate alır; yalnız renk sayımı veya kapasite doluluğu fail değildir.
2. Koşul `failDelay` boyunca sürerse `TriggerLose`, `Playing → Recovering` yapar; recovery session token üretir. `FailRecoveryRequested` yayınlanıp Clean Up offer planlanmadan önce `ConveyorController.PlayWarningGlow` mevcut pulse'ı eşikten bağımsız bir kez yeniden başlatır. Bu ek görsel uyarı offer gecikmesini veya recovery akışını değiştirmez; pulse bitişi beklenmez. Henüz can harcanmaz ve `level_failed` gönderilmez.
3. `FailRecoveryController` input/Settings etkileşimini kapatır. Varsa kayıtlı pending continue credit'i tüketmeyi dener; aksi durumda Inspector gecikmesinden sonra Clean Up offer gösterilir.
4. İlk Clean Up offer, `SaveKeys.FirstCleanupTutorialCompleted` (`first_cleanup_tutorial_completed`) false iken ücretsiz tutorial sunar. `FailRecoveryController` mevcut `TryStartCleanup(false, ...)` yoluyla yalnız gold kontrolü/harcamasını atlar; başarılı başlatmadan sonra setting kaydedilir. Offer açılması veya iptal flag tüketmez. Sonraki Clean Up düğmesi Inspector maliyetiyle gold cleanup başlatır; yetersiz gold `GameShopPanelController.OpenForInsufficientGold` yolunu açar.
   - Ücretsiz offer, owner bazlı ortak `Tutorial Info` üzerinde `General/tutorial.first_cleanup`, fiyat etiketinde `General/cleanup.free` gösterir. Quit geçici gizlenir; buton mevcut anchored Y değerinden -100 px kayar ve base scale → 1.1× → base → 1.1× → base → bekleme şeklinde unscaled DOTween loop oynar. Cleanup başlangıcı, cancel/clear ve disable tween/aboneliği temizleyip orijinal position/scale/Quit ve normal fiyatı geri yükler. GoldImage, recovery token, marble ownership ve diğer tutorial owner'ları değişmez.
5. Clean Up ekranındaki Quit doğrudan fail kesinleştirmez; Fail Offer panelini açar. Bu panelde Play On aynı gold cleanup'ını, premium düğmesi `StoreManager.Purchase(StoreProductIds.FailOffer)` yolunu kullanır.
6. Cleanup uygun conveyor/entry bilyelerini kendi sahiplik API'leriyle detach eder; renklere ve source kapasitesine göre recovered SourceBox'lara animasyonla toplar. `TryCompleteRecovery(token)` → `Playing`; input ve giriş kuyruğu devam eder. Yeni attempt/build yapılmaz.
7. Premium continue bağlantısı `StoreGameplayActionRegistry` / `IFailOfferContinueHandler` üzerinden aynı recovery token'ını kullanır. Pending purchase sırasında session lock; geçersiz oturum/ertelenmiş fulfillment için store credit yolu korunur.
8. Fail Offer kapatılınca `FinalizePendingFail`: `Recovering → Failed`, attempt başına can harcama, analytics fail ve lose event'leri. `FailOfferPanelController` yalnız preview sunumudur; recovery mantığına ikinci sahip değildir.

## Retry, vazgeçme ve clear

- Lose panelinde kesinleşen fail son canı tüketmişse Menu'ye dönülür; aksi durumda `LevelResultFlowController.TryAgain` → `LevelSessionController.RetryCurrentLevel` mevcut save level'ini yeniden kurar ve yeni attempt başlatır.
- `Assets/Game/Scripts/Runtime/UI/HUD/GameplayGiveUpPanelController.cs`: lives config kapalıysa, henüz move commit edilmemişse veya infinite lives varsa mevcut `LoadMenu` yoluyla doğrudan Menu; diğer durumda onay paneli, onayda attempt başına can harcama. `LivesService.IsEnabled` kapalıyken spend başarılı no-op'tur; can ve refill deadline değiştirilmez.
- `TryBeginExit` → `Exiting` → level clear → SFX stop → sahip olunan timeScale askısını bırakma; ardından SceneLoader geçişi gelir.
- `ClearLevel` sırası: `LevelClearing` bildirimi → booster reset → key/lock clear → entry queue → board-full mesajı → conveyor → source board → target lanes → capacity reset.
- Board objesini tek başına silmek bu transaction cleanup'ına eşdeğer değildir. Retry, debug load ve scene exit'te eski animasyon/offer callback'leri yeni oturuma ulaşmamalıdır.
- Debug displayed-level yükleme analytics suppression taşır; normal progression yoluna çevrilmemelidir. Analytics exit-snapshot davranışı [ANALYTICS.md](ANALYTICS.md) içindedir.

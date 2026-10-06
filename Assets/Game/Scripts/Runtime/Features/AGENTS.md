# Gameplay / board kuralları

Bu kapsam `Levels`, `SourceBoxes`, `Conveyor`, `TargetBoxes`, `BoardFeatures`, `BoardTiles`, `Spawners`, `Boosters` klasörlerini kapsar. Ortak yaşam döngüsü: [GAME_FLOW.md](../../../Docs/GAME_FLOW.md). Booster değişikliklerinde ayrıca [alt kuralları](Boosters/AGENTS.md) oku.

## Sorumluluk sınırları

- `Assets/Game/Scripts/Runtime/Features/Levels/LevelSessionController.cs`: displayed level ve gameplay state. `LevelBuildController.cs`: doğrulama, build/clear ve feature bağlantıları. `LevelResultFlowController.cs`: win/fail kararı ve ödül claim'i. Bu işleri yeni bir GameManager'da toplama.
- `Assets/Game/Scripts/Runtime/Features/SourceBoxes/SourceBoxBoardController.cs`: hücre doluluğu, source erişilebilirliği, spawn/release ve board feature koordinasyonu.
- `Assets/Game/Scripts/Runtime/Features/SourceBoxes/SourceBox.cs`: tek kutunun state'i, bilyeleri ve release lifecycle'ı. `Marble.cs`: fizik ve transfer sahipliği. `SourceBoxReleaseAnimator.cs` / `SourceBoxUnlockAnimator.cs`: sunum ve tamamlanma callback'leri.
- `Assets/Game/Scripts/Runtime/Features/Conveyor/ConveyorController.cs`: slotlar ve hareket; `ConveyorEntryZone.cs`: giriş kuyruğu/transfer rezervasyonları; `MarbleCapacityController.cs`: sahadaki aktif + rezerve bilye sayısı.
- `Assets/Game/Scripts/Runtime/Features/TargetBoxes/TargetLaneController.cs`: target kuyruğu, rezervasyon, varış, kutu tamamlama ve lane kaydırma. `TargetBox.cs` tek kutunun reserved/arrived state'ini tutar. Booster partial dosyaları aynı controller'ın parçalarıdır.

## Input ve source progression

- Normal dokunuşları `Assets/Game/Scripts/Runtime/Features/SourceBoxes/GameplayInputController.cs` üzerinden geçir: session, tutorial, booster ve UI raycast kontrolleri burada birleşir.
- Board source'larını `SourceBoxBoardController.TryReleaseSourceBoxWithResult` ile serbest bırak; connected pair koordinasyonunu atlayarak doğrudan `SourceBox` çağırma. Recovered source'lar mevcut input kodunda ayrı ele alınır.
- Koordinat düzeni `Vector2Int(row, column)`, index `row * ColumnCount + column` şeklindedir; x'i column sanma.
- Erişilebilirliği `RecomputeSourceBoxAvailability` hesaplar: başlangıç girişleri, gerçekten boşaltılmış hücre komşuları, özel progression izinleri, arrow ve connected pair kısıtları birlikte değerlendirilir. Her boş görsel hücre oynanabilir boş hücre değildir.
- Release başlangıcı ve bitişi farklıdır: başlangıçta feature bildirimleri/boşalma hazırlığı; bitişte hücre kaydı kaldırma, spawner replacement ve gift çözümü yapılır. Sadece objeyi yok ederek bu akışı atlama.
- Hand release normal komşu progression'ını bastırır; `RefreshOrphanedNeighborsAfterHandRemoval` özel kurtarma yoludur. Genel unlock döngüsüyle değiştirme.

## Kapasite ve transfer sözleşmesi

- Field capacity ile boş conveyor slot sayısı aynı şey değildir. Source release, multiplier çıktısını da içeren `RequiredReleaseCapacity` için önce rezervasyon yapar; animasyon başlayamazsa rezervasyon geri alınır.
- `ConveyorController.RemoveMarble` slotu/hareket kaydını kaldırır, field count'u azaltır ve `ContentsChanged` yayınlar. Çağıran tekrar `NotifyMarbleConsumed` yapmamalı.
- Giriş transferindeki bilyeyi uygun `ConveyorEntryZone.TryDetach...` API'sinden çıkar; kuyruğu veya rezervasyonu geride bırakma.
- Target slot reservation, gerçek arrival değildir. `TryBeginTargetTransfer` → slot reserve → önceki sahibinden detach → animasyon → `ConfirmTransferArrival` sırasını koru. Target dolumu, analytics progress ve feature bildirimi varış yolundan çıkar.
- Aynı bilyeyi iki sisteme verme. İptalde reservation/ownership kayıtlarını ve tween'leri beraber temizle; eski callback'lerin yeni level'e uygulanmasını önleyen kontrolleri koru.

## Board feature ve level verisi

- `SourceBoxBoardController` kendi plain C# feature controller'larını oluşturur: connected boxes, multiplier gate, gift box, crate, panel, arrow. Bunlar için ayrı MonoBehaviour manager ekleme.
- Key/lock koordinasyonu `LevelBuildController` içindeki `KeyLockFeatureController` tarafından source/target spawn ve target-front event'leriyle yürütülür.
- Panel açılması `TargetBoxFirstFilled`, crate/gift/arrow ilerlemesi source release bildirimleriyle bağlıdır. Yeni feature'ı mevcut prepare/build/notify/clear hattına dahil et.
- Crate/panel altında sealed source'lar reveal tamamlanmadan etkileşime açılmaz; gift çözümü de input/availability'yi geçici olarak yönetir.
- Çalışan connected source kodu `Assets/Game/Scripts/Runtime/Features/BoardFeatures/ConnectedBoxBoardController.cs` ve `ConnectionController.cs` dosyalarındadır. `BoardFeatures/ConnectedBoxes/` içindeki aynı adlı dosyalar GUID korumak için `#if false` altındadır; onları uygulama dosyası sanma.
- Connected target işlemleri farklıdır: `Assets/Game/Scripts/Runtime/Features/TargetBoxes/ConnectedTargetGroupController.cs` ve `ConnectedTargetPresentationSystem.cs` kullanılır.
- Level modeli değişirse `Levels/LevelDefinitionValidator.cs`, `Assets/Game/Scripts/Editor/LevelManagement/GameLevelJsonModels.cs` ve import adapter'larını birlikte değerlendir. Runtime build için `ValidateForRuntimeBuild` yolunu koru.
- Inspector sahipleri: board prefab/root/camera/catalog referansları board controller'larında; feature prefab/ayarları `BoardFeatureCatalog`, renk görselleri `MarbleColorCatalog`; transfer süre/ease/scale değerleri ilgili animator veya target controller'dadır.

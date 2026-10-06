# Booster execution sözleşmesi

Bu klasörün dosyaları `Assets/Game/Scripts/Runtime/Features/Boosters/` altındadır. Önce [gameplay kurallarını](../AGENTS.md) oku. Ortak coordinator `BoosterController.cs`; mevcut tipler Hand, Shuffle ve Ufo'dur.

## Ortak lifecycle

```text
BoosterHudController → TryActivateBooster + activation guard
  Hand: Targeting → geçerli source/transfer → Running
  Shuffle / Ufo: Running
Running → tipe özgü işlem + target/board callback'leri
  → NotifyCompleted veya NotifyCancelled → Idle
```

- `BoosterController` yalnızca bir aktif booster tutar. Başlatma için session `Playing` olmalı ve mantıksal fail koşulu oluşmamış olmalıdır; tipe özgü activation guard da geçmelidir.
- Input kilidi coordinator state'inden gelir: `Running` normal gameplay input'unu engeller; Hand `Targeting`, mevcut `GameplayInputController` hit test'ini Hand'e yönlendirir. Targeting sırasında bütün simülasyonun durduğunu varsayma.
- Aktivasyonda `ActiveLifecycleVersion` yakala; execution/completion/cancellation bildirimlerine aynı değeri ver. Reset, bitiş ve yeni aktivasyon eski callback'leri geçersiz kılar.
- `LevelBuildController.ClearLevel`, coordinator disable ve `Recovering/Won/Failed/Exiting` state'leri reset yollarıdır. `Paused` aynı terminal reset değildir.
- Guard/event aboneliklerini lifecycle sonunda kaldır. Yeni tween/rezervasyon/geçici görsel için hem tamamlanma hem iptal cleanup'ı tanımla.

## Envanter, UI ve tutorial

- `Assets/Game/Scripts/Runtime/UI/HUD/BoosterHudController.cs` unlock/stock kontrolünü, seçili görseli ve envanter tüketimini yönetir.
- `Running` bildirimiyle `SaveManager.UseBooster` lifecycle başına bir kez çağrılır. Hand hedefleme açılışında tüketilmez. Child controller'da ikinci tüketim veya yeni otomatik refund davranışı ekleme.
- `booster_used`, tüketim sırasında yakalanan level context'iyle `BoosterCompleted` üzerinden gönderilir. İptal, başarı event'i değildir.
- Envanter ID'leri `hand`, `shuffle`, `ufo`; bunlar save/store/analytics ile paylaşılır.
- Satın alma/offer metadata'sı `Assets/Game/Scripts/Runtime/UI/HUD/AddBoosterPanelController.cs` içindedir. Unlock sunumu ve izinler `Assets/Game/Scripts/Runtime/Tutorial/BoosterUnlockTutorialController.cs` ile bağlıdır; HUD ve tutorial kontrollerini bypass etme.

## Tipe özgü uygulama noktaları

### Hand

- `HandBoosterController.cs` hedef preview/indicator ve mystery reveal'i yönetir. Geçerlilik ve connected partner seçimi board'un `IsValidHandBoosterTarget` / `TryGetHandDirectTransferSources` API'lerinden gelir.
- Mevcut seçim locked runtime source gerektirir; multiplier etkisindeki source veya partner uygun değildir. Yeni hit test ile bu kısıtları atlama.
- `Assets/Game/Scripts/Runtime/Features/TargetBoxes/TargetLaneController.HandTransfer.cs` bütün source bilyeleri için transfer planı/slot rezervasyonu kurar. Bu doğrudan target transferidir; normal conveyor release'iyle değiştirme.
- Aktif target transferi yüzünden bloke olan seçim `TargetTransferStateChanged` ile tekrar denenir. Gerçek commit sonrası board'a `NotifyHandDirectTransferCommitted`, coordinator'a `NotifyExecutionStarted` bildirilir.
- Source bitişi, target arrival/completion batch ve lane devamı tamamlanma zincirinin parçalarıdır; yalnızca son uçuş tween'i bitince booster'ı bitirme.

### Shuffle

- `ShuffleBoosterController.cs` kullanıcı tarafından release denenebilen source renklerini toplar; `TargetLaneController.Shuffle.cs` planlama, gather, queue commit, redistribute, front reveal ve cancellation'ı yönetir.
- Shuffle target kuyruklarını yeniden sıralar; source renklerini veya level asset'ini rastgele değiştirmez. Planı, uygunluk filtrelerini ve rollback yolunu target controller'da tut.
- Süre/ease/stagger alanları `TargetLaneController.Shuffle.cs` içinde serialized alanlardır; booster controller'a ikinci animasyon ayarı ekleme.

### UFO

- `UfoBoosterController.cs`: entry → collect → chamber hold → hedef rezervasyonu/aim/fire → arrival bekleme → completion/exit.
- `CanActivateUfo` hedef kapasitesi ve yakalanabilir bilyeleri kontrol eder. Conveyor ve aktif entry transferleri snapshot'a alınır; toplama hedeflerin renk başına kapasitesiyle sınırlıdır.
- Sahipliği önce `Marble.TryBeginUfoTransfer`, sonra conveyor remove veya entry detach ile alır. Fizik/hareket freeze ve restore mevcut controller'ın sorumluluğudur; `Time.timeScale` ile ikinci freeze kurma.
- `Assets/Game/Scripts/Runtime/Features/TargetBoxes/TargetLaneController.Ufo.cs` hedef rezervasyonu ve ortak arrival hattına giriş sağlar. Hedef bekleme, iptal edilen reservation ve geri bırakma yollarını koru.
- Completion çıkış animasyonunu ve cleanup'ı da içerir. Sorting, mask, scale, fizik ve sahiplenilmiş bilyeler restore/discard kararına tabidir; tween kill tek başına cleanup değildir.
- UFO referansları ve entry/collect/fire/exit süreleri kendi controller Inspector'ındadır; target'a özgü transfer ayarlarının sahibi target controller'dır.

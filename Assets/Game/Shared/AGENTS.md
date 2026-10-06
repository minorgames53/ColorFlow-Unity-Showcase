# Ortak altyapı kuralları

`Shared` sahneler arası servisleri ve tekrar kullanılan UI'ı barındırır. Sorumluluk haritası, config/audio ve animasyon ayrıntıları: [ARCHITECTURE.md](../Docs/ARCHITECTURE.md). Event/provider değişikliklerinden önce [ANALYTICS.md](../Docs/ANALYTICS.md) oku.

## Servis ve kayıt sahipliği

- `Assets/Game/Shared/Bootstrap/SharedSystemsBootstrap.cs` mevcut save, audio, haptics, ads, store ve navigation sahiplerini çözer; `LivesService` / `LivesPurchaseService` burada oluşturulur. Feature için ikinci kalıcı bootstrap kurma.
- `AnalyticsBootstrap`, `CrashReportingBootstrap` ve `DeveloperPanelBootstrap` kendi lifecycle sahipleridir; hepsini SharedSystemsBootstrap'ın başlattığını varsayma.
- Save değişikliklerini `Assets/Game/Shared/Save/SaveManager.cs` API'lerinden yap. `Data` canlı modeldir; doğrudan yazmak dirty state, event ve doğrulamayı atlar.
- Kalıcı alan değişikliğinde `GameSaveData`, `Data/`, `GameSaveDataFactory`, `GameSaveDataNormalizer`, `SaveJsonSerializer`, `SaveVersion` ve load/write yolunu birlikte incele. Var olmayan bir migration service varsayma.
- `SaveManager.IsWriteBlocked` / `TrySave` sonucunu ve işlem geri alma yollarını koru. Her setter'ın anında diske yazdığını varsayma; kritik işlem commit'inde mevcut save sınırını izle.
- Can ekonomisini `Assets/Game/Shared/Lives/LivesService.cs` ve `LivesPurchaseService.cs` üzerinden yürüt; refill zamanı için `Assets/Game/Shared/Time/ITimeProvider.cs` soyutlamasını koru.

## Store / ads

- IAP giriş noktası `Assets/Game/Shared/Store/Core/StoreManager.cs`; ürünler `StoreCatalog.cs` / `StoreProductIds.cs`, fulfillment `PurchaseProcessor.cs` ve `Assets/Game/Shared/Store/Rewards/StoreRewardProcessor.cs` içindedir.
- Transaction deduplication, persistent reward kaydı, pending fulfillment ve store confirmation sırasını bypass etme. UI callback'inde tekrar ödül verme veya purchase analytics gönderme.
- Fail offer gameplay bağlantısı `Assets/Game/Shared/Store/Rewards/StoreGameplayActionRegistry.cs` / `IFailOfferContinueHandler` üzerinden session token taşır. Geçersiz oturuma continue uygulama; mevcut pending credit ve session-lock yollarını koru.
- Reklamları `Assets/Game/Shared/Ads/Core/AdsService.cs` ve `AdPlacement` üzerinden çağır. Reward earned ve ad finished callback'leri farklıdır; sadece kapanıştan ödül üretme.
- Tenjin, consent ve developer erişim kuralları için root haritasındaki mevcut integration README'lerini kullan.

## Ortak UI sözleşmesi

- `Assets/Game/Shared/UI/Panels/PanelManager.cs` root/push/pop/overlay stack'ini ve arka plan blocker'ını yönetir; `UIPanel.cs` panelin açılma/kapanma tween'ini yönetir. Normal akışta yalnızca `SetActive` ile panel stack'ini bypass etme.
- Blocking modal varken yeniden kullanılan panelin context'ini değiştirmeden önce `PanelManager.TryDeferOpen` kontrolünü yap. Ertelenmiş callback'te ilgili level/session'ın hâlâ geçerli olduğunu denetle.
- `PanelManager.Blocking.cs` modal owner ve ertelenmiş istek kuyruğunu korur. `UIInputLockService` sayaçlı kilit kullanır; yalnızca aldığın kilidi bırak. Modal input izni mevcut lock sayısını sıfırlamaz.
- UI kilidi gameplay kilidiyle aynı değildir. Gameplay input'un sahibi `GameplayInputController`, pause state'inin sahibi `LevelSessionController`dır.
- Button/panel animasyonu için `TweenButton`, `UIPanel`, `UIConfig`; idle loop için `UIIdleTweenAnimator`; gold sunumu için `GoldDisplayPresenter` / `CoinFlyAnimator` kullan.
- `CoinFlyAnimator` ve `CoinFlyPresentationHandoff` sunum içindir. Ödül kaydını animasyonun tamamlanmasına veya Menu'ye taşınmasına bağlama.
- LocalizedString/TMP sunumunu ve enable/disable event aboneliklerini mevcut presenter yaklaşımıyla sürdür. Ortak UI'a level sonucu kararı ekleme; oyun özel ekranları `Assets/Game/Scripts/Runtime/UI` veya `Menu` altına yerleştir.

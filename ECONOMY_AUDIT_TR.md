# Color Flow — Mevcut Economy, Progression ve Monetization Raporu

İnceleme tarihi: 17 Eylül 2026. Kapsam: çalışma klasöründeki mevcut implementasyon ve Unity Editor üzerinden okunan asset/scene/prefab bağlantıları.

## İnceleme sınırı ve kanıt düzeyi

Oyun kodu, scene, prefab, ScriptableObject, ProjectSettings, paketler ve oyuncu kaydı değiştirilmedi. Bu rapor tek yeni dosyadır. Çalışma klasöründe önceden bulunan değişiklikler korunmuştur.

Unity MCP ile bağlı **Color Flow / Unity 6000.3.16f1** oturumu incelendi. Editor **Boot sahnesinde, Play Mode kapalı** durumdaydı. Boot'un yüklü nesneleri, gerçek config asset'leri ve prefab bileşenleri okundu; Game/Menu sahnelerinin kayıtlı bağlantıları ve prefab override'ları MCP üzerinden salt okunur incelendi. Game/Menu çalıştırılmadı ve satın alma/reklam tetiklenmedi. Dolayısıyla aşağıdaki oyuncu akışları, kod ile kayıtlı wiring'in birlikte izlenmesinden çıkarılan davranıştır; telefonda oynanmış bir test sonucu değildir.

**Kesin doğrulanamadı:** Google Play/App Store'daki güncel ürün fiyatları ve ürünlerin yayımlanma durumu; gerçek reklam doluluk oranı/mediation waterfall; cihazdaki consent, store ve reklam callback sırası; yayımlanmış Remote Config içeriği. Bunlar yerel kod veya Editor mock'uyla kanıtlanamaz.

Kaynak etiketleri:

- **[Kod]**: hardcoded sabit veya kod kuralı.
- **[Inspector]**: sahne/prefab üzerinde kayıtlı serialized değer.
- **[SO]**: Unity üzerinden okunan ScriptableObject değeri.
- **[Hesap]**: bu kaynaklardan runtime'da hesaplanan değer.
- **[Mağaza]**: store SDK metadata'sı; gerçek mağaza oturumu olmadan sayısal fiyat kesin değil.

## 1. Coin / Gold economy

Oyundaki ana para UI'da coin/altın olarak sunuluyor, kayıtta **gold.amount** olarak tutuluyor. Ayrı bir ikinci premium currency bulunmadı.

### Başlangıç ve kaynaklar

Yeni oyuncu **500 coin** ile başlar [Kod: `GameSaveDataFactory.InitialGold`]. Bu, mevcut oyuncunun her açılışta aldığı bir ödül değildir; yeni save oluşturulurken verilen başlangıç bakiyesidir.

| Oyuncu akışı | Balance etkisi | Kaynak |
| --- | ---: | --- |
| Yeni save | +500 | Kod |
| Level Win → Normal → base claim | +25 | Kod: `LevelWinRewardConfig` |
| Level Win → Hard → base claim | +50 | Kod |
| Level Win → Very Hard → base claim | +75 | Kod |
| Normal Win → başarılı Double Gold reklamı | **Toplam +50** | Kod × runtime multiplier |
| Hard Win → başarılı Double Gold reklamı | **Toplam +100** | Kod × runtime multiplier |
| Very Hard Win → başarılı Double Gold reklamı | **Toplam +150** | Kod × runtime multiplier |
| Coin IAP | +1.000 / 5.000 / 10.000 / 25.000 / 50.000 / 100.000 | Kod: `StoreCatalog` |
| Starter Pack | +2.500 | Kod |
| Fail Offer | +1.500 | Kod |
| Ücretli cleanup hiçbir bilyeyi devralamazsa | Harcanan 900 iade edilir | Inspector maliyet + kod rollback |

Double Gold, önceden verilmiş base ödülün üzerine ayrıca 2× ödeme değildir: base ve double birbirini dışlayan claim seçenekleridir. Toplam 2× alınır; 3× alınmaz.

Streak, günlük coin, mystery box coin, level içinden coin toplama, rastgele coin bonusu, remaining-move bonusu veya ayrı shop free-coin kaynağı **bulunmadı**. Gameplay'deki Gift Box ve Multiplier Gate, bilye/board mekaniğidir; currency ödülü değildir.

### Harcamalar

| Oyuncu akışı | Maliyet | Alınan karşılık | Kaynak |
| --- | ---: | --- | --- |
| Refill → Buy | Eksik can ×120 | Canlar maksimuma dolar | Kod + hesap |
| Hand stok 0 → Add Booster → Buy | 1.800 | 3 Hand | Game sahnesi Inspector |
| Shuffle stok 0 → Add Booster → Buy | 1.800 | 3 Shuffle | Game sahnesi Inspector |
| UFO stok 0 → Add Booster → Buy | 2.600 | 3 UFO | Game sahnesi Inspector |
| Fail recovery → Clean Up | 900 | Aynı attempt içinde cleanup/continue | Game sahnesi Inspector |
| Fail Offer → Play On | 900 | Aynı cleanup/continue | Aynı Inspector alanı |

Normal level başlangıcı, win, standart retry ve finalize edilmiş fail **doğrudan coin düşürmez**. Fail sırasında ücretli kurtarma seçilirse ayrı 900 coin sink'i oluşur.

### Balance değişimi ve animasyon

Gerçek bakiye `SaveManager` tarafından değiştirilir. `CoinFlyAnimator.Play()` para vermez: o anda mevcut authoritative bakiyeyi okur, eklenen ödülü çıkararak görsel başlangıç sayısını hesaplar ve UI sayısını gerçek bakiyeye doğru animasyonla taşır. İptalde görünümü gerçek bakiyeye eşitler.

Level 1–9: win paneli açıldığında base ödül otomatik claim edilir. Level 10 ve sonrası: Continue veya başarılı Double Gold callback'i claim eder; coin uçuşu Menu'ye taşınabilir. `CoinFlyPresentationHandoff` kalıcı ödül kuyruğu değil, RAM'deki görsel sunum kaydıdır. Uçuşu iptal etmek, verilmiş coin'i geri almaz; Menu'de uçuşu oynatmak ikinci kez coin vermez.

### Public entry point'ler ve kontroller

Balance mutasyonunun ana public API'leri:

| API | Davranış |
| --- | --- |
| `SaveManager.AddGold(int)` | Pozitif tutar ekler; 0/negatifi yok sayar; toplamı `int.MaxValue` ile doyurur. |
| `SaveManager.SpendGold(int)` | Negatif maliyet veya yetersiz bakiye → false, değişiklik yok. 0 → true. Başarılıysa tutarı düşürür. |
| `SaveManager.SetGold(int)` | Bakiyeyi en az 0 olacak şekilde ayarlar. |

Üst seviye public para etkili yollar: `LevelResultFlowController.TryClaimBaseWinReward()`, `TryClaimDoubleWinReward()`, gerektiğinde claim yapan `Continue()`; `LivesPurchaseService.TryFillToMaxWithGold()`; `StoreManager.Purchase()` üzerinden `PurchaseProcessor.Process()` → `StoreRewardProcessor.ApplyPersistentRewards()`; Editor/development için `PurchaseProcessor.DebugGrant()`. Booster satın alma ve ücretli cleanup private UI handler'larından yukarıdaki SaveManager API'lerine ulaşır. Developer Save paneli `SetGold()` kullanır; oyuncunun normal coin kaynağı değildir.

`ResetSave()` / `DeleteSave()` yeni default modele döndürür; `Reload()` / `ReloadFromProvider()` kayıtlı bakiyeyi yeniden yükler. `SaveManager.Data` canlı model olarak public okunabilir; doğrudan alan yazımı teknik olarak mümkün olsa da dirty/event kontrollerini atlar ve normal economy akışının kullandığı giriş değildir.

`AddGold`, `SpendGold`, `SetGold` kendi başlarına yalnızca dirty state ve event üretir; her çağrı otomatik disk commit'i değildir. Ayrıntı bölüm 10'da.

Referanslar: [SaveManager.cs](Assets/Game/Shared/Save/SaveManager.cs), [GameSaveDataFactory.cs](Assets/Game/Shared/Save/GameSaveDataFactory.cs), [LevelWinRewardConfig.cs](Assets/Game/Scripts/Runtime/Features/Levels/LevelWinRewardConfig.cs), [LevelResultFlowController.cs](Assets/Game/Scripts/Runtime/Features/Levels/LevelResultFlowController.cs), [CoinFlyAnimator.cs](Assets/Game/Shared/UI/Display/CoinFlyAnimator.cs).

## 2. Lives / Heart sistemi

Oyuncu **5/5 canla** başlar [Kod]. Normal yeni kayıt için maksimum **5**; runtime maksimumu `lives.max` alanından okunur, her açılışta yeniden 5'e zorlanmaz. Normal oyuncu akışında maksimum artıran ürün veya mekanizma bulunmadı.

| Durum | Can etkisi |
| --- | --- |
| Level başlatma | 0; girişte can rezerve edilmez |
| Win | 0 |
| Fail koşulu oluşması / recovery ekranı | Henüz 0 |
| Coin cleanup başarılı | 0; aynı attempt devam eder |
| Fail Offer continue başarılı | 0; aynı attempt devam eder |
| Fail Offer kapatılır → fail kesinleşir | −1 |
| Lose paneli → Retry | Ek tüketim yok; −1 zaten finalize aşamasında |
| Hamle yapmadan Menu'ye çıkma | 0 |
| Hamle/booster commit edildikten sonra Give Up onayı | −1; lose olmadan can düşebilir |
| Unlimited lives aktifken fail / give up | Gerçek can düşmez |
| Aynı attempt için ikinci life-spend çağrısı | Attempt flag'i nedeniyle engellenir |

`HasSpentLifeForCurrentAttempt` tekrar düşümü engeller. Bu flag RAM'dedir ve level build sırasında sıfırlanır. Unlimited durumda `TrySpendLife()` başarılı kabul edilir ama normal can sayısı azalmaz.

### Refill ve offline zaman

**Her 20 dakika → +1 can**, en çok maksimuma kadar [Kod: `LivesConfig.RefillDuration`]. İlk eksilmede `nextRefillUtc = UTC now + 1.200 saniye`. Sonraki kayıplar geçerli timer'ı başa almaz.

Offline regeneration vardır: açılışta, foreground dönüşünde ve gerektiğinde state okumalarında elapsed interval sayısı hesaplanır. Örneğin 0 can ve yeni başlamış timer ile 100 dakika sonra 5 can olur. Dolunca `nextRefillUtc = 0`; fazla süre geleceğe can kredisi olarak birikmez. Kısmi refill'de mevcut sonraki tick zamanı korunur.

Saat kaynağı **`DateTime.UtcNow`**, yani cihaz saatidir. Projede `ITimeProvider` bulunmasına rağmen mevcut `LivesService` bunu inject ederek kullanmıyor. Server time doğrulaması yok.

App kapanması canları sıfırlamaz veya tam doldurmaz. Kayıtlı can/timer yüklenir ve geçen süre uygulanır. Oyun devam ederken uygulamayı kapatmak için ayrıca −1 life kuralı bulunmadı. Force-kill ile graceful pause/quit save'i aynı garantiyi vermez.

### Oyuncunun gördüğü refill akışı

```text
Menu → Play / mevcut level düğmesi → 0 can
→ LifePanel açılır, level yüklenmez
→ Buy: 600 coin → +5 can → heart fly → popup kapanır
→ Oyuncu Play'e tekrar basar → level başlar
```

```text
0 can → LifePanel → Rewarded Life hazırsa reklam
→ reward-earned callback → +1 can ve save
→ reklam kapanışı → 1 heart fly
→ popup, canlar hâlâ eksikse açık kalır
→ oyuncu kapatıp Play'e basabilir veya sonraki hazır reklamı izleyebilir
```

**120 coin → +1 can düğmesi yoktur.** Birim fiyat 120'dir; Buy her zaman eksikleri tamamlar:

| Mevcut can | Buy fiyatı | Verilen can |
| ---: | ---: | ---: |
| 0 | 600 | 5 |
| 1 | 480 | 4 |
| 2 | 360 | 3 |
| 3 | 240 | 2 |
| 4 | 120 | 1 |
| 5 | Refill gerekmez | 0 |

Yetersiz coin → Menu refill popup'ı kapanır → Shop sekmesi açılır. Coin satın almak kendi başına can vermez; refill işlemi ayrıca yapılır. Mevcut scene wiring'inde LifePanel **Menu'de** bağlıdır. `GameLivesRefillHost` sınıfı vardır ancak Game sahnesinde bağlı çalışan örneği bulunmadı.

Rewarded Life, `CurrentLevel > 10` olduktan sonra görünür; stokta hazır reklam, eksik can ve unlimited olmaması gerekir. **Bir reklam +1 can**, +5 değildir. Reklam boyunca doğal regen dolumu tamamlarsa ödül cap nedeniyle 0 olabilir; FX sayısı gerçek artıştan hesaplanır.

Son canın fail ile tüketilmesi lose ekranındaki aksiyonu Menu'ye dönüşe çevirir. LifePanel doğrudan fail anında otomatik açılan ekran değildir; Menu'de Play veya can alanı üzerinden açılır.

### Unlimited lives

Starter Pack **60 dakika** unlimited verir [Kod]. `store.infiniteLivesEndUtc` ile saklanır. Yeni süre `max(now, mevcut bitiş) + süre` şeklinde eklenebilir. Aktifken life purchase/rewarded refill gerekmez, `HasLives` true döner; normal canların doğal regeneration'ı ayrı olarak devam edebilir. Süre app kapalıyken de akar.

Önemli giriş istisnası: can kontrolü `MenuPlayController` içindedir; `LevelSessionController.StartCurrentLevel/RetryCurrentLevel` içinde genel life gate yoktur. Boot, kayıtlı level 1–10 ise doğrudan Game'e gider. Bu yüzden erken progression'da **0 canla app yeniden açılışı Game başlangıcını engellemez**; ayrıntı riskler bölümünde.

Referanslar: [LivesService.cs](Assets/Game/Shared/Lives/LivesService.cs), [LivesPurchaseService.cs](Assets/Game/Shared/Lives/LivesPurchaseService.cs), [LivesEconomyConfig.cs](Assets/Game/Shared/Lives/LivesEconomyConfig.cs), [LivesRefillPanelController.cs](Assets/Game/Shared/Lives/UI/LivesRefillPanelController.cs), [MenuPlayController.cs](Assets/Game/Scripts/Runtime/Menu/MenuPlayController.cs), [GameplayGiveUpPanelController.cs](Assets/Game/Scripts/Runtime/UI/HUD/GameplayGiveUpPanelController.cs).

## 3. Booster economy

Üç gerçek booster vardır: **Hand, Shuffle, UFO**. Clean Up ayrı bir fail-recovery satın almasıdır; booster inventory öğesi değildir.

| Booster / save ID | Açılma | Yeni save stoku | Unlock tutorial hediyesi | Coin paketi | Rewarded | IAP / bundle | Kullanım |
| --- | ---: | ---: | --- | --- | --- | --- | --- |
| Hand / `hand` | Level 6 | 0 | Stok 0 ise +1 | 1.800 coin → ×3 | Bulunmadı | Starter Pack +1; Fail Offer +1 | Level sırasında hedef seçme ve doğrudan target transferi |
| Shuffle / `shuffle` | Level 8 | 0 | Stok 0 ise +1 | 1.800 coin → ×3 | Bulunmadı | Starter Pack +1; Fail Offer +1 | Level sırasında target kuyruklarını yeniden sıralama |
| UFO / `ufo` | Level 11 | 0 | Stok 0 ise +1 | 2.600 coin → ×3 | Bulunmadı | Starter Pack +1; Fail Offer +1 | Conveyor/entry bilyelerini uygun target kapasitesine taşıma |

Unlock level ve coin fiyat/adetleri **Game.unity Inspector** değerlerinden doğrulandı. Kod default unlock'ları da 6/8/11; fiyatlar ise `BoosterOffer.goldCost` alanında kod default 0 olup sahnede yukarıdaki gerçek değerlerle doldurulmuş.

### Tutorial, kullanım ve tüketim

Tutorial tam unlock level'ında ve `boosters[id].unlocked == false` iken başlar. Bilgi paneli/kilit açılma animasyonu ve yönlendirilmiş kullanım vardır. Hand için sahnede hedef koordinatı `(row=1, column=2)`; controller Level 6 pink source'u doğrular. Stok zaten pozitifse ekstra +1 hediye verilmez. Bu nedenle erken satın alınmış bundle stoğu varsa tutorial mevcut stoktan tüketebilir.

Tutorial tamamlanma işareti `unlocked=true` olarak kaydedilir. HUD kullanım kilidi ayrıca **displayed level ≥ unlock level** hesabıyla açılır. Bunlar aynı kavram değildir: `unlocked` alanı tutorial geçmişi olarak kullanılır; satın alınmış booster tek başına level kilidini kaldırmaz.

Level öncesi booster seçme veya pre-level booster paketi bulunmadı. HUD'da stok 0 olan açılmış booster'a dokunmak Add Booster panelini açar. Satın alma sonrasında stok artar, panel kapanır; booster otomatik kullanılmaz.

Tüketim `BoosterHudController.TryConsumeRunningBooster()` içinde, coordinator **Running** olduğunda `UseBooster(id)` ile **−1**, lifecycle başına bir kez gerçekleşir. Hand targeting açılışı tüketmez; geçerli transfer commit'inde Running'e geçer. Targeting iptali ücretsizdir. Shuffle/UFO Running'e geçince tüketir. Çalışmaya başladıktan sonraki iptal, fail, retry veya sahneden çıkış için otomatik stok iadesi bulunmadı.

Booster aktivasyonu Playing state, yeterli stok, level kilidi, tutorial/input izinleri, logical fail olmaması ve tipe özgü uygun hedef kontrollerine bağlıdır. Aynı anda bir booster aktiftir.

Normal level-win booster ödülü **bulunmadı**. Günlük booster/free shop booster **bulunmadı**. Stok için tasarımsal quantity cap yok; integer taşmasını önlemek için `int.MaxValue` saturating cap var. Negatif stok normalize edilir, yetersiz stokta kullanım başarısız olur.

Save: `boosters[]` içindeki `id`, `amount`, `unlocked`, `values[]`. Add/Use/Set çoğunlukla dirty işaretler; tutorial completion açıkça Save yapar, IAP transaction commit'i kaydeder. Normal coin ile booster alımı ve kullanımında ayrı anlık Save yoktur.

Referanslar: [BoosterHudController.cs](Assets/Game/Scripts/Runtime/UI/HUD/BoosterHudController.cs), [AddBoosterPanelController.cs](Assets/Game/Scripts/Runtime/UI/HUD/AddBoosterPanelController.cs), [BoosterUnlockTutorialController.cs](Assets/Game/Scripts/Runtime/Tutorial/BoosterUnlockTutorialController.cs), [BoosterController.cs](Assets/Game/Scripts/Runtime/Features/Boosters/BoosterController.cs), [Game.unity](Assets/Game/Scenes/Game.unity).

## 4. Rewarded ads — bütün oyuncu placement'ları

Kod çağrı noktaları ve `AdPlacement` enum'u birlikte tarandığında **iki** oyuncu placement'ı bulundu.

| Placement | UI / erişim | Koşul | Ödül | Limit / cooldown |
| --- | --- | --- | --- | --- |
| `RewardedLife` | Menu LifePanel | CurrentLevel >10, eksik can, unlimited değil, reklam hazır | +1 can; max'a clamp | Günlük/session/level sayacı yok; can kapasitesi ve ad readiness sınırlar |
| `RewardedDoubleGold` | Win paneli Double Reward | CurrentLevel >10, o win henüz claim edilmemiş, reklam hazır | Toplam 50 / 100 / 150 coin | Her win için tek claim; günlük/session cooldown yok |

Level 10 kazanıldığında progression önce 11 olur. Dolayısıyla **ilk Double Gold fırsatı Level 10 win ekranıdır**; Level 11'i kazanmayı beklemez. Level 1–9 ödülleri otomatik claim edilir ve rewarded henüz açılmamıştır.

### Callback ve unavailable davranışı

`AdsService.ShowRewarded()` → `RewardedAdController` → `RewardedAdSlot` → **Google Mobile Ads / AdMob `RewardedAd.Load()` ve `RewardedAd.Show(rewardCallback)`**. Proje tarafındaki giriş bu SDK'dır. Gerçek mediation network listesi/waterfall kesin doğrulanamadı.

Ödül yalnızca **reward-earned callback** üzerinden verilir. Sadece reklamın kapanması, Show çağrısının yapılması veya satın alma benzeri bir UI animasyonu ödül vermez. SDK'nın callback içindeki reward amount/type değeri oyun bakiyesine aynen uygulanmaz; oyun placement'a göre kendi +1 veya 2× değerini kullanır.

Ad hazır değilse ilgili düğme pasif; unlock öncesi gizlidir. Çağrı sırasında şart değişirse `Disabled`, `NotReady`, `Busy` veya `Failed` sonuçları dönebilir. Fullscreen lock aynı anda iki reklamı engeller. UI'da `actionInProgress`, request version ve reward-granted flag'leri vardır. Slot'ta reward/finished callback'leri ayrı ayrı yalnız bir kere çalıştırılır.

Başarısız/ödülsüz kapanışta coin/life verilmez, UI yeniden kullanılabilir hale gelir. Win'de oyuncu base Continue seçeneğini kullanabilir. Life'ta coin satın alma veya timer alternatifi kalır. Reward callback geldikten sonra ad kapanışı başarısız raporlansa bile **zaten kazanılmış ödül geri alınmaz**; bu, failure'dan ödül üretilmesi değildir.

Life ödülü callback'te uygulanıp kaydedilir; heart fly normalde `Closed` sonrası oynar. Double coin de earned callback'te save edilir; Menu'ye geçiş finished callback'ini bekler.

Günlük sayaç, server-side reward claim kaydı, reklam başına kalıcı claim ID'si veya per-level ad izleme limiti bulunmadı. Win claim koruması o runtime sonuç nesnesinin flag'idir. Late callback ayrıntısı riskler bölümünde.

### Özellikle aranan diğer alanlar

| Alan | Sonuç |
| --- | --- |
| Life refill | Var: +1 |
| Bağımsız sabit coin reklamı | **Bulunmadı** |
| Win extra reward / multiplier | Var: toplam 2× |
| Booster rewarded | **Bulunmadı** |
| Continue / revive rewarded | **Bulunmadı**; coin veya Fail Offer IAP kullanılıyor |
| Shop free/rewarded offer | **Bulunmadı** |
| Daily reward / daily ad | **Bulunmadı** |
| Fail panelinde doğrudan rewarded | **Bulunmadı** |
| Menüde bağımsız rewarded offer | **Bulunmadı**; LifePanel içindeki placement var |

Developer Ads panelinde aynı placement'ları test eden public çağrılar vardır; debug callback'leri normal oyuncu ödül kaynağı olarak sayılmadı.

Referanslar: [AdsService.cs](Assets/Game/Shared/Ads/Core/AdsService.cs), [AdPlacement.cs](Assets/Game/Shared/Ads/Core/AdPlacement.cs), [RewardedAdSlot.cs](Assets/Game/Shared/Ads/Rewarded/RewardedAdSlot.cs), [LevelCompleteCanvasView.cs](Assets/Game/Scripts/Runtime/UI/HUD/LevelCompleteCanvasView.cs).

## 5. Interstitial ads ve erişim etkisi

Tek placement **`InterstitialGameToMenu`**. İlk uygun nokta **Level 19 kazanımı sonrası Game → Menu geçişidir**: kazanma sırasında save currentLevel 20 olur ve `CurrentLevel >19` koşulu sağlanır [Kod].

Gösterim bir “her N level” sayacıyla çalışmaz. Şartları sağlayan **her Game → Menu geçişinde** denenir. Bu nedenle win dönüşü, son canı tüketen fail dönüşü veya Give Up/hamlesiz Menu dönüşü tetikleyebilir. Canı kalan oyuncunun aynı sahnedeki Retry'si ve Menu → Game başlangıcı interstitial tetiklemez.

Şartlar: monetization açılmış, No Ads yok, **IAP No Ads entitlement sync başarılı**, ads altyapısı uygun ve reklam gösterilebilir durumda. Store entitlement bilinmiyorsa oyuncunun satın almış olabileceği No Ads korunarak interstitial gösterilmez. Bu nedenle Level 19 sonrası gerçek reklamın kesin görünmesi garanti değildir.

| Kural | Mevcut davranış |
| --- | --- |
| Level interval | Yok; Game→Menu geçiş temelli |
| Gösterimler arası zaman cooldown'u | Bulunmadı |
| Session ilk reklam/grace timer | Bulunmadı |
| Yeni oyuncu koruması | CurrentLevel ≤19 iken interstitial yok |
| Rewarded sonrası suppression | **Bulunmadı**; Double Gold ardından aynı Menu dönüşünde interstitial denenebilir |
| No Ads | Interstitial kapanır; rewarded teklifler açık kalır |
| Remote Config / A-B kontrolü | Bulunmadı |

`SceneLoader` Menu'yu async yüklemeye başlar, aktivasyonu bekletir, gerekirse interstitial yüklenmesini **en çok 10 saniye** bekler, ardından Show dener. Finished callback'i için **60 saniyelik** üst bekleme vardır; sonra sahne aktivasyonu devam eder. Bunlar cooldown değildir.

Gerçek AdsConfig [SO]: `adsEnabled=true`, `useTestAds=false`; Android/iOS için ayrı Life, Double Gold ve GameToMenu ad-unit değerleri atanmış. Load retry aralıkları **5/15/30/60 saniye**, ad cache yaşı **55 dakika**. Bunlar da oyuncu kullanım limiti değildir. Editor'da slotlar mock kullanır; Play Mode testleri gerçek cihaz reklam doğrulaması yerine geçmez.

### İnternet zorunluluğu

Game ve Menu sahnelerinde `InternetConnectionGuard` bağlı. `CurrentLevel >19 && !HasNoAds` olduğunda bağlantı gerektirir; sahne hazır olduktan sonra reachability kontrolü yapar. UIConfig'teki kontrol aralığı **1 saniye** [SO]. `NotReachable` durumunda No Connection blocking paneli açar, UI/gameplay input'u sınırlar. Panel açıkken otomatik kontrol yerine Refresh düğmesi kullanılır. No Ads sahipliği bu gereksinimi kaldırır.

Bu gate ad readiness veya entitlement-sync başarısı şartına bağlı değildir. Dolayısıyla IAP sync'i başarısız olduğu için interstitial gösterilmeyen bir oyuncu yine de internet gate'ine takılabilir. Offline life regeneration hesaplaması var olmaya devam eder; bu, oyunun her progression seviyesinde çevrimdışı oynanabildiği anlamına gelmez.

Referanslar: [SceneLoader.cs](Assets/Game/Shared/Navigation/SceneLoader.cs), [AdsConfig.asset](Assets/Game/Data/Configs/AdsConfig.asset), [InternetConnectionGuard.cs](Assets/Game/Shared/UI/Panels/InternetConnectionGuard.cs), [UIConfig.asset](Assets/Game/Data/Configs/UIConfig.asset).

## 6. IAP / gerçek para ürünleri

Toplam **9 ürün**, 8 consumable ve 1 non-consumable. Subscription **bulunmadı**. Product ID'ler platform için ayrı alias tanımlanmadan aynı katalogdan oluşturuluyor.

| Product ID | Tür | Oyuncunun aldığı içerik | Tek seferlik / erişim |
| --- | --- | --- | --- |
| `coins_1000` | Consumable | 1.000 coin | Tekrar alınabilir; Shop |
| `coins_5000` | Consumable | 5.000 coin | Tekrar alınabilir; Shop |
| `coins_10000` | Consumable | 10.000 coin | Tekrar alınabilir; Shop |
| `coins_25000` | Consumable | 25.000 coin | Tekrar alınabilir; Shop |
| `coins_50000` | Consumable | 50.000 coin | Tekrar alınabilir; Shop |
| `coins_100000` | Consumable | 100.000 coin | Tekrar alınabilir; Shop |
| `no_ads` | Non-consumable | Kalıcı No Ads; coin/booster/life vermez | Sahiplik varsa tekrar alınmaz; monetization offer gate'i |
| `starter_pack` | Consumable | 2.500 coin +1 Hand +1 Shuffle +1 UFO +60 dakika unlimited lives | **Local save başına bir kez**; No Ads içermez |
| `fail_offer` | Consumable | 1.500 coin +1 Hand +1 Shuffle +1 UFO +1 continue | Aktif recovery session gerekir; No Ads/unlimited içermez |

Bütün içerikler [Kod: `StoreCatalog`]. Fiyatlar [Mağaza]: `product.metadata.localizedPriceString` UI'a gelir. **Her ürünün gerçek para fiyatı kesin doğrulanamadı.** Shop prefabında tüm kartların price text'i `4.99$` örnek metni taşıyor; `ShopProductCardController.Refresh()` bunu gerçek store fiyatıyla veya metadata yoksa `—` ile değiştiriyor. Bu prefab metni fiyat listesi değildir.

### Initialization, fulfillment ve failure

1. Boot → save/lives/ads initialize → `StoreManager.Initialize()`.
2. `UnityIapPurchaseService` → `UnityIAPServices.StoreController()` → event bağlantıları → katalogdan `ProductDefinition` → `Connect()`.
3. Product metadata fetch → purchases fetch → confirmed entitlement/pending/deferred işlemleri.
4. UI satın alması `StoreManager.CanPurchase/Purchase` üzerinden gider. Store/product availability, aktif işlem, restore, unresolved ürün ve local ownership kontrolleri vardır.
5. Paid pending order → `StoreManager.HandlePurchasePending()` → `PurchaseProcessor.Process()`.
6. `StoreRewardProcessor.ApplyPersistentRewards()` coin, booster, unlimited ve No Ads alanlarını günceller. Transaction dedup kaydıyla **aynı save payload'ında** `TrySave()` yapılır. Başarısızsa runtime snapshot geri alınır; order confirm edilmez.
7. Fail Offer gameplay continue ayrıca captured token ile işlenir. Geçersiz session durumunda kalıcı recovery continue credit'ine dönüştürülür.
8. Başarılı persistence/fulfillment sonrası `ConfirmPurchase()`; confirmation ve fulfillment sorunları pending tutulup yeniden denenir. StoreManager retry 5 saniyeden başlayıp 60 saniyeye kadar artar.
9. UI başarı eventi coin-fly/refresh yapar; burada ikinci currency grant yoktur.

Cancellation ve failure ayrıdır. Shop'ta loading/blocker kalkar ve purchase-cancel paneli açılır. Deferred durumda pending/deferred state gösterilir; normal başarı ödülü gibi değerlendirilmez. Fail Offer UI purchase hatasında aksiyon kilidini açıp seçenekleri yeniler. Ürün metadata'sı yoksa satın alma düğmesi kullanılabilir olmaz. Boot store initialization hatasında oyunun diğer servislerinin devam etmesi amaçlanmış.

### Restore ve platform farkları

Menu Settings'te Restore Purchases yolu var. iOS/macOS/tvOS dalında `RestoreTransactions()` ardından purchases fetch; diğer dalda purchases fetch yapılır. Başlangıçta da entitlement senkronizasyonu vardır.

Restore sonucu **No Ads sahipliği** yeniden uygulanır. Tüketilmiş coin/booster/unlimited paketleri baştan tekrar dağıtılmaz. Pending consumable order'ların yeniden teslimi restore ödülü değil, normal pending fulfillment hattından işlenir. Starter Pack'in bir defalık kuralı local flag'dir; cihazlar arası kalıcı consumable entitlement olarak restore edilmez.

### Receipt handling

Order receipt, transaction ID, fiyat ve currency SDK order'ından taşınır. Projeye özgü, reward verilmeden önce zorunlu çalışan kriptografik receipt doğrulayıcı veya server-side purchase validation gate'i **bulunmadı**; fulfillment Unity IAP pending order teslimine dayanır.

`TenjinPurchaseValidation` adı bu ayrımla okunmalı: Android GooglePlay receipt JSON/signature ve `purchaseState ==0`; iOS JWS/AppReceipt bilgisini analytics için yakalar. Yakalama başarısızlığı reward'ı otomatik reddeden bir kontrol değildir. Tenjin revenue gönderimi store confirmation sonrasında journal üzerinden yürür.

Normal tekrar teslimlerinde transaction ID dedup vardır; sınırı ve continue-credit sorunları bölüm 14'te.

Referanslar: [StoreCatalog.cs](Assets/Game/Shared/Store/Core/StoreCatalog.cs), [StoreProductIds.cs](Assets/Game/Shared/Store/Core/StoreProductIds.cs), [StoreManager.cs](Assets/Game/Shared/Store/Core/StoreManager.cs), [PurchaseProcessor.cs](Assets/Game/Shared/Store/Core/PurchaseProcessor.cs), [StoreRewardProcessor.cs](Assets/Game/Shared/Store/Rewards/StoreRewardProcessor.cs), [UnityIapPurchaseService.cs](Assets/Game/Shared/Store/IAP/UnityIapPurchaseService.cs), [TenjinPurchaseValidation.cs](Assets/Game/Shared/Store/IAP/TenjinPurchaseValidation.cs).

## 7. Market / Shop ve gerçek wiring

`Assets/Game/Prefabs/UI/Shop Panel.prefab` içinde **8 bağlı ürün kartı** var: altı coin paketi, Starter Pack, No Ads. Fail Offer ayrı gameplay recovery panelindedir.

Game sahnesinde Shop, `GameShopPanelController` ile açılan paneldir. Gold `+`, booster alımında yetersiz gold ve cleanup'ta yetersiz gold buraya ulaşır. Bu gameplay Shop için sabit bir minimum level kontrolü yoktur; booster tutorial'ı sırasında gold `+` engellenir. Menu Shop sekmesi, normal açılış akışında Level 10 tamamlandıktan sonra görünür; oyuncu Settings üzerinden daha erken Menu'ye çıkabilirse de aynı sekme açılabilir.

| Offer | Fiyat / içerik | Görünme / satın alma koşulu | Limit ve persistence |
| --- | --- | --- | --- |
| Coin paketleri | Mağaza fiyatı; 1k–100k coin | Shop, hazır ürün | Daily/cooldown yok; transaction journal |
| Starter Pack | Mağaza fiyatı; 2.500 + üç booster +60 dk | Shop; local purchased değil | `starterPackPurchased`; alınınca kart gizlenir |
| No Ads | Mağaza fiyatı | CurrentLevel >19, entitlement resolved, sahip değil | `hasNoAds`; offer intro ayrıca kaydedilir |
| Hand/Shuffle paketi | 1.800 → ×3 | Gameplay, booster açık ve stok 0 tıklaması | Coin/stok; cooldown yok |
| UFO paketi | 2.600 → ×3 | Aynı | Coin/stok; cooldown yok |
| Life refill | Eksik ×120 → max | Menu LifePanel, eksik can, unlimited değil | Lives/timer/coin |
| Rewarded life | Reklam → +1 | Rewarded unlock ve readiness | Daily counter yok |
| Clean Up / Play On | 900 → cleanup | Aktif recovery | Per-level sayı sınırı bulunmadı |
| Fail Offer IAP | 1.500 +3 booster toplamı +continue | Aktif recovery ve hazır ürün | Transaction/token/credit journal |

Ayrı life IAP paketi, bağımsız booster IAP SKU'su, daily offer, ücretsiz Shop hediyesi, rewarded Shop kartı, süreli indirim sayacı veya abonelik **bulunmadı**.

No Ads otomatik tanıtımı: uygun oyuncu Menu'ye geldiğinde `noAdsIntroShown=false` ise coin uçuşunu bekleyip offer açmayı dener; gerçekten açıldığında flag kaydedilir. Level 19 win handoff'u bu sunumu ayrıca sıraya alır. Sonrasında offer butonundan manuel tekrar açılabilir; intro bir kez, satın alma sahiplik temellidir.

### Prefab default ile sahne override farkları

- Shop prefabında `coinFlyAnimator`, `panelManager`, purchase loading/blocker/cancel referansları boş. **Game ve Menu instance override'larında dolu**: bunları prefabda null görüp runtime eksikliği olarak yorumlamak yanlış olur.
- LifePanel prefabında `panelManager` boş; Menu instance'ında PanelManager, heart animator'ın `livesHud`, `inputLock`, `animationRoot` referansları bağlanmış.
- `MenuShopController.shopContent` sahnede null. Bununla birlikte içteki `ShopContentController.OnEnable()` kendi servislerini bağlayıp refresh yapıyor. Null referans burada tek başına Shop çalışmıyor kanıtı değildir; dış wrapper'ın refresh çağrısı etkisizdir.
- No Ads ve Starter Pack görünürlüğü runtime sahiplik/gate kontrolleriyle güncelleniyor. Card price binding'leri ve sekiz ürün ID'si doğrulandı.

Referanslar: [Shop Panel.prefab](<Assets/Game/Prefabs/UI/Shop Panel.prefab>), [LifePanel.prefab](Assets/Game/Prefabs/UI/LifePanel.prefab), [ShopContentController.cs](Assets/Game/Shared/Store/UI/ShopContentController.cs), [ShopProductCardController.cs](Assets/Game/Shared/Store/UI/ShopProductCardController.cs), [GameShopPanelController.cs](Assets/Game/Scripts/Runtime/UI/Shop/GameShopPanelController.cs), [MenuNoAdsOfferController.cs](Assets/Game/Scripts/Runtime/Menu/MenuNoAdsOfferController.cs).

## 8. Level progression ve timeline

Bağlı `LevelCatalog.asset`: **150 level referansı, 150 sıralı içerik**. Level numaraları 1–150. Bu sayı, her board tasarımının birbirinden geometrik olarak farklı olduğuna dair ayrı bir duplicate-board testi anlamına gelmez; oynanabilir katalog içerik sayısıdır.

İlk 150 displayed level katalogdaki aynı numaralı içeriği açar. **Level 151'den sonra displayed sayı artmaya devam eder**, içerik 135 elemanlı loop havuzundan gelir. `IncludeInLoop=false` olanlar 1–15; loop havuzu 16–150. `loopSeed=0` [SO]. Her cycle `System.Random(loopSeed + cycleIndex)` ile Fisher–Yates karıştırılır; cycle sınırında son içerik/ilk içerik tekrarına karşı swap uygulanır. Kullanıcıya özel random seed veya A/B ataması yok.

Salt okunur resolver doğrulaması: displayed 151→içerik64, 152→146, 153→20, 154→79, 155→68. Retry aynı displayed level için aynı içeriği çözer. `lastLoopContentLevelNumber` save alanı mevcut resolver'ın seçim kaynağı değildir.

### Timeline

Board feature satırları “global feature unlock save'i” değil, katalogda ilgili verinin **ilk fiilen kullanıldığı level** anlamındadır.

| Level / koşul | Oyuncunun gördüğü olay | Kaynak |
| --- | --- | --- |
| İlk açılış / Level 1 | 500 coin, 5 can, boosters 0; temel source→conveyor→target; tap tutorial | Kod + Level SO |
| Level 1'den itibaren | Can servisleri, fail recovery/coin cleanup; gameplay Shop için level gate yok | Kod + scene wiring |
| Level 5 | İlk mystery source box | Level SO |
| Level 6 | Hand unlock ve kullanım tutorial'ı | Game Inspector |
| Level 8 | Shuffle unlock ve kullanım tutorial'ı | Game Inspector |
| Level 10 | İlk spawner | Level SO |
| Level 10 tamamlanır | Save level11; rewarded teklifler açılır; ilk Double Gold fırsatı; normal win akışı Menu'ye geçmeye başlar | Kod |
| Level 10 win coin-fly tamamlanır | İlk App Review isteği; EnableRateUs ve önce istenmemiş olma koşulları | Kod + FeatureConfig SO |
| Level 11 | UFO unlock/tutorial; ilk initial conveyor marbles | Inspector + Level SO |
| Kayıtlı currentLevel ≥11 ile Boot | Game yerine Menu açılır | Kod |
| Level 17 | İlk Hard | Level SO |
| Level 18 | İlk Very Hard | Level SO |
| Level 19 tamamlanır | Save level20; Game→Menu interstitial uygunluğu, No Ads offer ve internet gate açılır | Kod |
| Level 31 | İlk mystery target | Level SO |
| Level 39 | İlk connected source boxes | Level SO |
| Level 51 | İlk arrow source | Level SO |
| Level 63 | İlk multiplier gate | Level SO |
| Level 75 | İlk Gift Box | Level SO |
| Level 85 | İlk panel board feature | Level SO |
| Level 101 | İlk connected target group | Level SO |
| Level 116 | İlk crate | Level SO |
| Level 135 | İlk key / locked target | Level SO |
| Level 150 | Son ilk-geçiş içerik; Very Hard | Level SO |
| Level 151+ | 16–150 havuzunun deterministik loop'u | SO + hesap |

App Review için eşik yalnızca “currentLevel yüksek” değildir: Level 10 win handoff'unda taşınan review isteği, Menu coin animasyonunun completion'ı, `EnableRateUs=true`, `first_app_review_requested=false` gerekir. Request success'te flag kaydedilir. Mobil işletim sistemi/store arayüzünün gerçekten gösterildiği bu incelemede kesin doğrulanamadı. Editor dalı 2 saniyelik simülasyondur.

### Difficulty'nin tam dağılımı

Difficulty, displayed level modulo hesabından veya Remote Config'ten değil **seçilen `LevelDefinition.difficulty` alanından** gelir. Loop'ta da seçilen eski içeriğin difficulty/reward'ı kullanılır.

- **Hard (27):** 17, 25, 35, 45, 49, 56, 59, 65, 68, 74, 78, 80, 84, 87, 95, 98, 104, 108, 114, 118, 125, 128, 134, 137, 139, 144, 148.
- **Very Hard (13):** 18, 30, 40, 50, 60, 70, 90, 100, 110, 120, 130, 140, 150.
- **Normal (110):** 1–150 içinde bu iki listede bulunmayan tüm seviyeler. Özellikle 80 Hard'dır; yalnız “her 10 level Very Hard” gibi bir kural çıkarılamaz.

Referanslar: [LevelCatalog.asset](Assets/Game/Data/Levels/LevelCatalog.asset), [LevelProgressController.cs](Assets/Game/Scripts/Runtime/Features/Levels/LevelProgressController.cs), [LevelDefinition.cs](Assets/Game/Scripts/Runtime/Features/Levels/LevelDefinition.cs), [InitialSceneRouting.cs](Assets/Game/Shared/Navigation/InitialSceneRouting.cs), [MenuCoinFlyHandoffPresenter.cs](Assets/Game/Scripts/Runtime/Menu/MenuCoinFlyHandoffPresenter.cs).

## 9. Win / lose — gerçek execution order

### Ortak başlangıç

Boot servisleri → save yükleme → life regeneration → initial scene routing. Menu'den başlıyorsa önce can kontrolü. Game'de `LevelSessionController.StartCurrentLevel()` → displayed level çözümü → board build → attempt flag'lerini reset → tutorial başlangıçları → level analytics. Başlangıçta coin/can bedeli yok. Booster Running olursa inventory o anda düşer.

### Win

```text
TargetLane.AllLanesCompleted
→ BeginWin: resultActive guard + sealed-source kontrolü + Playing→Won
→ difficulty'den 25/50/75 ödül hazırlanır; henüz coin verilmez
→ level complete analytics
→ MarkCurrentLevelCompleted: currentLevel = displayed+1, hemen save
→ win logic event'leri
→ target completion sunumu biter
→ winPanelDelay (Game Inspector: 0,1 sn)
→ WinAnimationTriggered
→ sonuç view winShowDelay (Inspector: 0,5 sn)
→ Win paneli
```

Level 1–9: panel → guarded base claim → gold + Save → coin fly; Continue → panel close → aynı sahnede sonraki level build.

Level 10+: panel → base Continue **veya** Double Gold. Base Continue: gold claim + Save → panel close → exit/clear → sunum handoff'u → Game→Menu geçişi. Double Gold: ShowRewarded → earned callback → 2× gold claim + Save → finished callback → panel close/exit/handoff → aynı Menu geçişi.

Menu geçişinde uygun interstitial araya girer; Menu aktive olup hazır olduğunda coin fly oynar. Level 10 milestone'unda review isteği, Level 19 milestone'unda No Ads sunumu coin fly sonrasına bağlanmıştır. Progression save'inin coin claim'den **önce** olması önemli bir kesilme penceresidir.

### Lose / recovery

```text
Conveyor full + target'ların conveyor/entry içeriğinden artık dolamayacağı durum
→ 0,1 sn aralıkla kontrol; Game Inspector failDelay 0,1 sn
→ TriggerLose: Playing→Recovering; session token; henüz life kaybı yok
→ input ve Settings etkileşimi kısıtlanır
→ varsa pending Fail Offer continue credit'i denenir
→ aksi durumda 1 sn sonra Clean Up teklifi
```

Seçenekler:

- **Clean Up:** 900 coin → en çok 30 uygun bilye recovery sahipliğine alınır → renklere ayrılmış recovered source box'lara taşınır → `TryCompleteRecovery(token)` → Playing. Can düşmez; yeni attempt açılmaz.
- **Quit:** doğrudan life düşürmez; Fail Offer panelini açar.
- **Fail Offer / Play On:** aynı 900 coin cleanup.
- **Fail Offer / premium:** store satın alma → kalıcı coin/booster ödülleri + kaydedilmiş transaction → token üzerinden coin bedelsiz cleanup. Geçerli session yoksa continue credit'i saklama yolu.
- **Fail Offer kapatma:** panel close → `FinalizePendingFail()` → Recovering→Failed → attempt için −1 life ve save → fail analytics → lose event/UI.

Can kaldıysa lose paneli Retry → aynı level rebuild; ek coin/can harcaması yok, tüketilmiş booster geri verilmez. Son can harcandıysa Menu'ye dönülür; monetization koşulları varsa interstitial denenir. Recovery sırasında tekrar fail/cleanup kullanımı için belirlenmiş sayısal limit bulunmadı.

Genel yinelenme korumaları: `resultActive`, `winRewardClaimed`, `HasSpentLifeForCurrentAttempt`, ad request version ve once flag'leri, booster lifecycle version, IAP transaction journal. Scene kayıtlı persistent UnityEvent çağrılarında ikinci AddGold/SpendGold veya reward-grant bağlantısı görülmedi. Bu korumalar bütün crash/late-callback durumlarının güvenli olduğunu kanıtlamaz; somut açıklar aşağıda.

## 10. Save / persistence envanteri

Ana storage **JSON**, `Application.persistentDataPath/save.json`. `save.tmp` geçici dosya ve `save.bak` önceki primary yedeği var. `JsonUtility` serializer, schema **version=1** [Kod]. Economy/progression için çalışan cloud-save backend'i bulunmadı.

| Veri | Save alanı / key | Yazılma | Okunma |
| --- | --- | --- | --- |
| Coin | `gold.amount` | Add/Spend/Set dirty; win/IAP/refill açık commit | Boot/Reload, UI ve tüm alımlar |
| Can | `lives.current`, `lives.max` | LivesService değişimlerinde Save | Boot, HUD, start/refill, regen |
| Timer | `lives.nextRefillUtc` | UTC saniyesi; can değişimi/regen Save | Boot, Tick, resume ve getter refresh |
| Unlimited | `store.infiniteLivesEndUtc` | Store ödül commit'i; süre ekleme | Life kontrolü, HUD, refill |
| Current level | `level.currentLevel` | `SetCurrentLevel` anında Save; win'de claim'den önce | Routing, session, UI, ads gates |
| Completed kayıtları | `level.levels[].levelNumber/completed/values` | Public API var; normal win bu listeyi yazmıyor | API mevcut; ana progression currentLevel kullanır |
| Pending next | `level.hasPendingNextLevel`, `level.pendingNextLevel` | API var; mevcut normal flow'da aktif caller bulunmadı | Şemada korunuyor |
| Son loop içerik | `level.lastLoopContentLevelNumber` | API var; aktif resolver kullanmıyor | Şemada korunuyor |
| Booster stok | `boosters[].id/amount` | Add/Use/Set dirty; sonraki commit | HUD, tutorial, store |
| Booster tutorial | `boosters[].unlocked` | Tutorial tamamlanınca Save | Tam unlock level'ında tutorial başlatma |
| Booster ek alanları | `boosters[].values[]` | Generic veri kapasitesi | Mevcut booster ekonomisi için aktif ek key bulunmadı |
| Level 1 tutorial | `settings[]: level_one_tap_tutorial_completed` | Tamamlanınca Save | Tutorial başlangıcı |
| Review | `settings[]: first_app_review_requested` | Request success → Save | İlk review uygunluğu |
| No Ads | `store.hasNoAds` | Purchase/restore durable commit | Ads, offer, internet guard |
| No Ads intro | `store.noAdsIntroShown` | Offer gerçekten açılınca Save | Menu otomatik intro |
| Starter Pack | `store.starterPackPurchased` | Ürün reward commit'i | Tek-seferlik uygunluk, kart görünümü |
| IAP dedup | `store.processedIapTransactions[]` | Reward ile aynı commit | Redelivery işleme |
| Confirm bekleyen | `store.unconfirmedIapTransactions[]` | Pending/confirm sınırları | Startup/retry/reconcile |
| Gameplay fulfillment | `store.iapGameplayFulfillments[]` | Transaction/product/token/applied | Fail Offer redelivery |
| Continue credit | `store.pendingFailOfferContinueCredits`, `reservedFailOfferContinueCredits` | Credit üretme/rezervasyon/tüketme save'leri | Recovery başlangıcı / normalization |
| IAP analytics | `pendingIapAnalyticsIntents[]`, `iapAnalyticsTransactions[]` | Satın alma niyeti, transaction, confirmation ve dispatch | Startup/retry, duplicate analytics kontrolü |
| Ses/haptic | `settings[]: sound_enabled`, `haptic_enabled` | Ayar değişimi Save | Bootstrap ve Settings |
| Rewarded günlük/level sayacı | **Bulunmadı** | — | — |
| Günlük ödül tarihi/sayacı | **Bulunmadı** | — | — |
| Board feature unlock kaydı | **Bulunmadı** | Feature'lar level içeriğinde | Level SO |

IAP analytics transaction satırları ayrıca `localizedPriceValue`, `currency`, accepted/confirmed flag'leri, Tenjin receipt/signature/quantity/dispatch bilgileri taşır; bunlar reward miktarı belirleyen remote ekonomi değildir.

Ayrı **PlayerPrefs** kayıtları: `analytics_active_level_attempt`, `analytics_last_displayed_level`, `analytics_last_attempt_number` (attempt telemetry); `ColorFlow.DeveloperTools.test_devices.v1` (developer Remote Config cache). Bunlar gold/lives/progression save'inin yerine geçmez.

### Commit / load detayları

- `OnApplicationPause(true)`, `OnApplicationQuit`, SaveManager owner destroy → dirty save flush.
- Coin ile booster satın alma, booster tüketimi ve coin cleanup kendi handler'ında ayrı `Save()` çağırmıyor. Bir sonraki win/progression/life/IAP commit'i veya lifecycle flush'ı bunları da diske taşır.
- Life refill'de SpendGold sonrası AddLives → aynı live modelin Save'i coin ve life'ı birlikte kaydeder. Normal life/refill API'leri `Save()` sonucunu UI başarısına bağlamaz.
- IAP farklı olarak `TrySave()` sonucunu kontrol edip snapshot rollback kullanır.
- Primary bozuksa ve primary mevcutsa uygun backup okunup restore edilmeye çalışılır. **Primary hiç yoksa backup recovery denenmiyor**, yeni kayıt oluşturuluyor; yalnız `.bak` kalması otomatik kurtarma anlamına gelmez.
- Desteklenmeyen schema/version veya storage erişim sorunu → yazma bloklanır; unsupported primary üzerine yazılmaz. Runtime default model kullanılabilir.
- Eski PlayerPrefs anahtarları, yeni JSON yaratılan belirli yolda silinir; eski progression'ı JSON'a taşıyan çalışan migration yolu bulunmadı. Serializer yalnız version1 kabul ediyor.
- Normalizasyon negatif coin/stok/can, yinelenen satır ve geçersiz alanları temizler. Reservation normalizasyonunun write sırasında da uygulanması aşağıdaki continue-credit sorununu oluşturur.

Referanslar: [GameSaveData.cs](Assets/Game/Shared/Save/GameSaveData.cs), [Data klasörü](Assets/Game/Shared/Save/Data), [JsonFileSaveStorage.cs](Assets/Game/Shared/Save/Storage/JsonFileSaveStorage.cs), [SaveJsonSerializer.cs](Assets/Game/Shared/Save/SaveJsonSerializer.cs), [GameSaveDataNormalizer.cs](Assets/Game/Shared/Save/GameSaveDataNormalizer.cs), [SaveKeys.cs](Assets/Game/Shared/Save/SaveKeys.cs).

## 11. Remote Config / A-B test

Firebase Remote Config entegrasyonu var, fakat okunan oyun-özel parametre **`test_devices`**. Developer panel erişim listesini yönetiyor. Boot başına fetch/activate, 15 saniyelik deadline ve başarısızlıkta 24 saatten yeni cache fallback'i bulunuyor. Session developer unlock'ı kalıcı değil.

| Economy/progression konusu | Remote ile değişen değer bulundu mu? |
| --- | --- |
| Can maksimumu / refill süresi / life fiyatı | Bulunmadı |
| Win reward / multiplier | Bulunmadı |
| Booster fiyatı / quantity / unlock | Bulunmadı |
| Difficulty / level katalog sırası / loop seed | Bulunmadı |
| Rewarded/interstitial unlock seviyesi / cooldown | Bulunmadı |
| IAP reward içeriği / SKU listesi | Bulunmadı |
| A/B cohort assignment / varyant economy | Bulunmadı |

AdsConfig ve FeatureConfig yerel ScriptableObject'lerdir; Firebase'den doldurulmuyor. Store fiyatının SDK'dan gelmesi Remote Config/A-B fiyat mekanizması değildir.

Dolaylı etki: `test_devices` yetkili developer'ın gold/life/booster/level debug araçlarını açmasına izin verebilir. Bu normal oyuncu economy config'i veya A/B balancing değildir. Yayımlanmış Firebase sunucu parametresi bu incelemede okunmadı.

Referanslar: [FirebaseTestDevicesRemoteConfig.cs](Assets/Game/Integrations/RemoteConfig/FirebaseTestDevicesRemoteConfig.cs), [DeveloperPanelBootstrap.cs](Assets/Game/Shared/DeveloperTools/Core/DeveloperPanelBootstrap.cs), [FeatureConfig.cs](Assets/Game/Shared/Config/FeatureConfig.cs).

## 12. Final economy tabloları

### Sources

| Source | Reward | Amount | Condition |
| --- | --- | ---: | --- |
| Yeni save | Coin | 500 | İlk kayıt |
| Normal win | Coin | 25 | Base claim |
| Hard win | Coin | 50 | Base claim |
| Very Hard win | Coin | 75 | Base claim |
| Double Gold | Coin | Toplam 50 /100 /150 | Level10 win'den itibaren earned callback; base yerine |
| Refill timer | Life | 1 /20 dk | Maksimumdan düşük |
| Rewarded Life | Life | 1 | CurrentLevel >10, eksik can, hazır ad |
| Unlock tutorial | İlgili booster | 1 | L6/L8/L11, tutorial bitmemiş ve stok0 |
| Coin IAP | Coin | 1k–100k | Paid order durable fulfillment |
| Starter Pack | Coin, booster, unlimited | 2.500 + her birinden1 +60 dk | Local bir kez |
| Fail Offer | Coin, booster, continue | 1.500 + her birinden1 +1 continue | Aktif recovery / durable credit fallback |

### Sinks

| Sink | Cost | Amount/Price | Condition |
| --- | --- | ---: | --- |
| Full life refill | Coin | Eksik can ×120 | Eksik can ve unlimited yok |
| Hand paketi | Coin | 1.800 /3 | Açılmış, stok0 offer |
| Shuffle paketi | Coin | 1.800 /3 | Açılmış, stok0 offer |
| UFO paketi | Coin | 2.600 /3 | Açılmış, stok0 offer |
| Clean Up / Play On | Coin | 900 | Recovery |
| Kesinleşmiş fail | Life | 1 | Unlimited değil, attempt'te daha önce düşmemiş |
| Give Up | Life | 1 | Move commit edilmiş ve unlimited değil |
| Booster Running | Inventory | 1 | Lifecycle başına bir kez |

### Rewarded Ads

| Placement | Reward | Amount | Limit |
| --- | --- | ---: | --- |
| RewardedLife | Life | +1 | Max lives ve ad readiness; daily limit yok |
| RewardedDoubleGold | Win coin | 50 /100 /150 toplam | Tek win claim; daily limit yok |

### IAP

| Product | Price/Product ID | Reward |
| --- | --- | --- |
| Coin 1k | Store fiyatı kesin değil / `coins_1000` | 1.000 coin |
| Coin 5k | Store fiyatı kesin değil / `coins_5000` | 5.000 coin |
| Coin 10k | Store fiyatı kesin değil / `coins_10000` | 10.000 coin |
| Coin 25k | Store fiyatı kesin değil / `coins_25000` | 25.000 coin |
| Coin 50k | Store fiyatı kesin değil / `coins_50000` | 50.000 coin |
| Coin 100k | Store fiyatı kesin değil / `coins_100000` | 100.000 coin |
| No Ads | Store fiyatı kesin değil / `no_ads` | Interstitial ve internet gate'inin kalkması |
| Starter Pack | Store fiyatı kesin değil / `starter_pack` | 2.500 coin + üç booster'dan1 +60 dk unlimited |
| Fail Offer | Store fiyatı kesin değil / `fail_offer` | 1.500 coin + üç booster'dan1 +continue |

### Progression

| Level/Condition | Unlock/Event |
| --- | --- |
| L1 | 500 coin /5 life /0 booster, temel tutorial |
| L6 /L8 /L11 | Hand /Shuffle /UFO |
| L10 tamamlanır | Rewarded, normal win→Menu, coin-fly sonrası ilk review isteği |
| L19 tamamlanır | Interstitial, No Ads offer, No Ads yoksa internet gate |
| İçerik L5/10/31/39/51/63/75/85/101/116/135 | Bölüm8'deki board feature ilk görünümleri |
| L151+ | 135 içerikli deterministik loop |

## 13. Mevcut değerlerin matematiksel sonuçları

Ek satın alma/cleanup olmadan normal net coin: **Normal +25, Hard +50, Very Hard +75**. Rewarded seçeneği başarılıysa toplam **+50/+100/+150**. Can azalması anında otomatik coin kesintisi olmadığı için bir fail'i doğrudan “−120 coin” saymak yanlış olur; 120, sonradan coin ile yenilenen her eksik canın bedelidir.

| Maliyet | Coin | Normal win eşdeğeri | Hard win eşdeğeri | Very Hard win eşdeğeri |
| --- | ---: | ---: | ---: | ---: |
| 1 eksik canın refill payı | 120 | 4,8 | 2,4 | 1,6 |
| 0→5 tam refill | 600 | 24 | 12 | 8 |
| Hand ×3 | 1.800 | 72 | 36 | 24 |
| Shuffle ×3 | 1.800 | 72 | 36 | 24 |
| UFO ×3 | 2.600 | 104 | 52 | 34,67 |
| Clean Up / Play On | 900 | 36 | 18 | 12 |

Paketlerden türetilmiş birim maliyet: Hand/Shuffle **600 coin =24 Normal /12 Hard /8 Very Hard win**. UFO **866,67 coin ≈34,67 Normal /17,33 Hard /11,56 Very Hard win**. Bunlar oran hesabıdır; Shop tek booster'ı bu fiyata satmıyor. 2.600 coin için yalnız Very Hard base win geliri biriktiriliyorsa tam sayı olarak 35 win gerekir. Double Gold alınan win'lerle oranlar yarıya iner.

Basit senaryolar; doğal regen, reklam, IAP ve booster harcaması yok varsayılmıştır:

1. **500 coin, 5 can → 5 kesinleşmiş fail:** 500 coin, 0 can. Full refill 600 istediği için alınamaz; 120'ye tek can seçeneği yoktur.
2. **500 +10 Normal win =750 coin → 5 fail → full refill:** 750−600=150 coin, 5 can. Fail'ler coin düşürmedi; düşüş refill seçiminde oldu.
3. **500 +40 Normal win =1.500 → 10 fail, her beş fail sonrası full refill:** 1.500−2×600=300 coin. Gerekli iki refill karşılanabilir.
4. **Başlangıçtan 16 Normal win:** 500+16×25=900 coin. Bir ücretli cleanup tüm 900'ü harcar; cleanup sayesinde Normal win gelirse bakiye25 olur.
5. **Bir Normal win'de bir cleanup:** net coin `25−900=−875`; Hard için `50−900=−850`, Very Hard için `75−900=−825`. Cleanup satın alma anında yeterli bakiye gerekir.
6. **5 fail sonrası beş başarılı Life reklamı:** coin değişmeden 0→5 can; her ödülden sonra yeni hazır reklam gerekir. Günlük limit bulunmadı; garanti ad doluluğu varsayılmaz.

Genel muhasebe:

`Bakiye = başlangıç +25N +50H +75V + ekstra-double-payları +IAP coin −120×satın-alınan-can −1800×Hand-paketi −1800×Shuffle-paketi −2600×UFO-paketi −900×ücretli-cleanup`

Burada X fail tek başına coin formülüne girmez; refill/cleanup kararlarıyla girer. Satın alınan can sayısı her Buy işleminde o andaki eksik sayıdır. Kullanıcı davranışı/fail oranı ölçümü olmadığı için “ortalama oyuncu” tahmini yapılmadı.

Katalogdaki 150 ilk-geçiş level'ın hepsi yalnız base reward ile bir kez bitirilirse toplam win geliri **110×25 +27×50 +13×75 =5.075 coin**; harcamasız başlangıç dahil **5.575 coin**.

## 14. Somut şüpheli / tutarsız durumlar

### 14.1. Pending Fail Offer continue credit'i tüketilirken geri oluşuyor

**Kanıtlanmış veri dönüşümü; gameplay zincirinden çıkan tüketilmeme sorunu.** `StoreManager.TryConsumeFailOfferRecoveryContinue()` bir krediyi pending'den reserved'a taşır, ardından `TrySave()` çağırır. Fakat `SaveManager.WriteCurrentData()` her yazmada `GameSaveDataNormalizer.Normalize()` çalıştırır; normalizer bütün reserved kredileri pending'e geri taşır.

```text
Başlangıç: pending=1, reserved=0
Reserve:   pending=0, reserved=1
TrySave → Normalize:
           pending=1, reserved=0
Gameplay continue başlatılır
CompleteReserved... → reserved0 olduğu için false; dönüş değeri kontrol edilmiyor
Son Save:  pending=1, reserved=0
```

Salt okunur MCP kontrolünde, yeni ve bağımsız bir `GameSaveData` nesnesi üzerinde `pending=0/reserved=1 → Normalize → pending=1/reserved=0` sonucu doğrulandı; gerçek save veya scene state'i kullanılmadı. Böyle bir pending kredi var olduğunda sonraki uygun recovery'lerde tekrar kullanılabilir kalıyor. Bu **coin/booster IAP ödülünün tekrar dağıtılmasıyla aynı hata değildir**; fallback continue kredi ekonomisini etkiler. Tam cihaz recovery senaryosu ayrıca test edilmelidir; hiçbir düzeltme uygulanmadı.

Kaynak: [StoreManager.cs](Assets/Game/Shared/Store/Core/StoreManager.cs) `TryConsumeFailOfferRecoveryContinue`; [SaveManager.cs](Assets/Game/Shared/Save/SaveManager.cs) `TryReserveFailOfferRecoveryContinueCredit`, `CompleteReservedFailOfferRecoveryContinueCredit`, `WriteCurrentData`; [GameSaveDataNormalizer.cs](Assets/Game/Shared/Save/GameSaveDataNormalizer.cs) `NormalizeStore`.

### 14.2. Progression kaydolup win coin'i kaybolabilir

Win mantığı currentLevel'ı artırıp save eder; claim bilgisi/ödül tutarı RAM'dedir. Özellikle Level10+ win ekranında Continue/reward-earned öncesi uygulama kapanırsa sonraki açılış yeni level'dan başlar, önceki claim edilmemiş coin için kalıcı recovery kaydı yoktur. Level1–9'da da win mantığı ile panelin otomatik claim'i arasındaki kısa pencere bulunur.

Kaynak: `LevelResultFlowController.BeginWin/TryClaimWinReward`, `LevelSessionController.MarkCurrentLevelCompleted`, `LevelCompleteCanvasView.Show`.

### 14.3. Erken progression'da sıfır canla doğrudan başlangıç

Boot routing currentLevel1–10 için Game'e gider; session start life kontrolü yapmaz. `MenuPlayController` gate'i atlanır. Ayrıca 0 canlı böyle bir attempt finalize edilirse `TrySpendLife` başarısız olur, `HasSpentLifeForCurrentAttempt=false` kalır; `DidFinalizedFailExhaustLives` bu flag'i de istediğinden Retry yoluna düşebilir. Bu, normal Menu girişindeki sıfır-can engelinden farklıdır.

Kaynak: `InitialSceneRouting.Resolve`, `LevelSessionController.StartCurrentLevel`, `LevelResultFlowController.DidFinalizedFailExhaustLives`.

### 14.4. Force-kill ve normal economy write sonuçları

Coin booster alımı, booster tüketimi ve cleanup bedeli anında diske commit edilmiyor. Sonraki save'den önce force-kill bu değişiklikleri kaybettirebilir. Normal win/life akışındaki `Save()` disk hatasını UI'a false olarak yansıtmıyor; runtime başarı gösterilip sonraki açılışta eski kayıt görülebilir. IAP'nin `TrySave`/snapshot mekanizması daha sıkı ve bu normal API davranışından ayrı.

### 14.5. Cleanup bedeli sonrası geç iptal için genel refund yok

Hiç bilye detach edilemezse 900 iade ediliyor. Fakat cleanup animasyonu başladıktan sonra session geçersizliği, recovered box'a ekleme başarısızlığı veya disable/clear → `CancelRuntimeRecovery` yolunda genel coin refund yok. Para düşmüş ama continue tamamlanmamış durum mümkündür. Aynı şekilde IAP continue, cleanup başlatılınca uygulanmış sayılıyor; animasyonun tamamlanmasını durable fulfillment işareti beklemiyor.

Kaynak: `FailRecoveryController.TryStartCleanup`, `CompleteCleanup`, `CancelRuntimeRecovery`; `PurchaseProcessor.TryCompleteGameplayFulfillment`.

### 14.6. Callback kapanıştan sonra gelirse reward kaybı penceresi

Slot'un `RewardedShowSession.Reward()` guard'ı yalnızca reward'ın daha önce çağrılmasını kontrol ediyor; finished sonrası reward çağrısını tümüyle reddetmiyor. UI ise finished callback'te aktif request'i temizliyor. Bu nedenle SDK/platform earned callback'i finished'dan sonra teslim ederse UI onu eski request sayıp ödülü vermeyebilir. Bu sıra gerçek cihazda gözlemlenmedi; koddan görülen somut order bağımlılığıdır. Normal failure-only callback'inin kendi başına ödül verdiği yol bulunmadı.

### 14.7. IAP dedup sonsuz geçmiş tutmuyor

Processed transaction journal için hedef sınır **128**; eski confirmed kayıtlar budanabiliyor. Hâlâ pending kayıtlar korunuyor. Budanmış çok eski transaction yeniden pending order olarak teslim edilirse mevcut local dedup onu tanımayabilir. Normal restore yalnız No Ads'i geri yüklediği için her restore coin çoğaltır sonucu çıkarılamaz; risk eski pending redelivery/saklanan geçmiş sınırıdır.

### 14.8. Cihaz saati life ekonomisini etkiliyor

Regen ve unlimited bitişi yerel UTC saatine bağlı. Saat ileri/geri değişikliği refill/unlimited süresini etkileyebilir. Server time veya çalışan enjekte edilmiş time-provider kontrolü bulunmadı.

### 14.9. Tanımlı fakat aktif akışta kullanılmayan alanlar

- `FeatureConfig.EnableShop` / `EnableRemoveAds` alanları var ve asset'te true; oyun kodu taramasında bu property'leri tüketen aktif gate bulunmadı. Shop/No Ads görünürlüğünün gerçek sahibi başka controller koşulları.
- Cloud/account/notification bayrakları backend bulunduğunun kanıtı değil; asset'te false.
- `level.levels[].completed`, pending-next ve last-loop alanları schema/API'de var; normal win/loop yolu bunları kullanmıyor.
- `GameLivesRefillHost` kodu var, Game sahnesinde bağlı instance bulunmadı.
- Shop kartlarında `purchaseFailedKey` tanımlı olsa da normal failure handler cancel paneli açıyor; etiketin varlığı ayrı bir hata mesajının gösterildiğini kanıtlamıyor.

### 14.10. Default / Inspector ve UI ayrımları

| Alan | Kod/prefab default | Etkin kayıtlı değer / sonuç |
| --- | --- | --- |
| Booster offer `goldCost` | 0 | Game: 1.800 /1.800 /2.600 |
| Booster quantity | 3 | Game: hepsi3; fark yok |
| Unlock | 6/8/11 | Game: aynı |
| Result winPanelDelay | 0,5 sn | Game: 0,1 sn |
| Result failDelay | 0,5 sn | Game: 0,1 sn |
| Result view loseShowDelay | 0,5 sn | Game: 0 |
| Shop / No Ads / Rate Us flags | false | FeatureConfig SO: true |
| Shop price text | Her kartta4.99$ | Runtime store metadata veya — |
| Shop/Life prefab servis bağlantıları | Bazıları null | Scene override'larında bağlı |

Starter Pack'in 60 dakika unlimited reward'ı katalogda var; prefab TMP reward metinlerinde coin ve üç ×1 booster görülüyor, ayrı 60 dakika metni bulunmadı. İkon/sprite'ın bu içeriği yeterince ifade edip etmediği görsel cihaz testi olmadan kesin doğrulanamadı.

Normal akışta aynı fiyat için iki farklı etkin economy kaynağı bulunmadı: Clean Up ve Play On aynı `cleanupGoldCost` alanını okuyor; life UI toplamı aynı purchase service hesabından geliyor. “120 life fiyatı” ile “600 refill fiyatı” iki çelişkili değer değil, birim ve toplam fiyat ayrımıdır.

### 14.11. Backup ve unsupported-save edge case'leri

Primary kayıp fakat backup mevcutsa yeni default kayıt yaratılabiliyor. Unsupported version'da yazma bloklanırken runtime default ekonomi gösterilebiliyor; IAP reward commit'i bloklansa da bazı normal UI işlemleri runtime'da gerçekleşmiş gibi ilerleyebilir. Backup'a dönüş, son başarılı payload'a göre coin/stok/progression'ı geri alabilir. Cloud recovery bulunmadığından bu yerel kayıt davranışları oyuncu sonucunu belirler.

## Doğrulama ve gerekli testler

Yapılan kontroller: kod çağrı noktası taraması; live Boot hierarchy ve Console okuması; SO/prefab okumaları; Game/Menu serialized wiring ve override incelemesi; 150 level'ın difficulty/feature/loop taraması; resolver ile 151–160 eşlemesi; bağımsız DTO üzerinde continue-credit normalizasyon kontrolü. Play Mode, satın alma, reklam gösterimi, oyuncu kaydı reseti veya otomatik gameplay testi çalıştırılmadı. İlk ve son Console okumalarında error/warning yoktu. Son kontrolde Editor yine Boot sahnesinde ve Play Mode kapalıydı. Rapordaki yerel kaynak bağlantıları dosya sistemi üzerinden doğrulandı.

Geliştiricinin ayrı test kaydı/cihazında çalıştırması gereken somut senaryolar:

1. Yeni kayıt: 500 coin/5 life/0 booster; L6/8/11 tutorial hediyesi ve tüketimi; önceden bundle stoku varsa hediye farkı.
2. Normal/Hard/Very Hard win: +25/+50/+75; Double Gold +50/+100/+150 toplam; çift tıklama ve gecikmiş callback.
3. Level9→10→11 ile Level18→19→20 geçişleri: auto-claim, Menu, review, interstitial, No Ads ve internet gate sırası.
4. Her eksik-can sayısında120–600 fiyat; 0 can/500 coin ile yetersizlik; reklam +1; heart fly iptali; refill sonrası yeniden Play gereksinimi.
5. 20/40/100 dakika offline regen; full timer0; unlimited60 dakika ve süre bitimi; cihaz saati değişikliği.
6. Recovery→Quit→Fail Offer kapatma: tam bir life; Retry'de ikinci life yok; hamlesiz/hamleli Give Up farkı.
7. CurrentLevel≤10, 0 can, Boot yeniden açılışı; 0-can attempt sonrası Retry yolu.
8. 900 coin cleanup başarı/0 detach/geç iptal; fail/retry sonrası booster iadesi olmaması.
9. Pending Fail Offer credit=1 ile iki ardışık recovery: kredinin gerçekten azalması; rezervasyon save'inin normalization etkisi.
10. Sandbox IAP: dokuz SKU availability/fiyat/ödül, cancellation, deferred, connection loss, reward-save failure, pending redelivery, store confirmation retry, No Ads restore.
11. Win progression-save ile claim arasındaki kapanış; booster alımı/tüketimi sonrası force-kill; bozuk primary/backup-only/unsupported-version kayıtları.
12. No Ads yokken Double Gold ardından interstitial; No Ads varken rewarded'ın kalması ve offline gate'in kalkması.
13. Displayed150→151→152, retry ve app restart: aynı deterministik content/difficulty/reward.

Etkilenen oyun dosyası/klasörü: **yok**. Fonksiyonel değişiklik: **yok**. Yeni rapor: `ECONOMY_AUDIT_TR.md`. Gerekli yeni Inspector ataması: **yok**. Final mobil/Unity Console doğrulaması geliştirici tarafından yapılmalıdır.

## Economy at a glance

- **Başlangıç:** 500 coin,5 can, booster stokları0. Normal/Hard/Very Hard win **25/50/75 coin**.
- **Can:** kesinleşmiş fail veya hamle sonrası Give Up **−1**; başlangıç/retry ek can tüketmez. **20 dk→1 life**, offline hesaplanır. Coin refill **eksik×120**; 0→5 için **600**. Life reklamı **+1**.
- **Booster:** Hand L6, Shuffle L8, UFO L11. Tutorial stok0 ise1 verir ve kullanımda tüketir. Coin paketleri **3 Hand=1.800**, **3 Shuffle=1.800**, **3 UFO=2.600**. Booster reklamı yok.
- **Fail kurtarma:** **900 coin** Clean Up/Play On; en çok30 bilyeyi geri toplar, can kaybetmeden aynı attempt sürer. Alternatif Fail Offer IAP: **1.500 coin + her booster'dan1 +continue**.
- **Rewarded:** sadece Life ve win2×. **İlk fırsat Level10 win**; daily limit/cooldown yok. Double Gold base'in yerine toplam **50/100/150** verir.
- **Interstitial:** **Level19 tamamlandıktan sonra uygun her Game→Menu geçişi**; level interval/cooldown/rewarded suppression yok. No Ads kapatır. Aynı eşikten sonra No Ads yoksa internet gate'i var.
- **IAP:** altı coin paketi1k–100k; No Ads; Starter Pack (**2.500 + üç booster +60 dk unlimited**, local bir kez); Fail Offer. Gerçek fiyatlar store metadata'sından, bu oturumda kesin değil.
- **Progression:**150 katalog içeriği; sonra16–150 havuzundan135'lik deterministik loop. Normal win→Menu akışı Level10 sonunda başlar; aynı noktada coin-fly sonrası review isteği; Level19 sonunda No Ads offer.
- **Kayıt:** yerel JSON + backup; çalışan cloud-save veya economy A/B/Remote Config yok. Firebase Remote Config developer cihaz erişimi için kullanılıyor.
- **Öne çıkan riskler:** pending continue credit'i save normalizasyonunda geri oluşuyor; progression claim'den önce kaydoluyor; erken Boot0-can gate'ini atlayabiliyor; bazı normal economy işlemleri sonraki save'i bekliyor.

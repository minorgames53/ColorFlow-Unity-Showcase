# Localization entegrasyonu — 16 Eylül 2026

## Sonuç ve sayılar

Mevcut Unity Localization 1.5.11 / Addressables mimarisi kullanıldı. `General` tek String Table koleksiyonudur; bütün tablolar aynı Shared Table Data asset'ini kullanır. Kaynak dil İngilizcedir. Runtime çeviri üretimi yoktur; gerçek String Table asset'leri build'e dahildir.

| Locale | Durum | Key |
|---|---|---:|
| de | Yeni | 78 |
| fr | Yeni | 78 |
| pt-BR | Mevcut 71 korundu, 7 eklendi | 78 |
| es | Yeni | 78 |
| it | Yeni | 78 |
| pt-PT | Yeni; Brezilya tablosundan bağımsız çeviri | 78 |
| ar | Yeni | 78 |
| pl | Yeni | 78 |
| id | Mevcut 71 korundu, 7 eklendi | 78 |
| en | Kaynak; 7 yeni key | 78 |
| tr | Mevcut 71 korundu, 7 eklendi | 78 |

- 78 farklı key; istenen dokuz dilde toplam 702 dolu karşılık.
- Bu görevde dokuz hedef dile 560 yeni değer yazıldı. Önceden mevcut pt-BR/id tablolarındaki 142 değer korundu.
- İngilizce/Türkçe yeni girişlerle birlikte toplam 574 yeni tablo değeri oluşturuldu.
- Önceden var olan 284 entry'nin key, ID, çeviri ve Smart String durumu baseline karşılaştırmasında değişmedi.
- Localization dışından taşınan iki sabit metin: destek e-postası konusu ve `Unknown` değeri.
- Kodun zaten çağırdığı fakat tabloda bulunmayan beş key tamamlandı: `store_purchase_failed`, `store_purchase_deferred`, `store_restore_succeeded`, `store_restore_nothing`, `store_restore_failed`.
- `Color Flow`, teknik UID, fiyat/para birimi API çıktıları ve saf sayılar çevrilmedi. `Level`, `Shop`, `UFO`, `OK` gibi bazı dillerde doğal kullanılan kelimeler İngilizce fallback değildir.

## İncelenen metin yolları

Build'deki Boot, Menu ve Game sahneleri, Assets/Game prefabları, runtime UI/Menu/Tutorial, Lives, Store, Settings, Support, board/world text kodları tarandı. Sabit metinler LocalizeStringEvent; level, reward, booster, tutorial, lives ve difficulty metinleri mevcut LocalizedString presenter'ları üzerinden gelir. Fiyatlar StoreManager'ın mağaza tarafından yerelleştirilmiş fiyatlarından gelir. İlk seviye tutorial'ı el animasyonu kullanır; booster tutorial'ı mevcut booster name/description key'lerini paylaşır.

Uygulama değerlendirmesi platform review arayüzüdür. Privacy bağlantısı harici web sayfasına gider; platform/harici sayfa metinleri uygulama String Table'larına taşınmadı. SDK örnek sahneleri build listesinde değildir. Developer panel, debug ve Editor metinleri kapsam dışındadır. Oyunda kullanılan Legacy UI Text bulunmadı; kullanıcı etiketleri TMP'dir. Daily Reward/Notifications için çevrilecek çalışan UI tespit edilmedi.

## Arapça, font ve layout

- `NotoSansArabic-Bold.ttf` ve dinamik TMP SDF fontu eklendi; yedi Nunito fontunun fallback listesine serialized referansla bağlandı. Kaynak ve SIL OFL lisansı `Assets/Game/Font/NotoSansArabic-OFL.txt` içindedir. Font kaynağı: https://github.com/notofonts/noto-fonts/tree/main/hinted/ttf/NotoSansArabic
- `LocalizedArabicText` mevcut TMP/presenter hattına bağlıdır. Mantıksal Arapça tablo değerleri korunur; sunum sırasında bağlama göre harf biçimleri ve lam-alef ligatürü hazırlanır. TMP RTL layout ve satır sarmayı yapar. Latin ürün adları ve sayı/fiyat run'ları doğru yönde korunur. Sola hizalı etiketler Arapçada sağa geçer; ortalı etiketler ortada kalır. Disable/dil değişiminde normal sunum geri gelir.
- Bu yardımcı mevcut oyun metinleri için minimum Arapça sunum çözümüdür; genel amaçlı kullanıcı sohbeti/bütün Unicode bidi dilleri için bir framework değildir.
- `TMPArcText` RTL satırlara harf başına dönüş uygulamaz; Arapça birleşimler düz satırda korunur.
- Dar Navigation, No Ads, Settings, Shop tagline ve world-message alanlarında Auto Size alt sınırları ayarlandı. Tasarım hiyerarşisi korunmuştur.
- Menu/Game'de önceden boş olan temporary message panel/text/CanvasGroup/RectTransform alanları yeni `Localized Message.prefab` ile bağlandı. Mevcut PanelManager mesaj akışı kullanılır; runtime isim/path araması eklenmedi.

## Dil seçimi ve kayıt

Projede manuel Language Selection ekranı veya locale save/restore uygulaması yoktu; yeni bir ekran ya da save sistemi oluşturulmadı. Mevcut SaveManager/PlayerPrefs verilerine dokunulmadı.

Command-line seçimi öncelikli kalır. `PortugueseLocaleSelector` yalnız Portekizceyi, Unity SystemLocaleSelector'dan önce eşler: `pt-BR`/`pt_BR`/`pt-Latn-BR` → `pt-BR`; `pt`, `pt-PT`, `pt-AO`, `pt-MZ` → `pt-PT`. Android cihaz language tag'i, iOS mevcut localization native preferred-language fonksiyonu ve Editor/system culture kullanılır. Diğer diller Unity'nin mevcut parent-culture fallback kuralını kullanır.

## Çalıştırılan kontroller

- `ValidateTables`: 11 × 78 entry; key parity, boş değer, TODO/TBD/TRANSLATE, placeholder çokluğu/isimleri, rich text tag'leri, satır sonları, Smart String durumu, duplicate locale ve baseline kontrolü geçti.
- Gerçek StringTableEntry/format yolu üzerinden 66 Smart String veya formatlı değer biçimlendirildi; çözümlenmemiş argüman kalmadı.
- `ValidateReferencesAndGlyphs`: 7 fontta 225 karakter; 221 TMP etiketi, 3262 component ve 128 localization reference kontrolü geçti. Yeni locale/table/shared-data Addressables kayıtları ve locale label'ları doğrulandı.
- `MeasureLayouts`: yüklü üç sahnede 89 statik/runtime etiket için 1188 metin/locale kombinasyonu, gerçek rect/font/style/wrapping ve minimum font boyutuyla ölçüldü; son çalıştırmada 0 taşma riski bulundu. Bu test statik ölçümdür; bütün cihaz aspect ratio'larının görsel testi değildir.
- `ValidateArabicPresentation`: geçici Editor canvas'ında gerçek TMP mesh üretimiyle harf birleşimi, lam-alef, 123 rakamlarının x sırası, üç satır, otomatik sarma, Color Flow / 4.99 USD, rich text ve sprite tag'leri, SetText, Arapça→Latin geçişi ve disable/restoration kontrol edildi. Geçti.
- Unity LocalesProvider ile de-DE→de, fr-CA→fr, es-MX→es, it-CH→it, pl-PL→pl, id-ID→id, ar-SA/ar-EG→ar doğrulandı. Portekizce bölge eşlemeleri ayrıca test edildi.
- Unity script derlemesinden ve son kontrollerden sonra Console'da hata yoktu. Sahneler MCP/Unity Editor API ile kaydedildi. Play Mode ve player build çalıştırılmadı.

## Önceden var olan, localization dışı bulgular

Localization/font/presenter bağlantılarında eksik reference yoktur. Genel proje taramasında Settings Panel'in eski soundOnSprite/soundOffSprite/hapticOnSprite/hapticOffSprite referansları, Game/Board/Grid/Board Tile sprite'ı ve Spawner/Root/Shadow sprite'ı eksik bulundu. Settings referansları HEAD ile aynıdır; bu görev bunları değiştirmedi. Bunlar tekrarlanan scene/prefab kullanımlarıyla 14 eksik reference kaydıdır. Kapsam dışı sprite onarımı yapılmadı.

## Etkilenen dosyalar

- `Assets/Game/Localization/Locales`: 7 yeni locale asset'i.
- `Assets/Game/Localization/Tables`: 7 yeni General tablosu; en/tr/pt-BR/id, collection ve Shared Data güncellendi.
- `Assets/Game/Localization/Localization Settings.asset`: Portekizce başlangıç selector bağlantısı.
- `Assets/AddressableAssetsData`: locale/table grup ve entry kayıtları.
- `Assets/Game/Font`: Noto font/SDF/lisans ve 7 Nunito fallback/glyph güncellemesi.
- Sahneler: `Boot.unity`, `Menu.unity`, `Game.unity`.
- UI prefabları: `CleanUpOffer`, `Fail Offer Panel`, `LifePanel`, `No Connection Panel`, `Purchase Cancel`, `Settings Panel`, `Shop Panel`; yeni `Localized Message`.
- Grid prefabları: `Features/Gift Box`, `Features/Panel`, `Features/Spawner` (TMP sunum bileşeni bağlantıları).
- Yeni runtime scriptleri: `Shared/UI/LocalizedArabicText.cs`, `Shared/UI/PortugueseLocaleSelector.cs`.
- Güncellenen runtime scriptleri: `Shared/Support/SupportMailService.cs`, `Shared/Helper/TMPArcText.cs`.
- Editor yardımcıları: `Localization/Editor/LocalizationIntegration.cs`, `Translations.tsv`, `IntegrationBaseline.tsv`.
- Belgeler: `Docs/ARCHITECTURE.md`, bu rapor.

## Inspector ve sonraki testler

Manuel Inspector/font/table/locale/prefab/scene ataması gerekmiyor. Bütün gerekli bağlantılar kaydedildi.

Geliştirici son görsel/cihaz kontrolünde her locale ile Menu, Shop, Settings, Win/Fail, booster tutorial, No Connection ve restore/deferred mesajlarını; özellikle Arapça uzun satırları ve level/fiyatları kontrol edebilir. pt-BR ve pt-PT cihaz başlangıcını ayrı deneyebilir. Bunlar ek kurulum işi değildir; Play Mode testi bu görevde istenmediği için çalıştırılmadı.

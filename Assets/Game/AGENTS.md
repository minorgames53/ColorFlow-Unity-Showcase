# Color Flow — çalışma haritası

Unity renk eşleştirme oyunu: SourceBox bilyeleri conveyor üzerinden TargetBox kuyruklarını doldurur; level verileri ScriptableObject tabanlıdır.

## Çalışma kuralları

- Depo kökündeki [AGENTS.md](../../AGENTS.md) kuralları geçerlidir.
- Değişiklikten önce aşağıdaki haritadan mevcut sorumluyu bul; ilgili kodu ve kapsamındaki alt `AGENTS.md` dosyasını oku.
- Mevcut controller/API ve event akışını genişlet. Yeni manager/service veya ikinci input, progression, reward, panel pipeline'ı eklemeden önce mevcut sahibin neden yetmediğini somutlaştır.
- Task dışındaki dosyaları ve kullanıcının mevcut değişikliklerini koru. Namespace ile klasör adının birebir eşleştiğini varsayma.
- Runtime oyun kodu `Scripts/Runtime`, ortak altyapı `Shared`, platform bağlantıları `Integrations` altındadır. Editor kodunu ilgili `Editor` klasöründe tut; komşu dosyanın namespace ve adlandırmasını izle.
- Ayarlanabilir animasyon/gameplay değerlerinde mevcut `[SerializeField]`, `Min`/`Range`, `OnValidate` yaklaşımını sürdür. Var olan config sahibini kullan; her `Config` sınıfını ScriptableObject sanma.
- DOTween'de mevcut tween sahibini ve cleanup yolunu koru; scaled/unscaled zaman seçimini kopyalamadan önce [animasyon sözleşmesini](Docs/ARCHITECTURE.md#animasyon-ve-zaman) oku.
- Script değişikliklerinde gereken Inspector atamalarını belirt. Canlı atamalar koddan doğrulanamaz; gerektiğinde Unity MCP ile incele.
- Mimari veya lifecycle değişen tasklarda ilgili belgeyi de güncelle. Kod kaynak gerçektir; belgeler Inspector değerlerinin veya tamamlanmış testlerin kanıtı değildir.

## Task yönlendirmesi

| Konu | Önce oku |
|---|---|
| Yeni özellik, klasör seçimi, UI/Menu, config, audio | [ARCHITECTURE.md](Docs/ARCHITECTURE.md) |
| Board, Marble, conveyor, target, board feature | [Features/AGENTS.md](Scripts/Runtime/Features/AGENTS.md) |
| Level, win/fail, recovery, retry, progression | [GAME_FLOW.md](Docs/GAME_FLOW.md) ve Features kuralları |
| Booster | [Boosters/AGENTS.md](Scripts/Runtime/Features/Boosters/AGENTS.md) |
| Save, store, ads, ortak UI ve servisler | [Shared/AGENTS.md](Shared/AGENTS.md) |
| Analytics event veya provider | [ANALYTICS.md](Docs/ANALYTICS.md) |

SDK kurulumu ve özel entegrasyon kuralları için [Tenjin](Integrations/Tenjin/README.md), [Remote Config](Integrations/RemoteConfig/README.md), [Lunar](Integrations/LunarConsole/README.md) belgelerini kullan; içeriklerini burada çoğaltma.

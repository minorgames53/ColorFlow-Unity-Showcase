using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Game.Shared.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.TextCore.LowLevel;

namespace Game.Localization.Editor
{
    /// <summary>Explicit, repeatable Editor integration. Never runs on asset import or in builds.</summary>
    public static class LocalizationIntegration
    {
        private const string Root = "Assets/Game/Localization/";
        private static readonly string[] Codes = { "de", "fr", "pt-BR", "es", "it", "pt-PT", "ar", "pl", "id" };
        private static readonly string[] Names = { "Deutsch", "Français", "Português (Brasil)", "Español", "Italiano", "Português (Portugal)", "العربية", "Polski", "Bahasa Indonesia" };

        public static string ApplyTablesAndFonts()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("General");
            for (int i = 0; i < Codes.Length; i++)
            {
                string code = Codes[i];
                var locale = LocalizationEditorSettings.GetLocales().FirstOrDefault(l => l.Identifier.Code == code);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(code);
                    locale.LocaleName = Names[i];
                    AssetDatabase.CreateAsset(locale, Root + "Locales/" + code + ".asset");
                    LocalizationEditorSettings.AddLocale(locale);
                }
                if (collection.GetTable(code) == null) collection.AddNewTable(code);
            }
            foreach (string row in File.ReadAllLines(Root + "Editor/Translations.tsv"))
            {
                var cells = row.Split('\t');
                if (cells.Length != 3) throw new InvalidDataException(row);
                var table = (StringTable)collection.GetTable(cells[0]);
                table.AddEntry(cells[1], cells[2].Replace("\\n", "\n"));
                EditorUtility.SetDirty(table);
            }
            var source = (StringTable)collection.GetTable("en");
            foreach (var table in collection.StringTables)
                foreach (var entry in source.Values)
                {
                    var target = table.GetEntry(entry.KeyId);
                    if (target == null) throw new InvalidDataException(table.LocaleIdentifier.Code + ": " + entry.Key);
                    if (target.IsSmart != entry.IsSmart) target.IsSmart = entry.IsSmart;
                }
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(collection);

            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(Root + "Localization Settings.asset");
            var selectors = settings.GetStartupLocaleSelectors();
            if (!selectors.Any(s => s is PortugueseLocaleSelector))
            {
                int systemIndex = selectors.FindIndex(s => s is SystemLocaleSelector);
                selectors.Insert(systemIndex < 0 ? 0 : systemIndex, new PortugueseLocaleSelector());
                EditorUtility.SetDirty(settings);
            }
            const string fontPath = "Assets/Game/Font/NotoSansArabic-Bold SDF.asset";
            var arabic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (arabic == null)
            {
                var sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Font/NotoSansArabic-Bold.ttf");
                arabic = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                arabic.name = "NotoSansArabic-Bold SDF";
                AssetDatabase.CreateAsset(arabic, fontPath);
                arabic.material.name = arabic.name + " Material";
                AssetDatabase.AddObjectToAsset(arabic.material, arabic);
                foreach (var texture in arabic.atlasTextures)
                {
                    texture.name = arabic.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(texture, arabic);
                }
            }
            var all = string.Join("", collection.StringTables.SelectMany(t => t.Values).Select(e => e.LocalizedValue));
            var arabicStrings = string.Join("", ((StringTable)collection.GetTable("ar")).Values.Select(e => e.LocalizedValue));
            string arabicCharacters = arabicStrings + LocalizedArabicText.Prepare(arabicStrings);
            AddAndVerifyCharacters(arabic, arabicCharacters.Where(c => c >= 0x600 && !char.IsControl(c)));
            EditorUtility.SetDirty(arabic);
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Game/Font" }))
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font == arabic) continue;
                AddAndVerifyCharacters(font, all.Where(c => c < 0x600 && !char.IsControl(c)));
                if (!font.fallbackFontAssetTable.Contains(arabic)) font.fallbackFontAssetTable.Add(arabic);
                EditorUtility.SetDirty(font);
            }
            AssetDatabase.SaveAssets();
            return ValidateTables();
        }

        public static string WirePrefabs()
        {
            var result = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Developer")) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset.GetComponentsInChildren<TMP_Text>(true).Length == 0) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int changes = WireRoot(root);
                    if (changes > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); result.AppendLine(path + ": " + changes); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return result.ToString();
        }

        public static string WireScene(string path)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (scene.isDirty) throw new InvalidOperationException("Scene already dirty: " + path);
            int changes = 0;
            foreach (var root in scene.GetRootGameObjects()) changes += WireRoot(root);
            if (changes > 0) EditorSceneManager.MarkSceneDirty(scene);
            // Caller checks Console before saving the finished scene via Unity MCP.
            return path + ": " + changes;
        }

        private static int WireRoot(GameObject root)
        {
            int count = 0;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(text.transform, null);
                if (path.Contains("Developer") || path.Contains("DevLevelTest") || text.name == "UIDText") continue;
                // Include empty popup labels: their runtime values may contain Arabic.
                bool changed = false;
                var arabic = text.GetComponent<LocalizedArabicText>();
                if (arabic == null)
                {
                    arabic = text.gameObject.AddComponent<LocalizedArabicText>();
                    var serialized = new SerializedObject(arabic);
                    serialized.FindProperty("label").objectReferenceValue = text;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
                bool words = Regex.IsMatch(text.text ?? "", @"[A-Za-z\u0080-\uFFFF]") || text.GetComponent<LocalizeStringEvent>() != null;
                if (words && !text.enableAutoSizing)
                {
                    text.fontSizeMax = text.fontSize;
                    text.fontSizeMin = Mathf.Min(text.fontSize, Mathf.Max(16, text.fontSize * .6f));
                    text.enableAutoSizing = true;
                    changed = true;
                }
                if (!changed) continue;
                EditorUtility.SetDirty(text);
                if (PrefabUtility.IsPartOfPrefabInstance(text)) PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                count++;
            }
            return count;
        }

        public static string ValidateTables()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("General");
            var source = (StringTable)collection.GetTable("en");
            var failures = new List<string>();
            var locales = LocalizationEditorSettings.GetLocales();
            if (locales.GroupBy(l => l.Identifier.Code).Any(g => g.Count() != 1)) failures.Add("Duplicate locale");
            foreach (string code in Codes)
                if (!locales.Any(l => l.Identifier.Code == code)) failures.Add("Missing locale: " + code);
            foreach (var table in collection.StringTables)
            {
                if (table.Count != source.Count) failures.Add("Key count " + table.LocaleIdentifier);
                if (table.SharedData != source.SharedData) failures.Add("Shared data " + table.LocaleIdentifier);
                foreach (var entry in source.Values)
                {
                    var value = table.GetEntry(entry.KeyId);
                    string name = table.LocaleIdentifier + "/" + entry.Key;
                    if (value == null || string.IsNullOrWhiteSpace(value.LocalizedValue)) { failures.Add("Empty " + name); continue; }
                    if (Regex.IsMatch(value.LocalizedValue, @"\b(TODO|TBD|TRANSLATE)\b")) failures.Add("Placeholder " + name);
                    if (Tokens(entry.LocalizedValue, @"\{[^{}]+\}") != Tokens(value.LocalizedValue, @"\{[^{}]+\}")) failures.Add("Arguments " + name);
                    if (Tokens(entry.LocalizedValue, @"<[^>]+>") != Tokens(value.LocalizedValue, @"<[^>]+>")) failures.Add("Tags " + name);
                    if (value.IsSmart != entry.IsSmart) failures.Add("Smart " + name);
                    if (entry.LocalizedValue.Count(c => c == '\n') != value.LocalizedValue.Count(c => c == '\n')) failures.Add("Newlines " + name);
                }
            }
            foreach (string row in File.ReadAllLines(Root + "Editor/IntegrationBaseline.tsv"))
            {
                var cells = row.Split('\t');
                var entry = ((StringTable)collection.GetTable(cells[0])).GetEntry(long.Parse(cells[2]));
                string original = Encoding.UTF8.GetString(Convert.FromBase64String(cells[3]));
                if (entry == null || entry.Key != cells[1] || entry.LocalizedValue != original || entry.IsSmart != bool.Parse(cells[4]))
                    failures.Add("Existing translation changed " + cells[0] + "/" + cells[1]);
            }
            if (failures.Count != 0) throw new InvalidDataException(string.Join("\n", failures));
            return "PASS: " + string.Join(", ", collection.StringTables.Select(t => t.LocaleIdentifier.Code + "=" + t.Count)) + "; 284 original entries unchanged; placeholders, tags, smart metadata and newlines match.";
        }

        private static string Tokens(string value, string pattern) => string.Join("|", Regex.Matches(value, pattern).Cast<Match>().Select(m => m.Value).OrderBy(v => v));

        private static void AddAndVerifyCharacters(TMP_FontAsset font, IEnumerable<char> characters)
        {
            string requested = new string(characters.Distinct().ToArray());
            string missing = new string(requested.Where(c => !font.HasCharacter(c)).ToArray());
            if (missing.Length > 0) font.TryAddCharacters(missing, out _);
            missing = new string(requested.Where(c => !font.HasCharacter(c)).ToArray());
            if (missing.Length > 0) throw new InvalidDataException(font.name + ": " + missing);
        }

        public static string ValidateReferencesAndGlyphs()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("General");
            var failures = new List<string>();
            var unrelated = new List<string>();
            var addressables = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var assets = collection.StringTables.Cast<UnityEngine.Object>().Concat(LocalizationEditorSettings.GetLocales())
                .Concat(new UnityEngine.Object[] { collection.SharedData });
            foreach (var asset in assets)
                if (addressables.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))) == null)
                    failures.Add("Not Addressable: " + asset.name);

            var characters = new HashSet<char>();
            foreach (var table in collection.StringTables)
                foreach (var entry in table.Values)
                {
                    string text = Regex.Replace(entry.LocalizedValue, @"\{[^{}]+\}", "123");
                    text = Regex.Replace(text, @"<[^>]+>", "");
                    if (table.LocaleIdentifier.Code == "ar") text = LocalizedArabicText.Prepare(text);
                    foreach (char c in text) if (!char.IsControl(c)) characters.Add(c);
                }
            // Cover both case variants even when a label uses TMP's uppercase style.
            foreach (char c in "äöüßÄÖÜẞñáéíóú¿¡ąćęłńśźżĄĆĘŁŃŚŹŻàâæçèêëîïôœùûüÿÀÂÆÇÈÊËÎÏÔŒÙÛÜŸãõÃÕ") characters.Add(c);
            int fontCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Game/Font" }))
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font.name.StartsWith("Noto")) continue;
                foreach (char c in characters)
                    if (!font.HasCharacter(c, true, true)) failures.Add("Glyph " + font.name + " U+" + ((int)c).ToString("X4"));
                fontCount++;
                EditorUtility.SetDirty(font);
            }
            int components = 0, labels = 0, localizedReferences = 0;
            Action<GameObject> inspect = root =>
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) { failures.Add("Missing component " + root.name); continue; }
                    string path = AnimationUtility.CalculateTransformPath(component.transform, null);
                    if (path.Contains("Developer") || path.Contains("DevLevelTest")) continue;
                    components++;
                    var serialized = new SerializedObject(component);
                    var p = serialized.GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null && p.objectReferenceInstanceIDValue != 0)
                        {
                            string missing = "Missing reference " + path + "/" + p.propertyPath;
                            if (component is LocalizeStringEvent || component is LocalizedArabicText || component is TMP_Text) failures.Add(missing);
                            else unrelated.Add(missing);
                        }
                        if (p.type != "LocalizedString") continue;
                        var id = p.FindPropertyRelative("m_TableEntryReference.m_KeyId");
                        var key = p.FindPropertyRelative("m_TableEntryReference.m_Key");
                        if (id == null || key == null || (id.longValue == 0 && string.IsNullOrEmpty(key.stringValue))) continue;
                        bool exists = id.longValue != 0 ? collection.SharedData.GetEntry(id.longValue) != null : collection.SharedData.GetEntry(key.stringValue) != null;
                        if (!exists) failures.Add("Unknown localized key " + path + "/" + p.propertyPath);
                        localizedReferences++;
                    }
                    if (!(component is TMP_Text label) || label.name == "UIDText") continue;
                    labels++;
                    var bridge = label.GetComponent<LocalizedArabicText>();
                    if (bridge == null) failures.Add("No Arabic presenter " + path);
                    else if (new SerializedObject(bridge).FindProperty("label").objectReferenceValue != label) failures.Add("Arabic label reference " + path);
                }
            };
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).GetRootGameObjects()) inspect(root);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("Developer")) inspect(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            }
            AssetDatabase.SaveAssets();
            if (failures.Count != 0) throw new InvalidDataException(string.Join("\n", failures.Distinct()));
            return "PASS: " + fontCount + " fonts, " + characters.Count + " characters, " + labels + " labels, " + components + " components, " + localizedReferences + " localized references; tables/locales/shared data Addressable.\nUnrelated existing references:\n" + string.Join("\n", unrelated.Distinct());
        }

        public static string WireTemporaryMessages()
        {
            const string path = "Assets/Game/Prefabs/UI/Localized Message.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var root = new GameObject("Localized Message", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
                var preview = EditorSceneManager.NewPreviewScene();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                try
                {
                    var rect = (RectTransform)root.transform;
                    rect.sizeDelta = new Vector2(850, 220);
                    var style = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Purchase Cancel.prefab")
                        .GetComponentsInChildren<UnityEngine.UI.Image>(true).First(i => i.sprite != null);
                    var image = root.GetComponent<UnityEngine.UI.Image>();
                    image.sprite = style.sprite;
                    image.type = style.type;
                    image.color = style.color;
                    image.raycastTarget = false;
                    var child = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
                    child.transform.SetParent(root.transform, false);
                    var text = child.GetComponent<TextMeshProUGUI>();
                    text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Font/Nunito-Black SDF.asset");
                    text.fontSize = 40;
                    text.fontSizeMin = 24;
                    text.fontSizeMax = 40;
                    text.enableAutoSizing = true;
                    text.alignment = TextAlignmentOptions.Center;
                    text.color = Color.white;
                    text.raycastTarget = false;
                    text.text = string.Empty;
                    var tr = text.rectTransform;
                    tr.anchorMin = Vector2.zero;
                    tr.anchorMax = Vector2.one;
                    tr.offsetMin = new Vector2(36, 24);
                    tr.offsetMax = new Vector2(-36, -24);
                    WireRoot(root);
                    root.SetActive(false);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            int count = 0;
            foreach (var manager in UnityEngine.Object.FindObjectsByType<Game.Shared.UI.Panels.PanelManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(manager);
                if (so.FindProperty("temporaryMessagePanel").objectReferenceValue != null) continue;
                var blocker = (GameObject)so.FindProperty("backgroundBlocker").objectReferenceValue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, blocker.transform.parent);
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = new Vector2(.05f, .5f);
                rect.anchorMax = new Vector2(.95f, .5f);
                rect.sizeDelta = new Vector2(0, 220);
                rect.anchoredPosition = Vector2.zero;
                so.FindProperty("temporaryMessagePanel").objectReferenceValue = instance;
                so.FindProperty("temporaryMessageText").objectReferenceValue = instance.GetComponentInChildren<TMP_Text>(true);
                so.FindProperty("temporaryMessageCanvasGroup").objectReferenceValue = instance.GetComponent<CanvasGroup>();
                so.FindProperty("temporaryMessageRect").objectReferenceValue = rect;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                count++;
            }
            return "Wired temporary messages: " + count;
        }

        public static string MeasureLayouts()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("General");
            var targets = new Dictionary<TMP_Text, HashSet<string>>();
            Action<TMP_Text, string> add = (text, key) =>
            {
                if (text == null) return;
                if (!targets.TryGetValue(text, out var keys)) targets[text] = keys = new HashSet<string>();
                keys.Add(key);
            };
            foreach (var ev in UnityEngine.Object.FindObjectsByType<LocalizeStringEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var entry = ev.StringReference.TableEntryReference;
                var data = entry.KeyId != 0 ? collection.SharedData.GetEntry(entry.KeyId) : collection.SharedData.GetEntry(entry.Key);
                if (data != null) add(ev.GetComponent<TMP_Text>(), data.Key);
            }
            // Runtime presenters use the same tables but write their serialized TMP references directly.
            foreach (var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (component == null) continue;
                var so = new SerializedObject(component);
                Action<string, string> field = (name, key) => add(so.FindProperty(name)?.objectReferenceValue as TMP_Text, key);
                switch (component.GetType().Name)
                {
                    case "MenuLevelNodeView": field("difficultyText", "level.difficulty.very_hard"); field("difficultyText", "level.difficulty.hard"); break;
                    case "MenuTopHudController": field("livesStatusText", "life.full"); break;
                    case "GameplayHudController": field("levelText", "hud.level"); break;
                    case "SourceBoxBoardFullMessageView": field("messageText", "game_board_is_full"); break;
                    case "LevelCompleteCanvasView":
                        field("winRibbonText", "win.level_complete"); field("loseRibbonText", "lose.level_title");
                        field("loseContinueButtonText", "lose.try_again"); field("loseContinueButtonText", "lose.continue_to_menu"); break;
                    case "AddBoosterPanelController":
                    case "BoosterUnlockTutorialController":
                        foreach (string booster in new[] { "hand", "shuffle", "ufo" })
                        { field("boosterNameText", "booster." + booster + ".name"); field("descriptionText", "booster." + booster + ".description"); }
                        break;
                    case "BoosterHudController":
                        foreach (string booster in new[] { "handPresentation", "shufflePresentation", "ufoPresentation" })
                            field(booster + ".unlockLevelText", "booster.unlock_level_prefix");
                        break;
                    case "PanelManager":
                        foreach (string key in new[] { "store_purchase_deferred", "store_restore_succeeded", "store_restore_nothing", "store_restore_failed" }) field("temporaryMessageText", key);
                        break;
                }
            }
            var preview = EditorSceneManager.NewPreviewScene();
            var testObject = new GameObject("Localization measurement", typeof(RectTransform), typeof(TextMeshProUGUI));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(testObject, preview);
            var probe = testObject.GetComponent<TextMeshProUGUI>();
            var issues = new List<string>();
            int cases = 0;
            try
            {
                foreach (var target in targets)
                {
                    var text = target.Key;
                    Vector2 size = text.rectTransform.rect.size;
                    probe.font = text.font;
                    probe.fontSharedMaterial = text.fontSharedMaterial;
                    probe.fontStyle = text.fontStyle;
                    probe.isOrthographic = text.isOrthographic;
                    probe.fontSize = text.enableAutoSizing ? text.fontSizeMin : text.fontSize;
                    probe.enableAutoSizing = false;
                    probe.textWrappingMode = text.textWrappingMode;
                    probe.characterSpacing = text.characterSpacing;
                    probe.wordSpacing = text.wordSpacing;
                    probe.lineSpacing = text.lineSpacing;
                    probe.margin = text.margin;
                    foreach (var key in target.Value)
                        foreach (var table in collection.StringTables)
                        {
                            string value = Regex.Replace(table.GetEntry(key).LocalizedValue, @"\{[^{}]+\}", "1234");
                            if (key == "booster.unlock_level_prefix") value += " 11";
                            probe.isRightToLeftText = table.LocaleIdentifier.Code == "ar" && LocalizedArabicText.ContainsArabic(value);
                            if (probe.isRightToLeftText) value = LocalizedArabicText.Prepare(value);
                            Vector2 preferred = probe.GetPreferredValues(value, size.x, Mathf.Infinity);
                            cases++;
                            if (preferred.y > size.y + 1 || preferred.x > size.x + 1)
                                issues.Add(text.gameObject.scene.name + "|" + AnimationUtility.CalculateTransformPath(text.transform, null) + "|" + key + "|" + table.LocaleIdentifier.Code + "|rect=" + size + "|need=" + preferred + "|min=" + probe.fontSize);
                        }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            return "Measured " + cases + " localized layouts, " + targets.Count + " labels. Issues " + issues.Count + ":\n" + string.Join("\n", issues);
        }

        public static string ValidateArabicPresentation()
        {
            var preview = EditorSceneManager.NewPreviewScene();
            var canvas = new GameObject("Arabic test canvas", typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvas, preview);
            var go = new GameObject("Arabic test label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvas.transform, false);
            try
            {
                var text = go.GetComponent<TextMeshProUGUI>();
                text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Font/Nunito-Black SDF.asset");
                text.fontSize = 40;
                text.textWrappingMode = TextWrappingModes.Normal;
                text.rectTransform.sizeDelta = new Vector2(600, 800);
                var presenter = go.AddComponent<LocalizedArabicText>();
                typeof(LocalizedArabicText).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(presenter, null);
                text.SetText("المستوى 123\nمكافأة x2\nColor Flow 4.99 USD");
                presenter.Refresh();
                text.ForceMeshUpdate(true, true);
                if (!text.isRightToLeftText || text.textInfo.lineCount != 3) throw new Exception("Arabic multiline layout");
                var positions = new Dictionary<char, float>();
                foreach (var character in text.textInfo.characterInfo)
                    if (character.lineNumber == 0 && char.IsDigit(character.character)) positions[character.character] = character.origin;
                if (!positions.ContainsKey('1') || !(positions['1'] < positions['2'] && positions['2'] < positions['3']))
                    throw new Exception("Arabic numeric mesh order");
                if (!text.text.Contains("DSU 99.4 wolF roloC")) throw new Exception("Arabic mixed Latin run");
                if (LocalizedArabicText.Prepare("سلام") != "ﺳﻼﻡ") throw new Exception("Lam-alef shaping");
                string rich = LocalizedArabicText.Prepare("<b>المستوى 123</b> <color=#FF0000>مكافأة x2</color> <sprite=0>");
                foreach (string tag in new[] { "<b>", "</b>", "<color=#FF0000>", "</color>", "<sprite=0>" })
                    if (!rich.Contains(tag)) throw new Exception("Rich text tag: " + tag);
                text.SetText("Level 123");
                presenter.Refresh();
                if (text.isRightToLeftText || text.text != "Level 123") throw new Exception("Arabic to Latin switch");
                text.SetText("المستوى 123");
                presenter.Refresh();
                typeof(LocalizedArabicText).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(presenter, null);
                if (text.isRightToLeftText || text.text != "المستوى 123") throw new Exception("Arabic disable restoration");
                presenter.Refresh();
                text.rectTransform.sizeDelta = new Vector2(180, 800);
                text.SetText("أفرغ اللوحة ووفر مساحة لمواصلة اللعب.");
                presenter.Refresh();
                text.ForceMeshUpdate(true, true);
                if (text.textInfo.lineCount < 2) throw new Exception("Arabic automatic wrapping");
                return "PASS: Arabic joined forms/lam-alef, RTL mesh, digit positions, multiline/wrapping, mixed Color Flow/price, tags/sprite, SetText, locale switch, disable restoration.";
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}

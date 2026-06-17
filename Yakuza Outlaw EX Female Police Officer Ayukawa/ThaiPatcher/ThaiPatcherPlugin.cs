using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ThaiPatcher
{
    [BepInPlugin("com.thaipatcher.yakuzarogue", "ThaiPatcher", "1.0.0")]
    public class ThaiPatcherPlugin : BasePlugin
    {
        internal static new ManualLogSource Log = null!;
        private static Harmony _harmony = null!;

        // โฟลเดอร์เก็บไฟล์คำแปล: BepInEx/ThaiPatcher/
        internal static string DataDir => Path.Combine(BepInEx.Paths.BepInExRootPath, "ThaiPatcher");

        public override void Load()
        {
            Log = base.Log;
            _harmony = new Harmony("com.thaipatcher.yakuzarogue");

            Directory.CreateDirectory(DataDir);

            try
            {
                PatchLocalization();
            }
            catch (Exception ex)
            {
                Log.LogError($"[ThaiPatcher] Failed to patch: {ex}");
            }
        }

        private static void PatchLocalization()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "LqCoreCommon");

            if (asm == null)
            {
                Log.LogError("[ThaiPatcher] LqCoreCommon not found");
                return;
            }

            Type? packageDataType;
            try { packageDataType = asm.GetTypes().FirstOrDefault(t => t.Name == "PackageData"); }
            catch (ReflectionTypeLoadException ex) { packageDataType = ex.Types.FirstOrDefault(t => t?.Name == "PackageData"); }

            if (packageDataType == null)
            {
                Log.LogError("[ThaiPatcher] PackageData not found");
                return;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var postfix = new HarmonyMethod(typeof(ThaiPatcherPlugin).GetMethod(nameof(AfterInitTable)));

            var uiMethod = packageDataType.GetMethod("InitLocalizationUITable", flags);
            var dialogMethod = packageDataType.GetMethod("InitLocalizationDialogTable", flags);

            if (uiMethod != null)
            {
                _harmony.Patch(uiMethod, postfix: postfix);
                Log.LogInfo("[ThaiPatcher] Patched InitLocalizationUITable ✓");
            }
            if (dialogMethod != null)
            {
                _harmony.Patch(dialogMethod, postfix: postfix);
                Log.LogInfo("[ThaiPatcher] Patched InitLocalizationDialogTable ✓");
            }
        }

        public static void AfterInitTable(object __instance, MethodBase __originalMethod)
        {
            try
            {
                bool isDialog = __originalMethod.Name.Contains("Dialog");
                string jsonFile = isDialog ? "thai_dialog.json" : "thai_ui.json";
                string jsonPath = Path.Combine(DataDir, jsonFile);

                if (!File.Exists(jsonPath))
                {
                    Log.LogInfo($"[ThaiPatcher] ไม่พบ {jsonFile} — ข้าม");
                    return;
                }

                var translations = LoadJson(jsonPath);
                if (translations.Count == 0) return;

                string getter = isDialog ? "get_dicLocalizationDialog" : "get_dicLocalizationUi";
                var getMethod = __instance.GetType().GetMethod(getter,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (getMethod == null)
                {
                    Log.LogError($"[ThaiPatcher] {getter} not found");
                    return;
                }

                var dictObj = getMethod.Invoke(__instance, null);
                if (dictObj == null) return;

                int total = InjectTranslations(dictObj, translations);
                Log.LogInfo($"[ThaiPatcher] {jsonFile}: inject {total} entries สำเร็จ");
            }
            catch (Exception ex)
            {
                Log.LogError($"[ThaiPatcher] AfterInitTable error: {ex}");
            }
        }

        private static Dictionary<string, string> LoadJson(string path)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var text = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(text);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(v))
                        result[prop.Name] = v;
                }
                Log.LogInfo($"[ThaiPatcher] โหลด {result.Count} entries จาก {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[ThaiPatcher] LoadJson error: {ex.Message}");
            }
            return result;
        }

        // inject เข้า Dictionary<SystemLanguage, Dictionary<string,string>> ทุก language
        private static int InjectTranslations(object outerDict, Dictionary<string, string> translations)
        {
            int count = 0;
            foreach (var outerEntry in Il2CppIterate(outerDict))
            {
                var outerType = outerEntry.GetType();
                var innerObj = outerType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)
                                        ?.GetValue(outerEntry);
                if (innerObj == null) continue;

                var setItem = innerObj.GetType().GetMethod("set_Item",
                    BindingFlags.Public | BindingFlags.Instance);
                if (setItem == null) continue;

                foreach (var kv in translations)
                {
                    try
                    {
                        setItem.Invoke(innerObj, new object[] { kv.Key, kv.Value });
                        count++;
                    }
                    catch { }
                }
                // นับแค่ครั้งเดียว (ทุก lang เหมือนกัน)
                break;
            }
            return count;
        }

        private static IEnumerable<object> Il2CppIterate(object dictObj)
        {
            var t = dictObj.GetType();
            var getEnum = t.GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance);
            if (getEnum == null) yield break;

            var enumerator = getEnum.Invoke(dictObj, null);
            var enumType = enumerator.GetType();
            var moveNext = enumType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
            var getCurrent = enumType.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
            if (moveNext == null || getCurrent == null) yield break;

            while ((bool)moveNext.Invoke(enumerator, null))
                yield return getCurrent.Invoke(enumerator, null);
        }
    }
}

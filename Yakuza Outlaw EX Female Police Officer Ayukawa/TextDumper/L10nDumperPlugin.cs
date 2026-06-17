using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TextDumper
{
    [BepInPlugin("com.l10ndumper.yakuzarogue", "L10nDumper", "1.0.0")]
    public class L10nDumperPlugin : BasePlugin
    {
        internal static new ManualLogSource Log = null!;
        private static Harmony _harmony = null!;

        public override void Load()
        {
            Log = base.Log;
            _harmony = new Harmony("com.l10ndumper.yakuzarogue");

            // ค้นหา PackageData type ตอน runtime แล้วค่อย patch
            try
            {
                PatchLocalizationMethods();
            }
            catch (Exception ex)
            {
                Log.LogError($"[L10nDumper] Failed to patch: {ex}");
            }
        }

        private static void PatchLocalizationMethods()
        {
            // PackageData อยู่ใน LqCoreCommon ไม่ใช่ Assembly-CSharp
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "LqCoreCommon");

            if (asm == null)
            {
                Log.LogError("[L10nDumper] LqCoreCommon not found. Loaded assemblies:");
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    Log.LogInfo($"  {a.GetName().Name}");
                return;
            }

            Log.LogInfo($"[L10nDumper] Found LqCoreCommon: {asm.FullName}");

            // หา PackageData โดยตรง
            Type? packageDataType = TryGetTypes(asm).FirstOrDefault(t => t.Name == "PackageData");

            if (packageDataType == null)
            {
                Log.LogError("[L10nDumper] PackageData not found in LqCoreCommon");
                return;
            }

            Log.LogInfo($"[L10nDumper] Found PackageData: {packageDataType.FullName}");

            // patch InitLocalizationUITable และ InitLocalizationDialogTable (private instance, no params)
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var uiMethod = packageDataType.GetMethod("InitLocalizationUITable", flags);
            var dialogMethod = packageDataType.GetMethod("InitLocalizationDialogTable", flags);

            if (uiMethod != null)
            {
                _harmony.Patch(uiMethod,
                    postfix: new HarmonyMethod(typeof(L10nPatches).GetMethod(nameof(L10nPatches.AfterInitTable))));
                Log.LogInfo("[L10nDumper] Patched InitLocalizationUITable ✓");
            }
            else
                Log.LogWarning("[L10nDumper] InitLocalizationUITable not found");

            if (dialogMethod != null)
            {
                _harmony.Patch(dialogMethod,
                    postfix: new HarmonyMethod(typeof(L10nPatches).GetMethod(nameof(L10nPatches.AfterInitTable))));
                Log.LogInfo("[L10nDumper] Patched InitLocalizationDialogTable ✓");
            }
            else
                Log.LogWarning("[L10nDumper] InitLocalizationDialogTable not found");
        }


        private static IEnumerable<Type> TryGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
        }

        // เรียกจาก patch หลัง method ทำงาน
        internal static void DumpInstance(object instance, string methodName)
        {
            Log.LogInfo($"[L10nDumper] {methodName} finished — reading dict properties...");

            var type = instance.GetType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // dicLocalizationUi และ dicLocalizationDialog เป็น property ใน IL2CPP
            // เข้าถึงผ่าน getter method ชื่อ get_dicLocalizationUi / get_dicLocalizationDialog
            var targets = new (string getter, string label)[]
            {
                ("get_dicLocalizationUi",     "L10nUI"),
                ("get_dicLocalizationDialog", "L10nDialog"),
            };

            foreach (var (getter, label) in targets)
            {
                var getMethod = type.GetMethod(getter, flags);
                if (getMethod == null)
                {
                    Log.LogWarning($"[L10nDumper] {getter} not found on {type.Name}");

                    // debug: แสดง method ทั้งหมดที่มี
                    var allMethods = type.GetMethods(flags).Select(m => m.Name).OrderBy(x => x);
                    Log.LogInfo($"[L10nDumper] Available methods on {type.Name}:");
                    foreach (var mn in allMethods)
                        Log.LogInfo($"  {mn}");
                    continue;
                }

                var dictObj = getMethod.Invoke(instance, null);
                if (dictObj == null)
                {
                    Log.LogWarning($"[L10nDumper] {getter} returned null");
                    continue;
                }

                Log.LogInfo($"[L10nDumper] {label} dict type: {dictObj.GetType().FullName}");
                DumpDictionary(label, dictObj);
            }
        }

        // iterate IL2CPP object ผ่าน reflection (ใช้กับ Il2CppSystem.Collections.Generic.Dictionary)
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

        // dict คือ Dictionary<SystemLanguage, Dictionary<string, string>>
        internal static void DumpDictionary(string label, object dict)
        {
            var byLang = new Dictionary<string, Dictionary<string, string>>();

            try
            {
                foreach (var outerEntry in Il2CppIterate(dict))
                {
                    var outerType = outerEntry.GetType();
                    var langObj  = outerType.GetProperty("Key",   BindingFlags.Public | BindingFlags.Instance)?.GetValue(outerEntry);
                    var innerObj = outerType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(outerEntry);
                    if (langObj == null || innerObj == null) continue;

                    var langName = langObj.ToString() ?? "Unknown";
                    var inner = new Dictionary<string, string>();

                    foreach (var innerEntry in Il2CppIterate(innerObj))
                    {
                        var it = innerEntry.GetType();
                        var k = it.GetProperty("Key",   BindingFlags.Public | BindingFlags.Instance)?.GetValue(innerEntry)?.ToString() ?? "";
                        var v = it.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(innerEntry)?.ToString() ?? "";
                        inner[k] = v;
                    }

                    byLang[langName] = inner;
                    Log.LogInfo($"[L10nDumper] {label}[{langName}]: {inner.Count} entries");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[L10nDumper] {label} error: {ex}");
            }

            foreach (var (lang, data) in byLang)
                WriteJson($"{label}_{lang}", data);
        }

        internal static void WriteJson(string label, Dictionary<string, string> data)
        {
            if (data.Count == 0)
            {
                Log.LogWarning($"[L10nDumper] {label}: 0 entries, skipping");
                return;
            }

            var outputDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "L10nDumps");
            Directory.CreateDirectory(outputDir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            int i = 0;
            foreach (var kv in data)
            {
                i++;
                var comma = i < data.Count ? "," : "";
                sb.AppendLine($"  {JsonStr(kv.Key)}: {JsonStr(kv.Value)}{comma}");
            }
            sb.AppendLine("}");

            var fileName = $"{label}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var fullPath = Path.Combine(outputDir, fileName);
            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

            Log.LogInfo($"[L10nDumper] Saved {data.Count} entries → {fullPath}");
        }

        private static string JsonStr(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
    }

    public static class L10nPatches
    {
        // generic postfix ที่ใช้ได้กับทั้ง InitLocalizationUITable และ InitLocalizationDialogTable
        public static void AfterInitTable(object __instance, MethodBase __originalMethod)
        {
            try
            {
                L10nDumperPlugin.DumpInstance(__instance, __originalMethod.Name);
            }
            catch (Exception ex)
            {
                L10nDumperPlugin.Log.LogError($"[L10nDumper] Postfix error: {ex}");
            }
        }
    }
}

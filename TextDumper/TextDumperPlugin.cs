using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TextDumper
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class TextDumperPlugin : BasePlugin
    {
        internal static new ManualLogSource Log = null!;
        private Harmony _harmony = null!;

        public override void Load()
        {
            Log = base.Log;
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            ClassInjector.RegisterTypeInIl2Cpp<TextDumperBehaviour>();

            SceneManager.sceneLoaded += (Action<Scene, LoadSceneMode>)OnSceneLoaded;

            Log.LogInfo("TextDumper loaded. Press F7 to dump current scene. Press F6 to highlight text under mouse.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var go = new GameObject("TextDumperBehaviour");
            go.AddComponent<TextDumperBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }

    public class TextDumperBehaviour : MonoBehaviour
    {
        private bool _overlayEnabled = false;
        private string _hoverInfo = "";

        public TextDumperBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
                DumpAllText();

            if (Input.GetKeyDown(KeyCode.F6))
            {
                _overlayEnabled = !_overlayEnabled;
                TextDumperPlugin.Log.LogInfo(_overlayEnabled
                    ? "Hover mode ON — เอาเมาส์ชี้ที่ text"
                    : "Hover mode OFF");
            }

            if (_overlayEnabled)
                UpdateHoverTarget();
        }

        private void UpdateHoverTarget()
        {
            var mousePos = Input.mousePosition;

            // ตรวจสอบ TextMeshProUGUI ทุกตัวในฉาก
            var allTmps = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            TextMeshProUGUI? nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var tmp in allTmps)
            {
                if (!tmp.isActiveAndEnabled) continue;
                var rt = tmp.rectTransform;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                // แปลง world corners เป็น screen space
                var cam = Camera.main;
                if (cam == null) continue;

                bool inside = IsScreenPointInRect(mousePos, rt);
                if (inside)
                {
                    var center = rt.position;
                    var screenCenter = cam.WorldToScreenPoint(center);
                    float dist = Vector2.Distance(new Vector2(mousePos.x, mousePos.y),
                                                  new Vector2(screenCenter.x, screenCenter.y));
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = tmp;
                    }
                }
            }

            if (nearest != null)
            {
                _hoverInfo = $"TMP: \"{nearest.text}\"\nPath: {GetPath(nearest.gameObject)}";
            }
            else
            {
                // ตรวจสอบ Legacy Text
                var allTexts = UnityEngine.Object.FindObjectsOfType<Text>();
                Text? nearestText = null;
                nearestDist = float.MaxValue;

                foreach (var t in allTexts)
                {
                    if (!t.isActiveAndEnabled) continue;
                    var rt = t.rectTransform;
                    bool inside = IsScreenPointInRect(mousePos, rt);
                    if (inside)
                    {
                        var cam = Camera.main;
                        if (cam == null) continue;
                        var screenCenter = cam.WorldToScreenPoint(rt.position);
                        float dist = Vector2.Distance(new Vector2(mousePos.x, mousePos.y),
                                                      new Vector2(screenCenter.x, screenCenter.y));
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearestText = t;
                        }
                    }
                }

                if (nearestText != null)
                    _hoverInfo = $"Text: \"{nearestText.text}\"\nPath: {GetPath(nearestText.gameObject)}";
                else
                    _hoverInfo = "";
            }
        }

        private bool IsScreenPointInRect(Vector3 screenPos, RectTransform rt)
        {
            var cam = rt.GetComponentInParent<Canvas>()?.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, new Vector2(screenPos.x, screenPos.y), cam);
        }

        private void OnGUI()
        {
            if (!_overlayEnabled || string.IsNullOrEmpty(_hoverInfo)) return;

            var mousePos = Input.mousePosition;
            // Unity GUI y-axis กลับด้าน
            float guiY = Screen.height - mousePos.y - 80f;
            float guiX = mousePos.x + 10f;

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            var content = new GUIContent(_hoverInfo);
            var size = style.CalcSize(content);
            size.x = Mathf.Max(size.x, 300f);

            // ป้องกันกรอบออกนอกจอ
            if (guiX + size.x > Screen.width) guiX = Screen.width - size.x - 10f;
            if (guiY + size.y > Screen.height) guiY = Screen.height - size.y - 10f;
            if (guiY < 0) guiY = 0;

            GUI.Box(new Rect(guiX, guiY, size.x + 10f, size.y + 10f), _hoverInfo, style);
        }

        private void DumpAllText()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            var sb = new StringBuilder();
            sb.AppendLine($"=== TextDumper — Scene: {sceneName} ===");
            sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            sb.AppendLine();

            var seen = new HashSet<string>();

            // Dump TextMeshPro
            int tmpCount = 0;
            sb.AppendLine("--- TextMeshPro (TMP) ---");
            var tmps = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            foreach (var t in tmps)
            {
                if (string.IsNullOrWhiteSpace(t.text)) continue;
                var path = GetPath(t.gameObject);
                var line = $"{path}\t→\t\"{t.text}\"";
                if (seen.Add(line))
                {
                    sb.AppendLine(line);
                    tmpCount++;
                }
            }

            // Dump TextMeshPro (world space)
            var tmpsWorld = UnityEngine.Object.FindObjectsOfType<TextMeshPro>();
            foreach (var t in tmpsWorld)
            {
                if (string.IsNullOrWhiteSpace(t.text)) continue;
                var path = GetPath(t.gameObject);
                var line = $"{path}\t→\t\"{t.text}\"";
                if (seen.Add(line))
                {
                    sb.AppendLine(line);
                    tmpCount++;
                }
            }

            sb.AppendLine();

            // Dump Legacy UI Text
            int legacyCount = 0;
            sb.AppendLine("--- Legacy UI Text ---");
            var texts = UnityEngine.Object.FindObjectsOfType<Text>();
            foreach (var t in texts)
            {
                if (string.IsNullOrWhiteSpace(t.text)) continue;
                var path = GetPath(t.gameObject);
                var line = $"{path}\t→\t\"{t.text}\"";
                if (seen.Add(line))
                {
                    sb.AppendLine(line);
                    legacyCount++;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"=== รวม TMP: {tmpCount}  Legacy: {legacyCount}  ทั้งหมด: {tmpCount + legacyCount} ===");

            // เขียนไฟล์
            var outputDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "TextDumps");
            Directory.CreateDirectory(outputDir);
            var fileName = $"dump_{sceneName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var fullPath = Path.Combine(outputDir, fileName);
            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

            TextDumperPlugin.Log.LogInfo($"[TextDumper] Dumped {tmpCount + legacyCount} texts → {fullPath}");
        }

        private static string GetPath(GameObject go)
        {
            var parts = new List<string>();
            var current = go.transform;
            while (current != null)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }
    }
}

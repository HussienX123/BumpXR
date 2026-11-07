#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BumpXR.EditorTools
{
    /// <summary>
    /// Editor-only batchmode helpers (lives in an Editor folder, never ships in the APK).
    /// </summary>
    public static class BumpXRBuildTools
    {
        /// <summary>
        /// Apply Meta Project Setup Tool "Required" fixes for Android, enable Controllers+Hands, set target API 34.
        /// Run with: -buildTarget Android -executeMethod BumpXR.EditorTools.BumpXRBuildTools.ApplyProjectSetup
        /// </summary>
        public static void ApplyProjectSetup()
        {
            Debug.Log("[BumpXR] Active build target: " + EditorUserBuildSettings.activeBuildTarget);

            var config = OVRProjectConfig.CachedProjectConfig;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            OVRProjectConfig.CommitProjectConfig(config);

            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;

            var group = BuildTargetGroup.Android;
            var setupType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("OVRProjectSetup")).First(t => t != null);
            var getTasks = setupType.GetMethod("GetTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var tasks = (System.Collections.IEnumerable)getTasks.Invoke(null, new object[] { group });
            foreach (var task in tasks)
            {
                var tt = task.GetType();
                var isDone = (Func<BuildTargetGroup, bool>)tt.GetProperty("IsDone").GetValue(task);
                if (isDone(group)) continue;
                string level = Eval(tt, task, "Level", group);
                string message = Eval(tt, task, "Message", group);
                if (level == "Required")
                {
                    string fixMsg = Eval(tt, task, "FixMessage", group);
                    object ok = tt.GetMethod("Fix").Invoke(task, new object[] { group });
                    Debug.Log($"[BumpXR] PST FIX ({fixMsg}) => {ok}");
                }
                else
                {
                    Debug.Log($"[BumpXR] PST remaining [{level}] {message}");
                }
            }

            AssetDatabase.SaveAssets();
        }


        // ------------------------------------------------------------------ Phase 2 builders

        private const string PokeTemplateGuid = "4db41829582c7d24f80ee9603868dd67"; // Template_PokeInteraction (ISDK quick action)
        private const string RayTemplateGuid = "8369d93f7b6b99742bbea0649a41b7b1";  // Template_RayInteraction (ISDK quick action)
        private const string MainScenePath = "Assets/BumpXR/Scenes/BumpMain.unity";
        private const string DiscoveryScenePath = "Assets/BumpXR/Scenes/DiscoveryTest.unity";

        /// <summary>Generate 6 small sample images as raw JPEG/PNG bytes (TextAssets) in Resources/SamplePhotos.</summary>
        public static void GenerateSamplePhotos()
        {
            const string dir = "Assets/BumpXR/Resources/SamplePhotos";
            System.IO.Directory.CreateDirectory(dir);
            var specs = new (string name, Color top, Color bottom, bool png)[]
            {
                ("sunset.jpg", new Color(1f, 0.55f, 0.2f), new Color(0.55f, 0.1f, 0.4f), false),
                ("ocean.png", new Color(0.2f, 0.8f, 1f), new Color(0.05f, 0.2f, 0.6f), true),
                ("forest.jpg", new Color(0.6f, 0.9f, 0.4f), new Color(0.05f, 0.35f, 0.15f), false),
                ("violet.png", new Color(0.85f, 0.6f, 1f), new Color(0.3f, 0.1f, 0.6f), true),
                ("citrus.jpg", new Color(1f, 0.95f, 0.3f), new Color(1f, 0.5f, 0.1f), false),
                ("night.png", new Color(0.15f, 0.15f, 0.35f), new Color(0.02f, 0.02f, 0.08f), true),
            };

            for (int s = 0; s < specs.Length; s++)
            {
                const int w = 320, h = 240;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                var cx = w * (0.25f + 0.1f * s);
                var cy = h * 0.6f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color c = Color.Lerp(specs[s].bottom, specs[s].top, y / (float)h);
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d < 48f) c = Color.Lerp(Color.white, c, Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (d - 30f) / 18f)));
                        tex.SetPixel(x, y, c);
                    }
                tex.Apply();
                byte[] bytes = specs[s].png ? tex.EncodeToPNG() : tex.EncodeToJPG(85);
                UnityEngine.Object.DestroyImmediate(tex);
                System.IO.File.WriteAllBytes($"{dir}/{specs[s].name}.bytes", bytes);
            }

            AssetDatabase.Refresh();
            Debug.Log("[BumpXR] Sample photos generated: " + specs.Length);
        }

        /// <summary>Create Assets/BumpXR/Scenes/BumpMain.unity with the hands-first interaction rig and two poke/ray canvases.</summary>
        public static void BuildBumpMain()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

            // Install OVRCameraRig + OVRComprehensiveInteractionRig via Meta's official QuickActions API.
            Oculus.Interaction.OVR.Editor.QuickActions.OVRQuickActionsAPI.AddOVRInteractionRig(generateAsEditableCopy: false);
            var isdkRef = UnityEngine.Object.FindFirstObjectByType<Oculus.Interaction.Input.OVRCameraRigRef>();
            if (isdkRef != null) AddHandVisuals(isdkRef.gameObject);

            RectTransform share = MakeInteractiveCanvas("ShareCanvas", new Vector2(900f, 600f), 0.0006f, new Color(0.05f, 0.06f, 0.1f, 0.92f));
            RectTransform incoming = MakeInteractiveCanvas("IncomingCard", new Vector2(520f, 380f), 0.0008f, new Color(0.08f, 0.1f, 0.18f, 0.97f));
            share.position = new Vector3(0f, 1.3f, 0.6f);
            incoming.position = new Vector3(0f, 1.3f, 0.45f);

            var main = new GameObject("BumpMain", typeof(BumpXR.UI.BumpMainController));
            var so = new SerializedObject(main.GetComponent<BumpXR.UI.BumpMainController>());
            so.FindProperty("shareRoot").objectReferenceValue = share;
            so.FindProperty("incomingRoot").objectReferenceValue = incoming;
            so.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory("Assets/BumpXR/Scenes");
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, MainScenePath);
            AddScenesToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[BumpXR] BumpMain scene created at " + MainScenePath);
        }

        private static RectTransform MakeInteractiveCanvas(string name, Vector2 size, float scale, Color bg)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster), typeof(UnityEngine.UI.Image));
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<UnityEngine.UI.Image>().color = bg;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * scale;

            // Official Meta Interaction SDK QuickActions API for Canvas Poke + Pinch-Ray interaction.
            Oculus.Interaction.Editor.QuickActions.QuickActionsAPI.AddPokeCanvasInteraction(go, fixPointableCanvasModule: true);
            Oculus.Interaction.Editor.QuickActions.QuickActionsAPI.AddRayCanvasInteraction(go, fixPointableCanvasModule: true);

            return rt;
        }

        private static void AddHandVisuals(GameObject isdkRig)
        {
            var hands = UnityEngine.Object.FindObjectsByType<Oculus.Interaction.Input.Hand>(FindObjectsSortMode.None);
            foreach (string side in new[] { "Left", "Right" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/Hands/OVRHandVisual{side}.prefab");
                if (prefab == null) { Debug.LogWarning("[BumpXR] hand visual prefab missing: " + side); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.SetParent(isdkRig.transform, false);
                var wanted = side == "Left" ? Oculus.Interaction.Input.Handedness.Left : Oculus.Interaction.Input.Handedness.Right;
                var hand = hands.FirstOrDefault(h => h.Handedness == wanted);
                foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    var so = new SerializedObject(mb);
                    var prop = so.FindProperty("_hand");
                    if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null)
                    {
                        prop.objectReferenceValue = hand;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        Debug.Log($"[BumpXR] wired {mb.GetType().Name}._hand ({side}) => {(hand ? hand.name : "NULL")}");
                    }
                }
            }
        }

        /// <summary>BumpMain first (launch scene), then DiscoveryTest, then the original SampleScene.</summary>
        public static void AddScenesToBuildSettings()
        {
            var list = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { DiscoveryScenePath, MainScenePath })
            {
                list.RemoveAll(s => s.path == path);
                list.Insert(0, new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[BumpXR] Build scenes: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
        }

        /// <summary>Attempt an Android APK build to Builds/BumpXR.apk. Does not touch signing.</summary>
        public static void BuildApk()
        {
            System.IO.Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "Builds/BumpXR.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[BumpXR] APK build result: {report.summary.result}, size {report.summary.totalSize} bytes, errors {report.summary.totalErrors}");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) Debug.Log("[BumpXR] BUILD ERROR: " + m.content);
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
        /// <summary>Print remaining PST tasks (read-only).</summary>
        public static void ReportProjectSetup()
        {
            var group = BuildTargetGroup.Android;
            var setupType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("OVRProjectSetup")).First(t => t != null);
            var getTasks = setupType.GetMethod("GetTasks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var tasks = (System.Collections.IEnumerable)getTasks.Invoke(null, new object[] { group });
            foreach (var task in tasks)
            {
                var tt = task.GetType();
                var isDone = (Func<BuildTargetGroup, bool>)tt.GetProperty("IsDone").GetValue(task);
                if (isDone(group)) continue;
                Debug.Log($"[BumpXR] PST open [{Eval(tt, task, "Level", group)}] {Eval(tt, task, "Message", group)}");
            }
        }


        /// <summary>Log the hierarchy + components of prefabs (read-only research helper).</summary>
        public static void DumpPrefabs()
        {
            string[] guids = { "4db41829582c7d24f80ee9603868dd67", "8369d93f7b6b99742bbea0649a41b7b1" };
            string[] paths = {
                "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRInteraction.prefab",
                "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRComprehensiveInteractionRig.prefab",
                "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab" };
            var all = guids.Select(AssetDatabase.GUIDToAssetPath).Concat(paths);
            foreach (var p in all)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var sb = new System.Text.StringBuilder("[BumpXR] DUMP " + p + " => " + (go ? "loaded" : "NULL") + "\n");
                if (go) Dump(go.transform, 0, sb);
                Debug.Log(sb.ToString());
            }
        }

        private static void Dump(Transform t, int depth, System.Text.StringBuilder sb)
        {
            var comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(" [").Append(comps).Append("]\n");
            if (depth < 4) foreach (Transform c in t) Dump(c, depth + 1, sb);
        }
        private static string Eval(Type taskType, object task, string prop, BuildTargetGroup group)
        {
            var lambda = taskType.GetProperty(prop).GetValue(task);
            return lambda.GetType().GetMethod("GetValue").Invoke(lambda, new object[] { group })?.ToString();
        }
    }
}
#endif


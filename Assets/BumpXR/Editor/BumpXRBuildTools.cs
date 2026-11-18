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
                    if (message != null && (message.IndexOf("Keystore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            message.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            message.IndexOf("App ID", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Debug.Log($"[BumpXR] PST skipped (protected setting) [{level}] {message}");
                        continue;
                    }
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


        // ------------------------------------------------------------------ Phase 3 setup

        /// <summary>
        /// Phase 3: create Resources/BumpSettings.asset (bump thresholds) and enable passthrough in BumpMain
        /// (OVRProjectConfig.insightPassthroughSupport, OVRManager.isInsightPassthroughEnabled, OVRPassthroughLayer,
        /// transparent camera background, no skybox). Docs: https://developers.meta.com/vr/documentation/unity/unity-passthrough-gs
        /// Run with: -executeMethod BumpXR.EditorTools.BumpXRBuildTools.Phase3Setup
        /// </summary>
        public static void Phase3Setup()
        {
            const string settingsPath = "Assets/BumpXR/Resources/BumpSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<BumpXR.Core.BumpSettings>(settingsPath) == null)
            {
                System.IO.Directory.CreateDirectory("Assets/BumpXR/Resources");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BumpXR.Core.BumpSettings>(), settingsPath);
                Debug.Log("[BumpXR] created " + settingsPath);
            }

            var config = OVRProjectConfig.CachedProjectConfig;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            OVRProjectConfig.CommitProjectConfig(config);

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MainScenePath);
            var rig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            var manager = UnityEngine.Object.FindFirstObjectByType<OVRManager>();
            if (rig == null || manager == null) throw new InvalidOperationException("OVRCameraRig/OVRManager not found in BumpMain");

            manager.isInsightPassthroughEnabled = true;
            EditorUtility.SetDirty(manager);
            if (UnityEngine.Object.FindFirstObjectByType<OVRPassthroughLayer>() == null)
            {
                var layer = new GameObject("PassthroughLayer");
                layer.transform.SetParent(rig.transform, false);
                layer.AddComponent<OVRPassthroughLayer>();
            }

            var cam = rig.centerEyeAnchor.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            EditorUtility.SetDirty(cam);
            RenderSettings.skybox = null;

            // URP 32-bit HDR (R11G11B10) drops framebuffer alpha and blocks Insight Passthrough underlay on Quest; disable HDR on Mobile_RPAsset.
            var mobileRp = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/Mobile_RPAsset.asset");
            if (mobileRp != null)
            {
                var rpSo = new SerializedObject(mobileRp);
                var hdrProp = rpSo.FindProperty("m_SupportsHDR");
                if (hdrProp != null && hdrProp.boolValue)
                {
                    hdrProp.boolValue = false;
                    rpSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mobileRp);
                    Debug.Log("[BumpXR] Disabled m_SupportsHDR on Assets/Settings/Mobile_RPAsset.asset so alpha=0 reaches Passthrough underlay");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            ApplyProjectSetup();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[BumpXR] Phase3Setup done: passthrough support=Supported, OVRManager.isInsightPassthroughEnabled=true, OVRPassthroughLayer present");
        }

        // ------------------------------------------------------------------ Phase 4 setup & release APK

        /// <summary>
        /// Phase 4: release identity (com.hussienx.bumpxr, HussienX, BumpXR, 0.1.0, versionCode 1),
        /// procedural 512x512 ring+fist icon, and disable unused Scene/Anchor support so USE_SCENE and
        /// USE_ANCHOR_API drop out of the merged manifest while keeping Passthrough, Hands, and Colocation Discovery.
        /// Run with: -executeMethod BumpXR.EditorTools.BumpXRBuildTools.Phase4Setup
        /// </summary>
        public static void Phase4Setup()
        {
            PlayerSettings.companyName = "HussienX";
            PlayerSettings.productName = "BumpXR";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.hussienx.bumpxr");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;

            GenerateAppIcon();

            var config = OVRProjectConfig.CachedProjectConfig;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.None;
            config.sharedAnchorSupport = OVRProjectConfig.FeatureSupport.None;
            var cfgSo = new SerializedObject(config);
            var anchorProp = cfgSo.FindProperty("anchorSupport");
            if (anchorProp != null) anchorProp.intValue = 0; // AnchorSupport.Disabled
            var sceneProp = cfgSo.FindProperty("sceneSupport");
            if (sceneProp != null) sceneProp.intValue = 0; // FeatureSupport.None
            var sharedAnchorProp = cfgSo.FindProperty("sharedAnchorSupport");
            if (sharedAnchorProp != null) sharedAnchorProp.intValue = 0;
            cfgSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            OVRProjectConfig.CommitProjectConfig(config);

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MainScenePath);
            var manager = UnityEngine.Object.FindFirstObjectByType<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
                var mgrSo = new SerializedObject(manager);
                var reqScene = mgrSo.FindProperty("requestScenePermissionOnStartup");
                if (reqScene != null && reqScene.boolValue) reqScene.boolValue = false;
                mgrSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[BumpXR] Phase4Setup done: id={PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android)} " +
                      $"v={PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}), sceneSupport={config.sceneSupport}, sharedAnchor={config.sharedAnchorSupport}");
        }

        /// <summary>Phase 5A: bump version to 0.1.1 (versionCode 2) and serialize new BumpSettings gate fields.</summary>
        public static void Phase5ASetup()
        {
            PlayerSettings.bundleVersion = "0.1.1";
            PlayerSettings.Android.bundleVersionCode = 2;

            var config = OVRProjectConfig.CachedProjectConfig;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.None;
            config.sharedAnchorSupport = OVRProjectConfig.FeatureSupport.None;
            var cfgSo = new SerializedObject(config);
            var anchorProp = cfgSo.FindProperty("anchorSupport");
            if (anchorProp != null) anchorProp.intValue = 0;
            var sceneProp = cfgSo.FindProperty("sceneSupport");
            if (sceneProp != null) sceneProp.intValue = 0;
            var sharedAnchorProp = cfgSo.FindProperty("sharedAnchorSupport");
            if (sharedAnchorProp != null) sharedAnchorProp.intValue = 0;
            cfgSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            OVRProjectConfig.CommitProjectConfig(config);

            var settings = AssetDatabase.LoadAssetAtPath<BumpXR.Core.BumpSettings>("Assets/BumpXR/Resources/BumpSettings.asset");
            if (settings != null) EditorUtility.SetDirty(settings);

            AssetDatabase.SaveAssets();
            Debug.Log($"[BumpXR] Phase5ASetup done: v={PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}), scene={config.sceneSupport}, sharedAnchor={config.sharedAnchorSupport}");
        }

        /// <summary>Generate a 512x512 procedural PNG app icon (cyan bump ring + stylized fist glyph) and assign in PlayerSettings.</summary>
        public static void GenerateAppIcon()
        {
            const string iconPath = "Assets/BumpXR/Resources/AppIcon.png";
            System.IO.Directory.CreateDirectory("Assets/BumpXR/Resources");
            const int n = 512;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var pixels = new Color[n * n];
            var center = new Vector2(255.5f, 255.5f);
            Color bgInner = new Color(0.08f, 0.13f, 0.25f, 1f);
            Color bgOuter = new Color(0.03f, 0.05f, 0.10f, 1f);
            Color cyan = new Color(0.18f, 0.90f, 1.00f, 1f);
            Color fistFill = new Color(0.95f, 0.99f, 1.00f, 1f);
            Color seam = new Color(0.05f, 0.08f, 0.16f, 1f);

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x, y);
                    float d = Vector2.Distance(p, center);
                    Color c = Color.Lerp(bgInner, bgOuter, Mathf.Clamp01(d / 260f));

                    // Outer bump ring + soft halo + bright impact spark at upper-right
                    float ringSd = Mathf.Abs(d - 198f) - 11f;
                    float halo = Mathf.Exp(-Mathf.Max(0f, ringSd) * 0.085f) * 0.42f;
                    float spark = Mathf.Exp(-Vector2.Distance(p, new Vector2(396f, 396f)) * 0.035f) * 0.55f;
                    c = Color.Lerp(c, cyan, Mathf.Clamp01(halo));
                    c = Color.Lerp(c, Color.Lerp(cyan, Color.white, Mathf.Clamp01(spark)), Mathf.Clamp01(0.5f - ringSd * 0.8f));

                    // Fist base (palm + wrist cuff)
                    float palmSd = Mathf.Min(
                        SdRoundedBox(p - new Vector2(256f, 214f), new Vector2(76f, 46f), 22f),
                        SdRoundedBox(p - new Vector2(256f, 152f), new Vector2(50f, 24f), 12f));
                    c = Color.Lerp(c, fistFill, Mathf.Clamp01(0.5f - palmSd * 0.8f));

                    // Four curled finger knuckles across the top with crisp seam outlines
                    float fingersSd = 1e5f;
                    for (int i = 0; i < 4; i++)
                    {
                        float fx = 199f + i * 38f;
                        fingersSd = Mathf.Min(fingersSd, SdRoundedBox(p - new Vector2(fx, 284f), new Vector2(16f, 38f), 14f));
                    }
                    c = Color.Lerp(c, seam, Mathf.Clamp01(0.5f - (fingersSd - 5.5f) * 0.8f));
                    c = Color.Lerp(c, fistFill, Mathf.Clamp01(0.5f - fingersSd * 0.8f));

                    // Tucked thumb bar across front with seam cutout
                    Vector2 tp = p - new Vector2(240f, 224f);
                    float rad = -0.12f;
                    Vector2 tr = new Vector2(tp.x * Mathf.Cos(rad) - tp.y * Mathf.Sin(rad), tp.x * Mathf.Sin(rad) + tp.y * Mathf.Cos(rad));
                    float thumbSd = SdRoundedBox(tr, new Vector2(56f, 16f), 14f);
                    c = Color.Lerp(c, seam, Mathf.Clamp01(0.5f - (thumbSd - 6f) * 0.8f));
                    c = Color.Lerp(c, fistFill, Mathf.Clamp01(0.5f - thumbSd * 0.8f));

                    c.a = 1f;
                    pixels[y * n + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(iconPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }

            var iconAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (iconAsset != null)
            {
                int[] sizes = PlayerSettings.GetIconSizes(UnityEditor.Build.NamedBuildTarget.Unknown, IconKind.Any);
                var slots = Enumerable.Repeat(iconAsset, Mathf.Max(1, sizes.Length)).ToArray();
                PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, slots, IconKind.Any);
                Debug.Log("[BumpXR] Procedural 512x512 app icon generated & assigned: " + iconPath);
            }
        }

        private static float SdRoundedBox(Vector2 p, Vector2 b, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x) - b.x + r, Mathf.Abs(p.y) - b.y + r);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
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
        public static void BuildApk() => BuildApkInternal("Builds/BumpXR.apk");

        /// <summary>Phase 4 release build to Builds/BumpXR-v0.1.0.apk with Development Build OFF. Does not touch signing.</summary>
        public static void BuildReleaseApk() => BuildApkInternal("Builds/BumpXR-v0.1.0.apk");

        /// <summary>Phase 5A release build to Builds/BumpXR-v0.1.1.apk (versionCode 2, Development Build OFF). Does not touch signing.</summary>
        public static void BuildReleaseApkV011()
        {
            Phase5ASetup();
            BuildApkInternal("Builds/BumpXR-v0.1.1.apk");
        }

        private static void BuildApkInternal(string outputPath)
        {
            EditorUserBuildSettings.development = false;
            System.IO.Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[BumpXR] APK build ({outputPath}) result: {report.summary.result}, size {report.summary.totalSize} bytes, errors {report.summary.totalErrors}");
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


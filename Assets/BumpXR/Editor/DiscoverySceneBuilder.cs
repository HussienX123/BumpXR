#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using BumpXR.UI;

namespace BumpXR.EditorTools
{
    /// <summary>
    /// One-shot generator for Assets/BumpXR/Scenes/DiscoveryTest.unity and the Colocation project-config flag.
    /// Run: Unity -batchmode -quit -executeMethod BumpXR.EditorTools.DiscoverySceneBuilder.Build
    /// </summary>
    public static class DiscoverySceneBuilder
    {
        private const string ScenePath = "Assets/BumpXR/Scenes/DiscoveryTest.unity";
        private const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        public static void Build()
        {
            // Colocation Discovery needs this flag; OVRManifestPreprocessor then adds
            // com.oculus.permission.USE_COLOCATION_DISCOVERY_API to the built manifest.
            var config = OVRProjectConfig.CachedProjectConfig;
            config.colocationSessionSupport = OVRProjectConfig.FeatureSupport.Required;
            OVRProjectConfig.CommitProjectConfig(config);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab);
            PrefabUtility.InstantiatePrefab(rigAsset, scene);

            var canvasGo = new GameObject("DiscoveryDebugCanvas", typeof(Canvas), typeof(Image));
            canvasGo.transform.position = new Vector3(0f, 1.5f, 2f);
            canvasGo.transform.localScale = Vector3.one * 0.002f;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(800f, 600f);

            var textGo = new GameObject("Output", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 20f);
            textRect.offsetMax = new Vector2(-20f, -20f);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = "BumpXR Discovery Debug";

            var panel = canvasGo.AddComponent<DiscoveryDebugPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("output").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BumpXR] DiscoveryTest scene created at " + ScenePath);
        }
    }
}
#endif


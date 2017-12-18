/*  The AssetBundle Manager */

// [downloading method selection]
// AssetBundleManager(ABM) internally uses either WWW or UnityWebRequest to download AssetBundles.
// By default, ABM will automatically select either one based on the version of the Unity runtime.
//
// - WWW
//   For Unity5.3 and earlier PLUS Unity5.5.
// - UnityWebRequest
//   For Unity5.4 and later versions EXCEPT Unity5.5.
//   UnityWebRequest class is officialy introduced since Unity5.4, it is intended to replace WWW.
//   The primary advantage of UnityWebRequest is memory efficiency. It does not load entire
//   AssetBundle into the memory while WWW does.
//
// For Unity5.5 we let ABM to use WWW since we observed a download failure case.
// (https://bitbucket.org/Unity-Technologies/assetbundledemo/pull-requests/25)
//
// Or you can force ABM to use either method by setting one of the following symbols in
// [Player Settings]-[Other Settings]-[Scripting Define Symbols] of each platform.
//
// - ABM_USE_WWW    (to use WWW)
// - ABM_USE_UWREQ  (to use UnityWebRequest)

#if !ABM_USE_WWW && !ABM_USE_UWREQ
#if UNITY_5_4_OR_NEWER && !UNITY_5_5
#define ABM_USE_UWREQ
#else
#define ABM_USE_WWW
#endif
#endif

using UnityEngine;
#if ABM_USE_UWREQ
using UnityEngine.Networking;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
/*  The AssetBundle Manager provides a High-Level API for working with AssetBundles. 
    The AssetBundle Manager will take care of loading AssetBundles and their associated 
    Asset Dependencies.
        Initialize()
            Initializes the AssetBundle manifest object.
        LoadAssetAsync()
            Loads a given asset from a given AssetBundle and handles all the dependencies.
        LoadLevelAsync()
            Loads a given scene from a given AssetBundle and handles all the dependencies.
        LoadDependencies()
            Loads all the dependent AssetBundles for a given AssetBundle.
        BaseDownloadingURL
            Sets the base downloading url which is used for automatic downloading dependencies.
        SimulateAssetBundleInEditor
            Sets Simulation Mode in the Editor.
        Variants
            Sets the active variant.
        RemapVariantName()
            Resolves the correct AssetBundle according to the active variant.
*/

namespace AssetBundles
{
    /// <summary>
    /// Loaded assetBundle contains the references count which can be used to
    /// unload dependent assetBundles automatically.
    /// </summary>
    public class LoadedAssetBundle
    {
        public AssetBundle m_AssetBundle;
        public int m_ReferencedCount;

        internal event Action unload;

        internal void OnUnload()
        {
            m_AssetBundle.Unload(false);
            if (unload != null)
                unload();
        }

        public LoadedAssetBundle(AssetBundle assetBundle)
        {
            m_AssetBundle = assetBundle;
            m_ReferencedCount = 1;
        }
    }

    /// <summary>
    /// Class takes care of loading assetBundle and its dependencies
    /// automatically, loading variants automatically.
    /// </summary>
    public class AssetBundleManager : MonoBehaviour
    {
        public enum LogMode { All, JustErrors };
        public enum LogType { Info, Warning, Error };

        static LogMode m_LogMode = LogMode.All;
        static string m_BaseDownloadingURL = "";
        static string[] m_ActiveVariants = { };
        static AssetBundleManifest m_AssetBundleManifest;

#if UNITY_EDITOR
        static int m_SimulateAssetBundleInEditor = -1;
        const string kSimulateAssetBundles = "SimulateAssetBundles";
#endif

        static Dictionary<string, LoadedAssetBundle> m_LoadedAssetBundles = new Dictionary<string, LoadedAssetBundle>();
        static Dictionary<string, string> m_DownloadingErrors = new Dictionary<string, string>();
        static List<string> m_DownloadingBundles = new List<string>();
        static List<AssetBundleLoadOperation> m_InProgressOperations = new List<AssetBundleLoadOperation>();
        static Dictionary<string, string[]> m_Dependencies = new Dictionary<string, string[]>();
        static bool m_IsAssetBundleEncrypted;
        static AssetBundleManager s_AssetsManager;
        static string[] m_AllAssetBundles = new string[0];
        static string[] m_AllVariants = new string[0];

#if !ENABLE_ASYNC_WAIT
        static WaitForEndOfFrame s_EndOfFrame = new WaitForEndOfFrame();
#endif

        public static LogMode logMode
        {
            get { return m_LogMode; }
            set { m_LogMode = value; }
        }

        /// <summary>
        /// The base downloading url which is used to generate the full
        /// downloading url with the assetBundle names.
        /// </summary>
        public static string BaseDownloadingURL
        {
            get { return m_BaseDownloadingURL; }
            set { m_BaseDownloadingURL = value; }
        }

        public delegate string OverrideBaseDownloadingURLDelegate(string bundleName);

        /// <summary>
        /// Implements per-bundle base downloading URL override.
        /// The subscribers must return null values for unknown bundle names;
        /// </summary>
        public static event OverrideBaseDownloadingURLDelegate overrideBaseDownloadingURL;

        /// <summary>
        /// Variants which is used to define the active variants.
        /// </summary>
        public static string[] ActiveVariants
        {
            get { return m_ActiveVariants; }
            set { m_ActiveVariants = value; }
        }

        /// <summary>
        /// AssetBundleManifest object which can be used to load the dependecies
        /// and check suitable assetBundle variants.
        /// </summary>
        public static AssetBundleManifest AssetBundleManifestObject
        {

            set
            {
                m_AssetBundleManifest = value;
                if (m_AssetBundleManifest != null)
                {
                    m_AllAssetBundles = m_AssetBundleManifest.GetAllAssetBundles();
                    m_AllVariants = m_AssetBundleManifest.GetAllAssetBundlesWithVariant();
                }
            }
        }

        public static bool IsAssetBundleEncrypted
        {
            set { m_IsAssetBundleEncrypted = value; }
            get { return m_IsAssetBundleEncrypted; }
        }

        public static bool IsAssetLoadFromResources { get; set; }

        private static void Log(LogType logType, string text)
        {
            if (logType == LogType.Error)
                Debug.LogError("[AssetBundleManager] " + text);
            else if (m_LogMode == LogMode.All && logType == LogType.Warning)
                Debug.LogWarning("[AssetBundleManager] " + text);
            else if (m_LogMode == LogMode.All)
                Debug.Log("[AssetBundleManager] " + text);
        }

        public static AssetBundleManager sInstance
        {
            get
            {
                if (!s_AssetsManager)
                {
                    GameObject go = new GameObject("AssetBundleManager", typeof(AssetBundleManager));
                    s_AssetsManager = go.GetComponent<AssetBundleManager>();
                    go.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(go);
                }
                return s_AssetsManager;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Flag to indicate if we want to simulate assetBundles in Editor without building them actually.
        /// </summary>
        public static bool SimulateAssetBundleInEditor
        {
            get
            {
                if (m_SimulateAssetBundleInEditor == -1)
                    m_SimulateAssetBundleInEditor = EditorPrefs.GetBool(kSimulateAssetBundles, true) ? 1 : 0;

                return m_SimulateAssetBundleInEditor != 0;
            }
            set
            {
                int newValue = value ? 1 : 0;
                if (newValue != m_SimulateAssetBundleInEditor)
                {
                    m_SimulateAssetBundleInEditor = newValue;
                    EditorPrefs.SetBool(kSimulateAssetBundles, value);
                }
            }
        }
#endif

        private static string GetStreamingAssetsPath()
        {
            if (Application.isEditor)
                return "file://" + System.Environment.CurrentDirectory.Replace("\\", "/"); // Use the build output folder directly.
            else if (Application.isMobilePlatform || Application.isConsolePlatform)
                return Application.streamingAssetsPath;
            else // For standalone player.
                return "file://" + Application.streamingAssetsPath;
        }

        /// <summary>
        /// Sets base downloading URL to a directory relative to the streaming assets directory.
        /// Asset bundles are loaded from a local directory.
        /// </summary>
        public static void SetSourceAssetBundleDirectory(string relativePath)
        {
            BaseDownloadingURL = GetStreamingAssetsPath() + relativePath;
        }

        /// <summary>
        /// Sets base downloading URL to a web URL. The directory pointed to by this URL
        /// on the web-server should have the same structure as the AssetBundles directory
        /// in the demo project root.
        /// </summary>
        /// <example>For example, AssetBundles/iOS/xyz-scene must map to
        /// absolutePath/iOS/xyz-scene.
        /// <example>
        public static void SetSourceAssetBundleURL(string absolutePath)
        {
            if (absolutePath.StartsWith("/"))
            {
                absolutePath = "file://" + absolutePath;
            }
            if (!absolutePath.EndsWith("/"))
            {
                absolutePath += "/";
            }

            BaseDownloadingURL = absolutePath + Utility.GetPlatformName() + "/";
        }

        /// <summary>
        /// Sets base downloading URL to a local development server URL.
        /// </summary>
        public static void SetDevelopmentAssetBundleServer()
        {
#if UNITY_EDITOR
            // If we're in Editor simulation mode, we don't have to setup a download URL
            if (SimulateAssetBundleInEditor)
                return;
#endif

            TextAsset urlFile = Resources.Load("AssetBundleServerURL") as TextAsset;
            string url = (urlFile != null) ? urlFile.text.Trim() : null;
            if (url == null || url.Length == 0)
            {
                Log(LogType.Error, "Development Server URL could not be found.");
            }
            else
            {
                AssetBundleManager.SetSourceAssetBundleURL(url);
            }
        }

        /// <summary>
        /// Retrieves an asset bundle that has previously been requested via LoadAssetBundle.
        /// Returns null if the asset bundle or one of its dependencies have not been downloaded yet.
        /// </summary>
        static public LoadedAssetBundle GetLoadedAssetBundle(string assetBundleName, out string error)
        {
            if (m_DownloadingErrors.TryGetValue(assetBundleName, out error))
                return null;

            LoadedAssetBundle bundle = null;
            m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundle);
            if (bundle == null)
                return null;

            // No dependencies are recorded, only the bundle itself is required.
            string[] dependencies = null;
            if (!m_Dependencies.TryGetValue(assetBundleName, out dependencies))
                return bundle;

            // Make sure all dependencies are loaded
            foreach (var dependency in dependencies)
            {
                if (m_DownloadingErrors.TryGetValue(dependency, out error))
                    return null;

                // Wait all the dependent assetBundles being loaded.
                LoadedAssetBundle dependentBundle;
                m_LoadedAssetBundles.TryGetValue(dependency, out dependentBundle);
                if (dependentBundle == null)
                    return null;
            }

            return bundle;
        }

        /// <summary>
        /// Returns true if certain asset bundle has been downloaded without checking
        /// whether the dependencies have been loaded.
        /// </summary>
        static public bool IsAssetBundleDownloaded(string assetBundleName)
        {
            return m_LoadedAssetBundles.ContainsKey(assetBundleName);
        }

        /// <summary>
        /// Initializes asset bundle namager and starts download of manifest asset bundle.
        /// Returns the manifest asset bundle downolad operation object.
        /// </summary>
        static public AssetBundleLoadManifestOperation Initialize()
        {
            return Initialize(Utility.GetPlatformName());
        }

        /// <summary>
        /// Initializes asset bundle namager and starts download of manifest asset bundle.
        /// Returns the manifest asset bundle downolad operation object.
        /// </summary>
        static public AssetBundleLoadManifestOperation Initialize(string manifestAssetBundleName)
        {
#if UNITY_EDITOR
            Log(LogType.Info, "Simulation Mode: " + (SimulateAssetBundleInEditor ? "Enabled" : "Disabled"));
#endif
            if (sInstance == null)
            {
                // Create assetbundle manager gameobject.
            }

#if UNITY_EDITOR
            // If we're in Editor simulation mode, we don't need the manifest assetBundle.
            if (SimulateAssetBundleInEditor)
                return null;
#endif

            if (string.IsNullOrEmpty(m_BaseDownloadingURL))
            {
                SetSourceAssetBundleURL(Application.persistentDataPath);
            }

            if (!IsAssetLoadFromResources)
            {
                LoadAssetBundle(manifestAssetBundleName, true);
            }

#if UNITY_EDITOR
            Log(LogType.Info, "BaseDownloadingURL: " + m_BaseDownloadingURL);
            Log(LogType.Info, "Assets Load from resources: " + (IsAssetLoadFromResources ? "True" : "False"));
            Log(LogType.Info, "AssetBundle encrypted: " + (IsAssetBundleEncrypted ? "True" : "False"));
#endif

            var operation = new AssetBundleLoadManifestOperation(manifestAssetBundleName, "AssetBundleManifest", typeof(AssetBundleManifest));
            m_InProgressOperations.Add(operation);
            return operation;
        }

        // Temporarily work around a il2cpp bug
        static protected void LoadAssetBundle(string assetBundleName)
        {
            LoadAssetBundle(assetBundleName, false);
        }

        // Starts the download of the asset bundle identified by the given name, and asset bundles
        // that this asset bundle depends on.
        static protected void LoadAssetBundle(string assetBundleName, bool isLoadingAssetBundleManifest)
        {
            Log(LogType.Info, "Loading Asset Bundle " + (isLoadingAssetBundleManifest ? "Manifest: " : ": ") + assetBundleName);

#if UNITY_EDITOR
            // If we're in Editor simulation mode, we don't have to really load the assetBundle and its dependencies.
            if (SimulateAssetBundleInEditor)
                return;
#endif

            if (!isLoadingAssetBundleManifest)
            {
                if (m_AssetBundleManifest == null)
                {
                    Log(LogType.Error, "Please initialize AssetBundleManifest by calling AssetBundleManager.Initialize()");
                    return;
                }
            }

            // Check if the assetBundle has already been processed.
            bool isAlreadyProcessed = LoadAssetBundleInternal(assetBundleName, isLoadingAssetBundleManifest);

            // Load dependencies.
            if (!isAlreadyProcessed && !isLoadingAssetBundleManifest)
                LoadDependencies(assetBundleName);
        }

        // Returns base downloading URL for the given asset bundle.
        // This URL may be overridden on per-bundle basis via overrideBaseDownloadingURL event.
        protected static string GetAssetBundleBaseDownloadingURL(string bundleName)
        {
            if (overrideBaseDownloadingURL != null)
            {
                foreach (OverrideBaseDownloadingURLDelegate method in overrideBaseDownloadingURL.GetInvocationList())
                {
                    string res = method(bundleName);
                    if (res != null)
                        return res;
                }
            }

            return m_BaseDownloadingURL;
        }

        // Checks who is responsible for determination of the correct asset bundle variant
        // that should be loaded on this platform. 
        //
        // On most platforms, this is done by the AssetBundleManager itself. However, on
        // certain platforms (iOS at the moment) it's possible that an external asset bundle
        // variant resolution mechanism is used. In these cases, we use base asset bundle 
        // name (without the variant tag) as the bundle identifier. The platform-specific 
        // code is responsible for correctly loading the bundle.
        static protected bool UsesExternalBundleVariantResolutionMechanism(string baseAssetBundleName)
        {
#if ENABLE_IOS_APP_SLICING
            var url = GetAssetBundleBaseDownloadingURL(baseAssetBundleName);
            if (url.ToLower().StartsWith("res://") ||
                url.ToLower().StartsWith("odr://"))
                return true;
#endif
            return false;
        }

        // Remaps the asset bundle name to the best fitting asset bundle variant.
        static protected string RemapVariantName(string assetBundleName)
        {
            string[] bundlesWithVariant = m_AllVariants;

            // Get base bundle name
            string baseName = assetBundleName.Split('.')[0];

            if (UsesExternalBundleVariantResolutionMechanism(baseName))
                return baseName;

            int bestFit = int.MaxValue;
            int bestFitIndex = -1;
            // Loop all the assetBundles with variant to find the best fit variant assetBundle.
            for (int i = 0; i < bundlesWithVariant.Length; i++)
            {
                string[] curSplit = bundlesWithVariant[i].Split('.');
                string curBaseName = curSplit[0];
                string curVariant = curSplit[1];

                if (curBaseName != baseName)
                    continue;

                int found = System.Array.IndexOf(m_ActiveVariants, curVariant);

                // If there is no active variant found. We still want to use the first
                if (found == -1)
                    found = int.MaxValue - 1;

                if (found < bestFit)
                {
                    bestFit = found;
                    bestFitIndex = i;
                }
            }

            if (bestFit == int.MaxValue - 1)
            {
                Log(LogType.Warning, "Ambigious asset bundle variant chosen because there was no matching active variant: " + bundlesWithVariant[bestFitIndex]);
            }

            if (bestFitIndex != -1)
            {
                return bundlesWithVariant[bestFitIndex];
            }
            else
            {
                return assetBundleName;
            }
        }

        // Sets up download operation for the given asset bundle if it's not downloaded already.
        static protected bool LoadAssetBundleInternal(string assetBundleName, bool isLoadingAssetBundleManifest)
        {
            // Already loaded.
            LoadedAssetBundle bundle = null;
            m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundle);
            if (bundle != null)
            {
                bundle.m_ReferencedCount++;
                return true;
            }

            // @TODO: Do we need to consider the referenced count of WWWs?
            // In the demo, we never have duplicate WWWs as we wait LoadAssetAsync()/LoadLevelAsync() to be finished before calling another LoadAssetAsync()/LoadLevelAsync().
            // But in the real case, users can call LoadAssetAsync()/LoadLevelAsync() several times then wait them to be finished which might have duplicate WWWs.
            if (m_DownloadingBundles.Contains(assetBundleName))
                return true;

            string bundleBaseDownloadingURL = GetAssetBundleBaseDownloadingURL(assetBundleName);

            if (bundleBaseDownloadingURL.ToLower().StartsWith("odr://"))
            {
#if ENABLE_IOS_ON_DEMAND_RESOURCES
                Log(LogType.Info, "Requesting bundle " + assetBundleName + " through ODR");
                m_InProgressOperations.Add(new AssetBundleDownloadFromODROperation(assetBundleName));
#else
                new ApplicationException("Can't load bundle " + assetBundleName + " through ODR: this Unity version or build target doesn't support it.");
#endif
            }
            else if (bundleBaseDownloadingURL.ToLower().StartsWith("res://"))
            {
#if ENABLE_IOS_APP_SLICING
                Log(LogType.Info, "Requesting bundle " + assetBundleName + " through asset catalog");
                m_InProgressOperations.Add(new AssetBundleOpenFromAssetCatalogOperation(assetBundleName));
#else
                new ApplicationException("Can't load bundle " + assetBundleName + " through asset catalog: this Unity version or build target doesn't support it.");
#endif
            }
            else
            {
                if (!bundleBaseDownloadingURL.EndsWith("/"))
                {
                    bundleBaseDownloadingURL += "/";
                }

                string url = bundleBaseDownloadingURL + assetBundleName;

                if (IsAssetBundleEncrypted)
                {
#if ABM_USE_UWREQ
                    UnityWebRequest request = UnityWebRequest.Get(url);
                    m_InProgressOperations.Add(new AssetBundleDownloadWebRequestFromEncryptOperation(assetBundleName, request));
#else
                     WWW download = new WWW(url);
                     m_InProgressOperations.Add(new AssetBundleDownloadFromWebOperation(assetBundleName, download));
#endif
                }
                else
                {
#if ABM_USE_UWREQ
                    // If url refers to a file in StreamingAssets, use AssetBundle.LoadFromFileAsync to load.
                    // UnityWebRequest also is able to load from there, but we use the former API because:
                    // - UnityWebRequest under Android OS fails to load StreamingAssets files (at least Unity5.50 or less)
                    // - or UnityWebRequest anyway internally calls AssetBundle.LoadFromFileAsync for StreamingAssets files
                    if (url.StartsWith(Application.streamingAssetsPath))
                    {
                        m_InProgressOperations.Add(new AssetBundleDownloadFileOperation(assetBundleName, url));
                    }
                    else
                    {
                        UnityWebRequest request = null;

                        if (isLoadingAssetBundleManifest || url.ToLower().StartsWith("file://"))
                        {
                            // For manifest assetbundle, always download it as we don't have hash for it.
                            request = UnityWebRequest.GetAssetBundle(url);
                        }
                        else
                        {
                            request = UnityWebRequest.GetAssetBundle(url, m_AssetBundleManifest.GetAssetBundleHash(assetBundleName), 0);
                        }
                        m_InProgressOperations.Add(new AssetBundleDownloadWebRequestOperation(assetBundleName, request));
                    }
#else
                WWW download = null;
                if (isLoadingAssetBundleManifest || url.ToLower().StartsWith("file://")) {
                    // For manifest assetbundle, always download it as we don't have hash for it.
                    download = new WWW(url);
                } else {
                    download = WWW.LoadFromCacheOrDownload(url, m_AssetBundleManifest.GetAssetBundleHash(assetBundleName), 0);
                }
                m_InProgressOperations.Add(new AssetBundleDownloadFromWebOperation(assetBundleName, download));
#endif
                }

            }
            m_DownloadingBundles.Add(assetBundleName);

            return false;
        }

        // Where we get all the dependencies and load them all.
        static protected void LoadDependencies(string assetBundleName)
        {
            if (m_AssetBundleManifest == null)
            {
                Log(LogType.Error, "Please initialize AssetBundleManifest by calling AssetBundleManager.Initialize()");
                return;
            }

            // Get dependecies from the AssetBundleManifest object..
            string[] dependencies = m_AssetBundleManifest.GetAllDependencies(assetBundleName);
            if (dependencies.Length == 0)
                return;

            for (int i = 0; i < dependencies.Length; i++)
                dependencies[i] = RemapVariantName(dependencies[i]);

            // Record and load all dependencies.
            m_Dependencies.Add(assetBundleName, dependencies);
            for (int i = 0; i < dependencies.Length; i++)
                LoadAssetBundleInternal(dependencies[i], false);
        }

        /// <summary>
        /// Unloads assetbundle and its dependencies.
        /// </summary>
        static public void UnloadAssetBundle(string assetBundleName)
        {
#if UNITY_EDITOR
            // If we're in Editor simulation mode, we don't have to load the manifest assetBundle.
            if (SimulateAssetBundleInEditor)
                return;
#endif
            assetBundleName = RemapVariantName(assetBundleName);

            UnloadAssetBundleInternal(assetBundleName);
            UnloadDependencies(assetBundleName);
        }

        static protected void UnloadDependencies(string assetBundleName)
        {
            string[] dependencies = null;
            if (!m_Dependencies.TryGetValue(assetBundleName, out dependencies))
                return;

            // Loop dependencies.
            foreach (var dependency in dependencies)
            {
                UnloadAssetBundleInternal(dependency);
            }

            m_Dependencies.Remove(assetBundleName);
        }

        static protected void UnloadAssetBundleInternal(string assetBundleName)
        {
            string error;
            LoadedAssetBundle bundle = GetLoadedAssetBundle(assetBundleName, out error);
            if (bundle == null)
                return;

            if (--bundle.m_ReferencedCount == 0)
            {
                bundle.OnUnload();
                m_LoadedAssetBundles.Remove(assetBundleName);

                Log(LogType.Info, assetBundleName + " has been unloaded successfully");
            }
        }

        void Update()
        {
            // Update all in progress operations
            for (int i = 0; i < m_InProgressOperations.Count;)
            {
                var operation = m_InProgressOperations[i];
                if (operation.Update())
                {
                    i++;
                }
                else
                {
                    m_InProgressOperations.RemoveAt(i);
                    ProcessFinishedOperation(operation);
                }
            }
        }

        void ProcessFinishedOperation(AssetBundleLoadOperation operation)
        {
            AssetBundleDownloadOperation download = operation as AssetBundleDownloadOperation;
            if (download == null)
                return;

            if (download.error == null)
                m_LoadedAssetBundles.Add(download.assetBundleName, download.assetBundle);
            else
            {
                string msg = string.Format("Failed downloading bundle {0} from {1}: {2}",
                    download.assetBundleName, download.GetSourceURL(), download.error);
                m_DownloadingErrors.Add(download.assetBundleName, msg);
            }

            m_DownloadingBundles.Remove(download.assetBundleName);
        }

#if ENABLE_ASYNC_WAIT

        public static async void LoadInResourceAssetAsyncWait<T>(string resourcePath, System.Action<T> callback) where T : UnityEngine.Object
        {
            AssetBundleLoadAssetOperation operation;

            string assetBundleName = GetInResourceAssetBundleName(resourcePath);
            if (string.IsNullOrEmpty(assetBundleName))
            {
                operation = new ResourceLoadAssetOperationFull(resourcePath);
            }
            else
            {
                assetBundleName = RemapVariantName(assetBundleName);
                LoadAssetBundle(assetBundleName);
                operation = new AssetBundleLoadAssetOperationFull(assetBundleName, Path.GetFileName(resourcePath), typeof(T));
            }

            m_InProgressOperations.Add(operation);

            await operation;

            callback?.Invoke(operation.GetAsset<T>());
        }

        public static async void LoadInResourcePackedAssetAsyncWait<T>(string assetBundleName, string resourcePath, System.Action<T> callback) where T : UnityEngine.Object
        {
            AssetBundleLoadAssetOperation operation;
            if (m_AssetBundleManifest == null || string.IsNullOrEmpty(m_AllAssetBundles.FirstOrDefault(s => s == assetBundleName)))
            {
                operation = new ResourceLoadAssetOperationFull(resourcePath);
            }
            else
            {
                assetBundleName = RemapVariantName(assetBundleName);
                LoadAssetBundle(assetBundleName);
                operation = new AssetBundleLoadAssetOperationFull(assetBundleName, Path.GetFileName(resourcePath), typeof(T));
            }
            m_InProgressOperations.Add(operation);

            await operation;

            callback?.Invoke(operation.GetAsset<T>());
        }

        static public async void LoadLevel(string assetBundleName, string path, bool isAdditive, System.Action<AsyncOperation> callback)
        {
            AssetBundleLoadOperation asyncOperation;

#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                asyncOperation =
                    LoadLevelAsync(assetBundleName, Path.GetFileName(path), isAdditive) as AssetBundleLoadLevelSimulationOperation;
            }
            else
#endif
            {
                asyncOperation =
                    LoadLevelAsync(assetBundleName, Path.GetFileName(path), isAdditive) as AssetBundleLoadLevelOperation;
            }

            if (asyncOperation != null)
            {
                await asyncOperation;
                var getAsyncOperation = asyncOperation as IGetAsyncOperation;
                callback?.Invoke(getAsyncOperation.GetAsyncOperation());
            }
            else
            {
                callback?.Invoke(null);
            }
        }

        public static async void LoadAssetAsync<T>(string assetBundleName, string assetName, Action<T> callback) where T : UnityEngine.Object
        {
            var operation = LoadAssetAsync(assetBundleName, assetName, typeof(T));
            await operation;
            callback?.Invoke(operation.GetAsset<T>());
        }

#else
        static public IEnumerator LoadInResourceAssetAsync<T>(string resourcePath, System.Action<T> callback) where T : UnityEngine.Object
        {
            AssetBundleLoadAssetOperation operation = null;

            string assetBundleName = GetInResourceAssetBundleName(resourcePath);
            if (string.IsNullOrEmpty(assetBundleName))
            {
                operation = new ResourceLoadAssetOperationFull(resourcePath);
            }
            else
            {
                assetBundleName = RemapVariantName(assetBundleName);
                LoadAssetBundle(assetBundleName);
                operation = new AssetBundleLoadAssetOperationFull(assetBundleName, Path.GetFileName(resourcePath), typeof(T));
            }

            m_InProgressOperations.Add(operation);

            yield return sInstance.StartCoroutine(operation);

            if (callback != null)
            {
                callback(operation.GetAsset<T>());
            }
        }

        static public IEnumerator LoadInResourcePackedAsset<T>(string assetBundleName, string resourcePath, System.Action<T> callback) where T : UnityEngine.Object
        {
            AssetBundleLoadAssetOperation operation;
            if (m_AssetBundleManifest == null || string.IsNullOrEmpty(m_AllAssetBundles.FirstOrDefault(s => s == assetBundleName)))
            {
                operation = new ResourceLoadAssetOperationFull(resourcePath);
            }
            else
            {
                assetBundleName = RemapVariantName(assetBundleName);
                LoadAssetBundle(assetBundleName);
                operation = new AssetBundleLoadAssetOperationFull(assetBundleName, Path.GetFileName(resourcePath), typeof(T));
            }
            m_InProgressOperations.Add(operation);

            yield return sInstance.StartCoroutine(operation);

            if (callback != null)
            {
                callback(operation.GetAsset<T>());
            }
        }

        static public IEnumerator LoadLevel(string assetBundleName, string path, bool isAdditive, System.Action<AsyncOperation> callback)
        {
            IGetAsyncOperation asyncOperation;

#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                asyncOperation =
                    LoadLevelAsync(assetBundleName, Path.GetFileName(path), isAdditive) as AssetBundleLoadLevelSimulationOperation;
            }
            else
#endif
            {
                asyncOperation =
                    LoadLevelAsync(assetBundleName, Path.GetFileName(path), isAdditive) as AssetBundleLoadLevelOperation;
            }

            while (asyncOperation.GetAsyncOperation() == null && !asyncOperation.IsDone())
            {
                yield return s_EndOfFrame;
            }

            if (callback != null)
            {
                callback(asyncOperation.GetAsyncOperation());
            }
        }

        
        static public IEnumerator LoadAssetAsync<T>(string assetBundleName, string assetName, Action<T> callback) where T : UnityEngine.Object
        {
            var operation = LoadAssetAsync(assetBundleName, assetName, typeof(T));
            yield return sInstance.StartCoroutine(operation);

            if (callback != null)
            {
                callback(operation.GetAsset<T>());
            }
        }
#endif


        static string GetInResourceAssetBundleName(string resourcePath)
        {
            if (m_AssetBundleManifest == null)
            {
                return null;
            }

            foreach (var assetBundle in m_AllAssetBundles)
            {
                if (assetBundle.EndsWith(resourcePath, StringComparison.OrdinalIgnoreCase) && assetBundle.Contains("resources/"))
                {
                    return assetBundle;
                }
            }

            return null;
        }


        /// <summary>
        /// Starts a load operation for an asset from the given asset bundle.
        /// </summary>
        static public AssetBundleLoadAssetOperation LoadAssetAsync(string assetBundleName, string assetName, System.Type type)
        {
            Log(LogType.Info, "Loading " + assetName + " from " + assetBundleName + " bundle");

            AssetBundleLoadAssetOperation operation = null;
#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleName, assetName);
                if (assetPaths.Length == 0)
                {
                    Log(LogType.Error, "There is no asset with name \"" + assetName + "\" in " + assetBundleName);
                    return null;
                }

                // @TODO: Now we only get the main object from the first asset. Should consider type also.
                UnityEngine.Object target = AssetDatabase.LoadMainAssetAtPath(assetPaths[0]);
                operation = new AssetBundleLoadAssetOperationSimulation(target);
            }
            else
#endif
            {
                assetBundleName = RemapVariantName(assetBundleName);
                LoadAssetBundle(assetBundleName);
                operation = new AssetBundleLoadAssetOperationFull(assetBundleName, assetName, type);

                m_InProgressOperations.Add(operation);
            }

            return operation;
        }

        /// <summary>
        /// Starts a load operation for a level from the given asset bundle.
        /// </summary>
        static public AssetBundleLoadOperation LoadLevelAsync(string assetBundleName, string levelName, bool isAdditive)
        {
            Log(LogType.Info, "Loading " + levelName + " from " + assetBundleName + " bundle");

            AssetBundleLoadOperation operation = null;
#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                operation = new AssetBundleLoadLevelSimulationOperation(assetBundleName, levelName, isAdditive);
            }
            else
#endif
            {
                if (m_AssetBundleManifest == null)
                {
                    operation = new AssetBundleLoadLevelOperation(string.Empty, levelName, isAdditive);
                }
                else
                {
                    if (!string.IsNullOrEmpty(assetBundleName))
                    {
                        assetBundleName = RemapVariantName(assetBundleName);
                        LoadAssetBundle(assetBundleName);
                    }
                    operation = new AssetBundleLoadLevelOperation(assetBundleName, levelName, isAdditive);
                }

                m_InProgressOperations.Add(operation);
            }

            return operation;
        }

        /// <summary>
        /// ��������AssetsĿ¼����Դ����(������ResourcesĿ¼).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path">AssetsĿ¼�����·��</param>
        /// <param name="callBack">��Դ������ɻص�</param>
        /// <param name="inPack">�����Դ�����һ��AssetBundle�ļ�</param>
        /// <returns></returns>
        public static AssetLoadTask<T> CreateAssetLoadTask<T>(string path, System.Action<T> callBack = null, bool inPack = false) where T : UnityEngine.Object
        {
            return new AssetLoadTask<T>(path, callBack, inPack);
        }

        /// <summary>
        /// ��������ResourcesĿ¼����Դ����.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path">ResourceĿ¼�����·��</param>
        /// <param name="callBack">��Դ������ɻص�</param>
        /// <param name="inPack">�����Դ�����һ��AssetBundle�ļ�</param>
        /// <returns></returns>
        public static ResourceLoadTask<T> CreateResourceLoadTask<T>(string path, System.Action<T> callBack = null, bool inPack = false) where T : UnityEngine.Object
        {
            return new ResourceLoadTask<T>(path, callBack, inPack);
        }

        /// <summary>
        /// �������س�������.
        /// </summary>
        /// <param name="path">����·��</param>
        /// <param name="isAdditive">���ӳ���</param>
        /// <param name="inPack">������������һ��AssetBundle�ļ�</param>
        /// <returns></returns>
        public static LevelLoadTask CreateLevelLoadTask(string path, bool isAdditive, bool inPack = false)
        {
            return new LevelLoadTask(path, isAdditive, inPack);
        }

    } // End of AssetBundleManager.
}

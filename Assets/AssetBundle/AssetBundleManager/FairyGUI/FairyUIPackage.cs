using System.Collections;
using AssetBundles;
using FairyGUI;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace AssetBundles
{
    public class FairyUIPackage
    {
#if UNITY_EDITOR
        private float mStartLoadTime;
        private string mDescFilePath;
#endif

        private readonly System.Action OnAddPackageCallback;

        private FairyUIPackage(string descFilePath, System.Action onAddPackageCallback)
        {

#if UNITY_EDITOR
            mStartLoadTime = Time.realtimeSinceStartup;
            mDescFilePath = descFilePath;
#endif

            OnAddPackageCallback = onAddPackageCallback;
            string assetBundleName = AssetBundleManager.GetAssetBundleName("Resources/" + descFilePath);
            if (string.IsNullOrEmpty(assetBundleName) || AssetBundleManager.SimulateAssetBundleInEditor)
            {
                UIPackage.AddPackage(descFilePath);

#if UNITY_EDITOR
                float t = Time.realtimeSinceStartup - mStartLoadTime;
                Debug.Log("[FairyUIPackage] Fairy ui package at " + mDescFilePath + " added. [t=" + t + "]");
#endif

                if (OnAddPackageCallback != null)
                {
                    OnAddPackageCallback();
                }
            }
            else
            {
                AssetBundleManager.sInstance.StartCoroutine(LoadAssetBundleAsync(assetBundleName));
            }
        }

        IEnumerator LoadAssetBundleAsync(string assetBundleName)
        {
            var loadAssetBundleOperation = AssetBundleManager.LoadAssetBundleAsync(assetBundleName);
            if (loadAssetBundleOperation == null)
            {
                yield break;
            }

            yield return AssetBundleManager.sInstance.StartCoroutine(loadAssetBundleOperation);

            var assetBundle = loadAssetBundleOperation.GetAssetBundle();
            if (assetBundle != null)
            {
                UIPackage.AddPackage(assetBundle);
#if UNITY_EDITOR
                float t = Time.realtimeSinceStartup - mStartLoadTime;
                Debug.Log("[FairyUIPackage] Fairy ui package at " + mDescFilePath + " added. [t=" + t + "]");
#endif

                if (OnAddPackageCallback != null)
                {
                    OnAddPackageCallback();
                }
            }
#if UNITY_EDITOR
            else
            {
                Debug.LogWarning("[FairyUIPackage] Fairy ui package at " + mDescFilePath + " could not be added !!!");
            }
#endif
        }

        /// <summary>
        /// Add a UI package from a path relative to Unity Resources path.
        /// </summary>
        /// <param name="descFilePath">Path relative to Unity Resources path.</param>
        /// <param name="onAddPackageCallback">Add a UI package callback.</param>
        public static void AddPackage(string descFilePath, System.Action onAddPackageCallback)
        {
            var _= new FairyUIPackage(descFilePath, onAddPackageCallback);
        }
    }
}



using System.Collections;
using AssetBundles;
using FairyGUI;

namespace AssetBundles
{
    public class FairyUIPackage
    {
        private readonly System.Action OnAddPackageCallback;

        public FairyUIPackage(string descFilePath, System.Action onAddPackageCallback)
        {
            OnAddPackageCallback = onAddPackageCallback;
            string assetBundleName = AssetBundleManager.GetAssetBundleName("Resources/" + descFilePath, true);
            if (string.IsNullOrEmpty(assetBundleName))
            {
                UIPackage.AddPackage(descFilePath);
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
                if (OnAddPackageCallback != null)
                {
                    OnAddPackageCallback();
                }
            }
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


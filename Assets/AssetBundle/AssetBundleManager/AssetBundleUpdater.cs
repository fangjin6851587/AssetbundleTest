using System.IO;
using AssetBundles;
using UnityEngine;

public class AssetBundleUpdater : MonoBehaviour
{
    public bool DownloadToLocal = true;
    public string DownloadUrl;
    public bool IsEncrypt;
    private AssetBundleList mAssetBundleList;

    private bool mCanDownload;
    private bool mNeedClearOldData;
    private AssetBundleUpdateInfo mUpdateInfo;

    public void StartCheckUpdate()
    {
        StartCheckVersion();
    }

    private string GetPlatformAssetBundleLocationPath()
    {
        return Path.Combine(Application.persistentDataPath, Utility.GetPlatformName());
    }

    private string GetPlatformAssetBundleListLocationPath()
    {
        return Path.Combine(GetPlatformAssetBundleLocationPath(), Utility.GetPlatformName());
    }

    private void Initilize()
    {
    }

    private void LoadLocalData()
    {
        LoadUpdateInfo();
        LoadAssetList();
    }

    private void LoadAssetList()
    {
        mAssetBundleList = null;

        if (mNeedClearOldData)
        {
            string assetPath = GetPlatformAssetBundleLocationPath();
            if (Directory.Exists(assetPath))
            {
                Directory.Delete(assetPath, true);
            }
            mNeedClearOldData = false;
        }

        string bundleListLocationPath = GetPlatformAssetBundleListLocationPath();
        if (File.Exists(bundleListLocationPath))
        {
            mAssetBundleList = new AssetBundleList();
            mAssetBundleList.Load(bundleListLocationPath, IsEncrypt);
        }

        if (mAssetBundleList == null)
        {
            var bundleList = Resources.Load("LocalAssetsInfo/" + AssetBundleList.FILE_NAME) as TextAsset;
            if (bundleList != null)
            {
                mAssetBundleList = new AssetBundleList();
                mAssetBundleList.Load(bundleList.bytes, IsEncrypt);
            }
        }
    }

    private void LoadUpdateInfo()
    {
    }

    private void StartCheckVersion()
    {
        Initilize();
    }
}
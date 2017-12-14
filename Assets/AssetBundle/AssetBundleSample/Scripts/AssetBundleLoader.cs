using System;
using System.Collections;
using System.Collections.Generic;
using AssetBundles;
using UnityEngine;

public class AssetBundleLoader : MonoBehaviour
{

    public AssetBundleUpdater Updater;
    private bool mIsError;

    void Start()
    {
        Updater.OnResultListener += OnAssetBundleUpdate;
        Updater.StartCheckUpdate();
    }

    private void OnAssetBundleUpdate(AssetBundleUpdaterResult assetBundleUpdaterResult)
    {
        AssetBundleUpdateCode code = assetBundleUpdaterResult.Code;
        if (code == AssetBundleUpdateCode.AssetBundleInitializeOk)
        {
            new ResourceLoadTask<GameObject>("MyCube", OnAssetLoaded, "AssetBundles");
            //new ResourceLoadTask<GameObject>("AssetBundles/MyCube", OnAssetLoaded);
            new LevelLoadTask("Test", "AssetBundle/AssetBundleSample/Scene", true);
        }
        else if (code == AssetBundleUpdateCode.VersionOk)
        {
            Updater.StartDownload();
        }
        mIsError = assetBundleUpdaterResult.IsError;
    }

    private void OnAssetLoaded(GameObject gameObject)
    {
        if (gameObject != null)
        {
            Instantiate(gameObject);
        }
    }

    private void OnGUI()
    {
        if (mIsError)
        {
            if (GUI.Button(new Rect(50, 50, 200, 80),  "Retry"))
            {
                Updater.RetryFromError();
            }
        }
    }
}

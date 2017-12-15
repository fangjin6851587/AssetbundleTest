using System;
using System.Collections;
using System.Collections.Generic;
using AssetBundles;
using UnityEngine;

public class AssetBundleLoader : MonoBehaviour
{

    public AssetBundleUpdater Updater;
    private bool mIsError;
    private bool mInited;
    private GameObject mGameObject;

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
            mInited = true;
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
            mGameObject = Instantiate(gameObject);
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

        if (mInited)
        {
            if (GUI.Button(new Rect(50, 50, 200, 80), "Load Asset"))
            {
                DestroyGameObject();
                AssetBundleManager.CreateAssetLoadTask<GameObject>("AssetBundle/AssetBundleSample/AssetBundle/MyCube", OnAssetLoaded);
            }

            if (GUI.Button(new Rect(50, 135, 200, 80), "Load Resource"))
            {
                DestroyGameObject();
                AssetBundleManager.CreateResourceLoadTask<GameObject>("AssetBundles/MyCube", OnAssetLoaded);
            }
            if (GUI.Button(new Rect(50, 220, 200, 80), "Load Resource From Package"))
            {
                DestroyGameObject();
                AssetBundleManager.CreateResourceLoadTask<GameObject>("AssetBundles/MyCube", OnAssetLoaded, true);

            }
            if (GUI.Button(new Rect(50, 305, 200, 80), "Load Level"))
            {
                DestroyGameObject();
                AssetBundleManager.CreateLevelLoadTask("AssetBundle/AssetBundleSample/Scene/Test", true, true);
            }
        }
    }

    void DestroyGameObject()
    {
        if (mGameObject != null)
        {
            Destroy(mGameObject);
        }
    }
}

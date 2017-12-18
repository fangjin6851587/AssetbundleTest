using System;
using System.Collections;
using System.Collections.Generic;
using AssetBundles;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AssetBundleLoader : MonoBehaviour
{

    public AssetBundleUpdater Updater;
    private bool mIsError;
    private bool mInited;
    private GameObject mGameObject;
    private uint mTotalSize;
    private uint mCurrentSize;
    private bool mIsDownloading;

    void Start()
    {
        Updater.OnResultListener += OnAssetBundleUpdate;
        Updater.StartCheckUpdate();
    }

    private void OnAssetBundleUpdate(AssetBundleUpdaterResult assetBundleUpdaterResult)
    {
        mIsDownloading = false;
        AssetBundleUpdateCode code = assetBundleUpdaterResult.Code;
        if (code == AssetBundleUpdateCode.AssetBundleInitializeOk)
        {
            mInited = true;
        }
        else if (code == AssetBundleUpdateCode.VersionOk)
        {
            Updater.StartDownload();
        }
        else if (code == AssetBundleUpdateCode.GetBundleListOk)
        {
            mCurrentSize = 0;
            mTotalSize = assetBundleUpdaterResult.TotalSize;
        }
        else if (code == AssetBundleUpdateCode.UpdateOk)
        {
            mIsDownloading = true;
            mCurrentSize += (uint)assetBundleUpdaterResult.AssetBundle.Size;
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
                AssetBundleManager.CreateResourceLoadTask<GameObject>("AssetBundles/MyCube1", OnAssetLoaded);
            }
            if (GUI.Button(new Rect(50, 220, 200, 80), "Load Level"))
            {
                DestroyGameObject();
                AssetBundleManager.CreateLevelLoadTask("AssetBundle/AssetBundleSample/Scene/Test", true);
            }
        }
        
        if(mIsDownloading)
        {
            float progress = 0;
            if (mTotalSize > 0)
            {
                progress = (float) mCurrentSize / mTotalSize;
            }
            GUILayout.Label("download progress: " + progress.ToString("P1"));
        }
    }

    void DestroyGameObject()
    {
        if (mGameObject != null)
        {
            Destroy(mGameObject);
        }
        if (SceneManager.GetSceneByName("Test").isLoaded)
        {
            SceneManager.UnloadSceneAsync("Test");
        }
    }
}

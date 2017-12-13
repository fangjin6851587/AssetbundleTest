using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using AssetBundles.AssetBundleHttpUtils;
using UnityEngine;

namespace AssetBundles
{
    public enum AssetBundleUpdateCode
    {
        Success = 0,

        VersionOk,
        GetBundleListOk,
        DownloadOk,
        UpdateCompleted,
        AssetBundleInitializeOk,

        VersionFailed,
        GetBundleListFailed,
        NeedDownloadNewApp,
        DownloadFailed
    }

    public class AssetBundleUpdaterResult
    {
        public AssetBundleInfo AssetBundle;

        public AssetBundleUpdateCode Code;

        public string Message = string.Empty;

        public uint TotalSize;
        public override string ToString()
        {
            string assetBundle = string.Empty;
            if (AssetBundle != null)
            {
                assetBundle = AssetBundle.AssetBundleName;
            }

            return "[AssetBundleUpdaterResult] " + string.Format("Code={0} Message={1} TotalSize={2} AssetBundle={3}",
                       Code, Message, TotalSize, assetBundle);
        }
    }

    public class AssetBundleUpdater : MonoBehaviour
    {
        public const int CURRENT_VERSION_MAJOR = 0;
        public bool DownloadToLocal = true;
        public string DownloadUrl;
        public bool IsEncrypt;
        public Action<AssetBundleUpdaterResult> OnResultListener;
        private AssetBundleList mAssetBundleList;
        private AssetBundleUpdateInfo mAssetBundleUpdateInfo;
        private bool mCanDownload;

        private bool mIsNeedUpdate;
        private bool mIsVersionConfirmed;
        private AssetBundleUpdaterResult mLastAssetBundleUpdaterResult;
        private MultiRangeDownloader mMultiRangeDownloader;
        private bool mNeedClearOldData;
        private List<HttpRange> mNeedDownloadHttpRangeList;
        private AssetBundleVersionInfo mTargetVersion;

        public void RetryFromError()
        {
            if (mIsVersionConfirmed)
            {
                StartDownloadAssetBundle();
            }
            else
            {
                StartCheckVersion();
            }
        }

        public void StartCheckUpdate(AssetBundleVersionInfo targetVersion = null)
        {
            mTargetVersion = targetVersion;
            StartCheckVersion();
        }

        public void StartDownload()
        {
            mCanDownload = true;
        }

        private void AfterDownloadComplete()
        {
            mIsVersionConfirmed = false;
            mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
            mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.UpdateCompleted;
            NotificationLastResult();

            AssetBundleManager.BaseDownloadingURL = !DownloadToLocal ? DownloadUrl : string.Empty;
            AssetBundleManager.IsAssetBundleEncrypted = IsEncrypt;
            StartCoroutine(AssetBundleInitialize());
        }

        private IEnumerator AssetBundleInitialize()
        {
            var operate = AssetBundleManager.Initialize();
            yield return StartCoroutine(operate);

            mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
            mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.AssetBundleInitializeOk;
            NotificationLastResult();
        }

        private void CollectUpdateResourceList(byte[] data, string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                mAssetBundleUpdateInfo.TargetVersion = mTargetVersion;

                var assetBundleList = new AssetBundleList();
                assetBundleList.Load(data, IsEncrypt);

                if (DownloadToLocal)
                {
                    GeneratePendingList(ref mAssetBundleUpdateInfo.PendingList, assetBundleList, mAssetBundleList);
                    mIsVersionConfirmed = true;
                    mIsNeedUpdate = mAssetBundleUpdateInfo.PendingList.Count > 0;
                    if (!mIsNeedUpdate)
                    {
                        mAssetBundleUpdateInfo.SyncCurrentVersion();
                    }
                    mCanDownload = false;
                }
                else
                {
                    mAssetBundleUpdateInfo.SyncCurrentVersion();
                    mIsVersionConfirmed = true;
                    mIsNeedUpdate = false;
                    mCanDownload = false;
                }

                mAssetBundleList = assetBundleList;
                SaveLocalData();
                mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
                mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.GetBundleListOk;
                mLastAssetBundleUpdaterResult.TotalSize = mAssetBundleUpdateInfo.GetPendingListTotalSize();
                NotificationLastResult();
            }
            else
            {
                ThrowException(AssetBundleUpdateCode.GetBundleListFailed, error);
            }
        }

        private void DownloadResult(MultiRangeHttpRequestResult result)
        {
            if (result.httpStatusCode != HttpStatusCode.PartialContent)
            {
                ThrowException(AssetBundleUpdateCode.DownloadFailed, result.message);
            }
            else if (result.multiRangeCode == MultiRangeCode.OK)
            {
                if (InitilizeDownload())
                {
                    StartDownloadUnfinished();
                }
            }
            else if (!string.IsNullOrEmpty(result.message) || result.multiRangeCode != MultiRangeCode.OK)
            {
                ThrowException(AssetBundleUpdateCode.DownloadFailed, result.message);
            }
        }

        private void DownloadUnfinishedCallBack(HttpRange range)
        {
            mLastAssetBundleUpdaterResult = null;
            AssetBundleInfo assetBundle;
            if (mAssetBundleUpdateInfo.PendingList.TryGetValue(range.id, out assetBundle))
            {
                if (range.data != null)
                {
                    string fullPath = GetPlatformAssetBundleLocationPath() + "/" + assetBundle.AssetBundleName;
                    string dir = Path.GetDirectoryName(fullPath);
                    if (dir != null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    try
                    {
                        using (var fs = new FileStream(fullPath, FileMode.Create))
                        {
                            fs.Write(range.data, 0, range.data.Length);
                            fs.SetLength(range.data.Length);
                        }
#if UNITY_IPHONE
                UnityEngine.iOS.Device.SetNoBackupFlag(fullPath);
#endif
                    }
                    catch (Exception e)
                    {
                        ThrowException(AssetBundleUpdateCode.DownloadFailed, e.Message);
                        return;
                    }

                    mAssetBundleUpdateInfo.PendingList.Remove(assetBundle.AssetBundleName);

                    mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
                    mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.DownloadOk;
                    mLastAssetBundleUpdaterResult.AssetBundle = assetBundle;
                    NotificationLastResult();
                }
            }
        }

        private void FixedUpdate()
        {
            if (mIsVersionConfirmed)
            {
                if (mIsNeedUpdate)
                {
                    if (mCanDownload)
                    {
                        StartDownloadAssetBundle();
                    }
                }
                else
                {
                    AfterDownloadComplete();
                }
            }
        }

        private void GeneratePendingList(ref Dictionary<string, AssetBundleInfo> pendingList, AssetBundleList latest,
            AssetBundleList current)
        {
            if (pendingList == null)
            {
                pendingList = new Dictionary<string, AssetBundleInfo>();
            }
            pendingList.Clear();

            foreach (var abi0 in latest.BundleList.Values)
            {
                AssetBundleInfo abi1;
                if (current.BundleList.TryGetValue(abi0.AssetBundleName, out abi1))
                {
                    if (!abi0.Hash.Equals(abi1.Hash))
                    {
                        pendingList.Add(abi0.AssetBundleName, abi0);
                    }
                }
                else
                {
                    pendingList.Add(abi0.AssetBundleName, abi0);
                }
            }
        }

        private string GetLocalAssetsInfoPath()
        {
            return Utility.GetLocalAssetsInfo();
        }

        private string GetPlatformAssetBundleListLocationPath()
        {
            return Path.Combine(GetPlatformAssetBundleLocationPath(), AssetBundleList.FILE_NAME);
        }

        private string GetPlatformAssetBundleLocationPath()
        {
            return Path.Combine(Application.persistentDataPath, Utility.GetPlatformName());
        }

        private string GetPlatformAssetBundleUpdateInfoPath()
        {
            return Path.Combine(GetPlatformAssetBundleLocationPath(), AssetBundleUpdateInfo.FILE_NAME);
        }

        private void GetVersionInfo()
        {
            string versionUrl = DownloadUrl + "/" + AssetBundleVersionInfo.FILE_NAME;
            var downloader = new Downloader(versionUrl, (data, error) =>
            {
                if (string.IsNullOrEmpty(error))
                {
                    var versionInfo = new AssetBundleVersionInfo();
                    versionInfo.Load(data, IsEncrypt);

                    if (versionInfo.MarjorVersion == CURRENT_VERSION_MAJOR)
                    {
                        StartUpdate(versionInfo);
                    }
                    else
                    {
                        mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
                        mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.NeedDownloadNewApp;
                        NotificationLastResult();
                    }
                }
                else
                {
                    ThrowException(AssetBundleUpdateCode.VersionFailed, error);
                }
            });
            StartCoroutine(downloader.SendWebRequest());
        }

        private void Initilize()
        {
            LoadLocalData();
            mIsVersionConfirmed = false;
            mCanDownload = false;
            mIsNeedUpdate = false;
        }

        private bool InitilizeDownload()
        {
            mCanDownload = false;
            bool needDownload = true;
            mNeedDownloadHttpRangeList = null;

            if (mAssetBundleUpdateInfo.PendingList.Count > 0)
            {
                mNeedDownloadHttpRangeList = new List<HttpRange>();
                foreach (var assetBundle in mAssetBundleUpdateInfo.PendingList.Values)
                {
                    var httpRange = new HttpRange();
                    httpRange.id = assetBundle.AssetBundleName;
                    httpRange.SetRange(assetBundle.StartOffset, (int) assetBundle.Size);
                    string targetPath = Path.Combine(GetPlatformAssetBundleLocationPath(), assetBundle.AssetBundleName);
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    mNeedDownloadHttpRangeList.Add(httpRange);
                    if (mNeedDownloadHttpRangeList.Count >= MultiRangeHttpRequest.MAX_HTTP_RANGE_COUNT)
                    {
                        break;
                    }
                }
            }
            else
            {
                needDownload = false;
                mIsNeedUpdate = false;
                mAssetBundleUpdateInfo.SyncCurrentVersion();
                SaveLocalData();
            }

            return needDownload;
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
                var bundleList = Resources.Load(GetLocalAssetsInfoPath() + AssetBundleList.FILE_NAME) as TextAsset;
                if (bundleList != null)
                {
                    mAssetBundleList = new AssetBundleList();
                    mAssetBundleList.Load(bundleList.bytes, IsEncrypt);
                }
            }
        }

        private void LoadLocalData()
        {
            LoadUpdateInfo();
            LoadAssetList();
        }

        private void LoadUpdateInfo()
        {
            mNeedClearOldData = false;

            var localVersion = new AssetBundleVersionInfo();
            var asset = Resources.Load(GetLocalAssetsInfoPath() + AssetBundleVersionInfo.FILE_NAME) as TextAsset;
            if (asset != null)
            {
                localVersion.Load(asset.bytes, IsEncrypt);
            }

            string updateInfoPath = GetPlatformAssetBundleUpdateInfoPath();
            if (File.Exists(updateInfoPath))
            {
                mAssetBundleUpdateInfo = new AssetBundleUpdateInfo();
                mAssetBundleUpdateInfo.Load(updateInfoPath, IsEncrypt);

                if (mAssetBundleUpdateInfo != null)
                {
                    if (AssetBundleVersionInfo.Compare(mAssetBundleUpdateInfo.TargetVersion, localVersion) < 0)
                    {
                        mNeedClearOldData = true;
                        mAssetBundleUpdateInfo.TargetVersion = localVersion;
                        mAssetBundleUpdateInfo.CurrentVersion = localVersion;
                    }
                }
            }
            else
            {
                mAssetBundleUpdateInfo = new AssetBundleUpdateInfo();
                mAssetBundleUpdateInfo.CurrentVersion = localVersion;
                mAssetBundleUpdateInfo.TargetVersion = localVersion;
            }
        }

        private void NotificationLastResult()
        {
            if (OnResultListener != null)
            {
                OnResultListener(mLastAssetBundleUpdaterResult);
            }
            Debug.Log(mLastAssetBundleUpdaterResult.ToString());
        }

        private void OnApplicationQuit()
        {
            if (mMultiRangeDownloader != null)
            {
                mMultiRangeDownloader.CloseDownloadData();
            }

            SaveLocalData();
        }

        private void SaveAssetBundleList()
        {
            if (mAssetBundleList != null)
            {
                string path = GetPlatformAssetBundleListLocationPath();
                mAssetBundleList.Save(path, IsEncrypt);
#if UNITY_IPHONE
            UnityEngine.iOS.Device.SetNoBackupFlag(path);
#endif
            }
        }

        private void SaveAssetBundleUpdateInfo()
        {
            if (mAssetBundleUpdateInfo != null)
            {
                string path = GetPlatformAssetBundleUpdateInfoPath();
                mAssetBundleUpdateInfo.Save(path, IsEncrypt);
#if UNITY_IPHONE
            UnityEngine.iOS.Device.SetNoBackupFlag(path);
#endif
            }
        }

        private void SaveLocalData()
        {
            SaveAssetBundleList();
            SaveAssetBundleUpdateInfo();
        }

        private void StartCheckVersion()
        {
            Initilize();

            if (mTargetVersion == null ||
                AssetBundleVersionInfo.Compare(mTargetVersion, mAssetBundleUpdateInfo.CurrentVersion) > 0)
            {
                GetVersionInfo();
            }
            else
            {
                mIsVersionConfirmed = true;
                mIsNeedUpdate = false;
                mCanDownload = false;

                mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
                mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.VersionOk;
                NotificationLastResult();
            }
        }

        private void StartDownloadAssetBundle()
        {
            if (InitilizeDownload())
            {
                StartDownloadUnfinished();
            }
        }

        private void StartDownloadUnfinished()
        {
            if (mNeedDownloadHttpRangeList != null && mNeedDownloadHttpRangeList.Count > 0)
            {
                if (mMultiRangeDownloader == null)
                {
                    mMultiRangeDownloader = new MultiRangeDownloader();
                }

                StartCoroutine(mMultiRangeDownloader.DownloadData(DownloadUrl + Utility.GetPackPlatfomrName(),
                    mNeedDownloadHttpRangeList, DownloadUnfinishedCallBack, DownloadResult));
            }
        }

        private void StartUpdate(AssetBundleVersionInfo version)
        {
            if (AssetBundleVersionInfo.Compare(mAssetBundleUpdateInfo.TargetVersion, version) < 0)
            {
                mTargetVersion = version;
                var downloader = new Downloader(DownloadUrl + AssetBundleList.FILE_NAME, CollectUpdateResourceList);
                StartCoroutine(downloader.SendWebRequest());
            }
            else
            {
                mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
                mLastAssetBundleUpdaterResult.Code = AssetBundleUpdateCode.VersionOk;

                if (AssetBundleVersionInfo.Compare(mAssetBundleUpdateInfo.TargetVersion,
                        mAssetBundleUpdateInfo.CurrentVersion) == 0)
                {
                    mIsVersionConfirmed = true;
                    mIsNeedUpdate = false;
                    mCanDownload = false;
                }
                else
                {
                    mIsVersionConfirmed = true;
                    mIsNeedUpdate = true;
                    mCanDownload = false;

                    mLastAssetBundleUpdaterResult.TotalSize = mAssetBundleUpdateInfo.GetPendingListTotalSize();
                }

                NotificationLastResult();
            }
        }

        private void ThrowException(AssetBundleUpdateCode code, string error)
        {
            SaveLocalData();
            mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult();
            mLastAssetBundleUpdaterResult.Code = code;
            mLastAssetBundleUpdaterResult.Message = error;
            NotificationLastResult();
        }
    }
}
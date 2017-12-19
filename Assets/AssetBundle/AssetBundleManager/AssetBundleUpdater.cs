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
        VersionOk = 1,
        BundleListOk,
        Updating,
        UpdateCompleted,
        AssetBundleInitializeOk,

        VersionFailed = 100,
        GetBundleListFailed,
        UpdateFailed,

        NeedDownloadNewApp = 200
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

        public bool IsError
        {
            get
            {
                return Code == AssetBundleUpdateCode.GetBundleListFailed ||
                       Code == AssetBundleUpdateCode.UpdateFailed || Code == AssetBundleUpdateCode.VersionFailed;
            }
        }
    }

    public class AssetBundleUpdater : MonoBehaviour
    {
        public const int CURRENT_VERSION_MAJOR = 0;
        public bool DownloadToLocal = true;
        public string DownloadUrl;
        public bool IsEncrypt;
        public bool AsssetBundleOnePackage = true;
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
            mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.UpdateCompleted};
            NotificationLastResult();
#if ENABLE_ASYNC_WAIT && NET_4_6
            AssetBundleInitialize();
#else
            StartCoroutine(AssetBundleInitialize());
#endif
        }

#if ENABLE_ASYNC_WAIT && NET_4_6
        private async void AssetBundleInitialize()
        {
            AssetBundleManager.BaseDownloadingURL = !DownloadToLocal ? DownloadUrl : string.Empty;
            AssetBundleManager.IsAssetBundleEncrypted = IsEncrypt;
            AssetBundleManager.ActiveVariants = mAssetBundleList.AllAssetBundlesWithVariant;
            AssetBundleManager.IsAssetLoadFromResources =
                !File.Exists(GetPlatformAssetBundleLocationPath() + "/" + Utility.GetPlatformName());

            var operate = AssetBundleManager.Initialize();
            if (operate != null)
            {
                await operate;
            }
            mLastAssetBundleUpdaterResult =
                new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.AssetBundleInitializeOk};
            NotificationLastResult();
        }
#else
        private IEnumerator AssetBundleInitialize()
        {
            AssetBundleManager.BaseDownloadingURL = !DownloadToLocal ? DownloadUrl : string.Empty;
            AssetBundleManager.IsAssetBundleEncrypted = IsEncrypt;
            AssetBundleManager.ActiveVariants = mAssetBundleList.AllAssetBundlesWithVariant;
            AssetBundleManager.IsAssetLoadFromResources =
                !File.Exists(GetPlatformAssetBundleLocationPath() + "/" + Utility.GetPlatformName());

            var operate = AssetBundleManager.Initialize();
            if (operate != null)
            {
                yield return StartCoroutine(operate);
            }

            mLastAssetBundleUpdaterResult =
                new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.AssetBundleInitializeOk};
            NotificationLastResult();
        }
#endif


        private void CollectUpdateResourceList(byte[] data, string error)
        {
            if (string.IsNullOrEmpty(error))
            {
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
                mAssetBundleUpdateInfo.TargetVersion = mTargetVersion;
                mAssetBundleList = assetBundleList;
                SaveLocalData();
                mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.VersionOk};
                NotificationLastResult();

                mLastAssetBundleUpdaterResult =
                    new AssetBundleUpdaterResult
                    {
                        Code = AssetBundleUpdateCode.BundleListOk,
                        TotalSize = mAssetBundleUpdateInfo.GetPendingListTotalSize()
                    };
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
                ThrowException(AssetBundleUpdateCode.UpdateFailed, result.message);
            }
            else if (result.multiRangeCode == MultiRangeCode.OK)
            {
                if (mLastAssetBundleUpdaterResult != null && mLastAssetBundleUpdaterResult.IsError)
                {
                    return;
                }

                if (InitilizeDownload())
                {
                    StartDownloadUnfinished();
                }
            }
            else if (!string.IsNullOrEmpty(result.message) || result.multiRangeCode != MultiRangeCode.OK)
            {
                ThrowException(AssetBundleUpdateCode.UpdateFailed, result.message);
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

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                    }

                    if (dir != null && File.Exists(dir))
                    {
                        File.Delete(dir);
                    }

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
                        ThrowException(AssetBundleUpdateCode.UpdateFailed, e.Message);
                        return;
                    }

                    mAssetBundleUpdateInfo.PendingList.Remove(assetBundle.AssetBundleName);
                    mLastAssetBundleUpdaterResult =
                        new AssetBundleUpdaterResult
                        {
                            Code = AssetBundleUpdateCode.Updating,
                            AssetBundle = assetBundle
                        };
                    NotificationLastResult();
                }
            }
        }

        private void FixedUpdate()
        {
            if (!mIsVersionConfirmed)
            {
                return;
            }

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
                        mLastAssetBundleUpdaterResult =
                            new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.NeedDownloadNewApp};
                        NotificationLastResult();
                    }
                }
                else
                {
                    ThrowException(AssetBundleUpdateCode.VersionFailed, error);
                }
            });


#if ENABLE_ASYNC_WAIT && NET_4_6
            downloader.AsyncSendWebRequest();
#else
            StartCoroutine(downloader.SendWebRequest());
#endif
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
            mLastAssetBundleUpdaterResult = null;

            if (mAssetBundleUpdateInfo.PendingList.Count > 0)
            {
                mNeedDownloadHttpRangeList = new List<HttpRange>();
                foreach (var assetBundle in mAssetBundleUpdateInfo.PendingList.Values)
                {
                    var httpRange = new HttpRange {id = assetBundle.AssetBundleName};
                    httpRange.SetRange(AsssetBundleOnePackage ? assetBundle.StartOffset : 0, (int) assetBundle.Size);
                    string targetPath = Path.Combine(GetPlatformAssetBundleLocationPath(), assetBundle.AssetBundleName);
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    mNeedDownloadHttpRangeList.Add(httpRange);
                    if (mNeedDownloadHttpRangeList.Count >= MultiRangeHttpRequest.MAX_HTTP_RANGE_COUNT || !AsssetBundleOnePackage)
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
                mAssetBundleList.Load(GetPlatformAssetBundleLocationPath(), IsEncrypt);
            }

            if (mAssetBundleList == null)
            {
                var bundleList = Resources.Load(GetLocalAssetsInfoPath() + Path.GetFileNameWithoutExtension(AssetBundleList.FILE_NAME)) as TextAsset;
                if (bundleList != null)
                {
                    mAssetBundleList = new AssetBundleList();
                    mAssetBundleList.Load(bundleList.bytes, IsEncrypt);
                }
                else
                {
                    mAssetBundleList = new AssetBundleList();
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
            var asset = Resources.Load(GetLocalAssetsInfoPath() + Path.GetFileNameWithoutExtension(AssetBundleVersionInfo.FILE_NAME)) as TextAsset;
            if (asset != null)
            {
                localVersion.Load(asset.bytes, IsEncrypt);
            }

            string updateInfoPath = GetPlatformAssetBundleUpdateInfoPath();
            if (File.Exists(updateInfoPath))
            {
                mAssetBundleUpdateInfo = new AssetBundleUpdateInfo();
                mAssetBundleUpdateInfo.Load(GetPlatformAssetBundleLocationPath(), IsEncrypt);

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
                mAssetBundleUpdateInfo = new AssetBundleUpdateInfo
                {
                    CurrentVersion = localVersion,
                    TargetVersion = localVersion
                };
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
                mAssetBundleList.Save(GetPlatformAssetBundleLocationPath(), IsEncrypt);
#if UNITY_IPHONE
            UnityEngine.iOS.Device.SetNoBackupFlag(path);
#endif
            }
        }

        private void SaveAssetBundleUpdateInfo()
        {
            if (mAssetBundleUpdateInfo != null)
            {
                mAssetBundleUpdateInfo.Save(GetPlatformAssetBundleLocationPath(), IsEncrypt);
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

                mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.VersionOk};
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

                if (AsssetBundleOnePackage)
                {
                    StartCoroutine(mMultiRangeDownloader.DownloadData(DownloadUrl + "/" + Utility.GetPackPlatfomrName(),
                        mNeedDownloadHttpRangeList, DownloadUnfinishedCallBack, DownloadResult));
                }
                else
                {
                    StartCoroutine(mMultiRangeDownloader.DownloadData(DownloadUrl + "/" + mNeedDownloadHttpRangeList[0].id,
                        mNeedDownloadHttpRangeList, DownloadUnfinishedCallBack, DownloadResult));
                }
            }
        }

        private void StartUpdate(AssetBundleVersionInfo version)
        {
            if (AssetBundleVersionInfo.Compare(mAssetBundleUpdateInfo.TargetVersion, version) < 0)
            {
                mTargetVersion = version;
                var downloader = new Downloader(DownloadUrl + "/" + AssetBundleList.FILE_NAME, CollectUpdateResourceList);

#if ENABLE_ASYNC_WAIT && NET_4_6
                downloader.AsyncSendWebRequest();
#else
                StartCoroutine(downloader.SendWebRequest());
#endif
            }
            else
            {
                mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult {Code = AssetBundleUpdateCode.VersionOk};

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
            mLastAssetBundleUpdaterResult = new AssetBundleUpdaterResult
            {
                Code = code,
                Message = error
            };
            NotificationLastResult();
        }
    }
}
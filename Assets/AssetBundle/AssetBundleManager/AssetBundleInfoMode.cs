using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace AssetBundles
{
    [Serializable]
    public abstract class ProtoData
    {
        public abstract string GetFileName();

        public virtual void Load(string path, bool isEncrypt)
        {
            path = Path.Combine(path, GetFileName());
            if (!File.Exists(path))
            {
                return;
            }

            using (var fs = new FileStream(path, FileMode.Open))
            {
                var buffer = new byte[fs.Length];
                fs.Read(buffer, 0, buffer.Length);
                Load(buffer, isEncrypt);
            }
        }

        public virtual void Load(byte[] buffer, bool isEncrypt)
        {
            try
            {
                if (isEncrypt)
                {
                    buffer = Crypto.AesDecryptBytes(buffer);
                }
                using (var m = new MemoryStream(buffer))
                {
                    var bf = new BinaryFormatter();
                    SetData(bf.Deserialize(m));
                }
            }
            catch
            {
                throw;
            }
        }

        protected abstract void SetData(object obj);

        public void Save(string path, bool isEncrypt)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            path = Path.Combine(path, GetFileName());
            using (var fs = new FileStream(path, FileMode.Create))
            {
                var bf = new BinaryFormatter();
                if (isEncrypt)
                {
                    using (var m = new MemoryStream())
                    {
                        bf.Serialize(m, this);
                        var buffer = Crypto.AesEncryptBytes(m.GetBuffer());
                        fs.Write(buffer, 0, buffer.Length);
                        fs.SetLength(buffer.Length);
                    }
                }
                else
                {
                    bf.Serialize(fs, this);
                }
            }
        }
    }

    [Serializable]
    public class AssetBundleInfo
    {
        public string AssetBundleName;
        public string[] Dependencies;
        public string Hash;
        public long Size;
        public int StartOffset;

        public AssetBundleInfo()
        {
            Dependencies = new string[0];
        }

        public AssetBundleInfo(string assetBundleName, string outPutPath, AssetBundleManifest manifest)
        {
            AssetBundleName = assetBundleName;
            Hash = manifest.GetAssetBundleHash(assetBundleName).ToString();
            Dependencies = manifest.GetAllDependencies(assetBundleName);
            var file = new FileInfo(Path.Combine(outPutPath, assetBundleName));
            if (file.Exists)
            {
                Size = file.Length;
            }
        }
    }

    [Serializable]
    public class AssetBundleList : ProtoData
    {
        public const string FILE_NAME = "bundleList.bytes";
        public Dictionary<string, AssetBundleInfo> BundleList;
        public string[] AllAssetBundlesWithVariant;

        public override string GetFileName()
        {
            return FILE_NAME;
        }

        protected override void SetData(object obj)
        {
            AssetBundleList bundleList = obj as AssetBundleList;
            if (bundleList != null)
            {
                BundleList = bundleList.BundleList;
                AllAssetBundlesWithVariant = bundleList.AllAssetBundlesWithVariant;
            }
        }

        public AssetBundleList(string outPutPath, AssetBundleManifest manifest)
        {
            AllAssetBundlesWithVariant = manifest.GetAllAssetBundlesWithVariant();
            BundleList = new Dictionary<string, AssetBundleInfo>();
            BundleList.Add(Utility.GetPlatformName(), GetManifestAssetBundleInfo(outPutPath));
            var allAssetBundles = manifest.GetAllAssetBundles();
            foreach (string assetBundle in allAssetBundles)
            {
                BundleList.Add(assetBundle, new AssetBundleInfo(assetBundle, outPutPath, manifest));
            }
        }

        public AssetBundleList()
        {
            BundleList = new Dictionary<string, AssetBundleInfo>();
        }

        private AssetBundleInfo GetManifestAssetBundleInfo(string outPutPath)
        {
            AssetBundleInfo assetBundle;
            using (var fs = new FileStream(Path.Combine(outPutPath, Utility.GetPlatformName()), FileMode.Open))
            {
                assetBundle = new AssetBundleInfo();
                assetBundle.AssetBundleName = Utility.GetPlatformName();
                byte[] data = new byte[fs.Length];
                fs.Read(data, 0, data.Length);
                assetBundle.Hash = Utility.ComputeMd5Hash(data);
                assetBundle.Size = fs.Length;
            }

            return assetBundle;
        }

        public AssetBundleInfo GetAssetBundleInfo(string assetBundleName)
        {
            AssetBundleInfo assetBundleInfo;
            BundleList.TryGetValue(assetBundleName, out assetBundleInfo);
            return assetBundleInfo;
        }

        public AssetBundleInfo GetAssetBundleInfoHash(string hash)
        {
            return BundleList.Values.FirstOrDefault(p => p.Hash == hash);
        }

        public long GetAsetBundleTotalSize()
        {
            return BundleList.Values.Sum(assetBundleInfo => assetBundleInfo.Size);
        }
    }

    [Serializable]
    public class AssetBundleUpdateInfo : ProtoData
    {
        public const string FILE_NAME = "update.bytes";
        public AssetBundleVersionInfo CurrentVersion;
        public AssetBundleVersionInfo TargetVersion;
        public Dictionary<string, AssetBundleInfo> PendingList;

        public AssetBundleUpdateInfo()
        {
            PendingList = new Dictionary<string, AssetBundleInfo>();
            CurrentVersion = new AssetBundleVersionInfo();
            TargetVersion = new AssetBundleVersionInfo();
        }

        public override string GetFileName()
        {
            return FILE_NAME;
        }

        public void SyncCurrentVersion()
        {
            CurrentVersion = TargetVersion;
        }

        protected override void SetData(object obj)
        {
            AssetBundleUpdateInfo updateInfo = obj as AssetBundleUpdateInfo;
            if (updateInfo != null)
            {
                CurrentVersion = updateInfo.CurrentVersion;
                TargetVersion = updateInfo.TargetVersion;
                PendingList = updateInfo.PendingList;
            }
        }

        public uint GetPendingListTotalSize()
        {
            long size = 0;
            if (PendingList != null)
            {
                foreach (var pending in PendingList.Values)
                {
                    size += pending.Size;
                }
            }

            return (uint)size;
        }
    }

    [Serializable]
    public class AssetBundleVersionInfo : ProtoData
    {
        public const string FILE_NAME = "version.bytes";
        public int MarjorVersion;
        public int MinorVersion;

        public override string GetFileName()
        {
            return FILE_NAME;
        }

        protected override void SetData(object obj)
        {
            AssetBundleVersionInfo version = obj as AssetBundleVersionInfo;
            if (version != null)
            {
                MarjorVersion = version.MarjorVersion;
                MinorVersion = version.MinorVersion;
            }
        }

        public static int Compare(AssetBundleVersionInfo v1, AssetBundleVersionInfo v2)
        {
            if (v1.MarjorVersion == v2.MarjorVersion && v1.MinorVersion == v2.MinorVersion)
            {
                return 0;
            }
            else
            {
                if (v1.MarjorVersion < v2.MarjorVersion ||
                    v1.MarjorVersion == v2.MarjorVersion && v1.MinorVersion < v2.MinorVersion)
                {
                    return -1;
                }

                return 1;
            }
        }
    }
}
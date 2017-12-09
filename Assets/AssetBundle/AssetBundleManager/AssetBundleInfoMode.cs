using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace AssetBundles
{
    public interface IProtoData
    {
        void Load(string path);
        void Save(string path);

        string GetName();
    }

    [System.Serializable]
    public class AssetBundleInfo
    {
        public string AssetBundleName;
        public string Hash;
        public AssetBundleInfo[] Dependencies;

        public AssetBundleInfo()
        {
            Dependencies = new AssetBundleInfo[0];
        }

        public AssetBundleInfo(string assetBundleName, AssetBundleManifest manifest)
        {
            AssetBundleName = assetBundleName;
            Hash = manifest.GetAssetBundleHash(assetBundleName).ToString();
            string[] dependencies = manifest.GetAllDependencies(assetBundleName);
            Dependencies = new AssetBundleInfo[dependencies.Length];
            for (int i = 0; i < dependencies.Length; i++)
            {
                Dependencies[i] = new AssetBundleInfo(dependencies[i], manifest);
            }
        }
    }

    [System.Serializable]
    public class AssetBundleUpdateInfo : IProtoData
    {
        public int CurrentVersion;
        public int TargetVersion;
        public Dictionary<string, AssetBundleInfo> PendingList;

        public AssetBundleUpdateInfo()
        {
            PendingList = new Dictionary<string, AssetBundleInfo>();
        }

        public AssetBundleUpdateInfo(int version, AssetBundleManifest manifest)
        {
            CurrentVersion = version;
            TargetVersion = version;
            string[] allAssetBundles = manifest.GetAllAssetBundles();
            PendingList = new Dictionary<string, AssetBundleInfo>();
            foreach (var assetBundle in allAssetBundles)
            {
                PendingList.Add(assetBundle, new AssetBundleInfo(assetBundle, manifest));
            }
        }

        public AssetBundleInfo GetAssetBundleInfo(string assetBundleName)
        {
            AssetBundleInfo assetBundleInfo;
            PendingList.TryGetValue(assetBundleName, out assetBundleInfo);
            return assetBundleInfo;
        }

        public void Load(string path)
        {
            path = Path.Combine(path, GetName());
            if (!File.Exists(path))
            {
                return;
            }

            using (var fs = new FileStream(path, FileMode.Open))
            {
                var bf = new BinaryFormatter();
                var updateInfo = bf.Deserialize(fs) as AssetBundleUpdateInfo;
                if (updateInfo != null)
                {
                    CurrentVersion = updateInfo.CurrentVersion;
                    TargetVersion = updateInfo.TargetVersion;
                    PendingList = updateInfo.PendingList;
                }
            }
        }

        public void Save(string path)
        {
            path = Path.Combine(path, GetName());
            using (var fs = new FileStream(path, FileMode.Create))
            {
                var bf = new BinaryFormatter();
                bf.Serialize(fs, this);
            }
        }

        private static string FILE_NAME = "update.byte";
        public string GetName()
        {
            return FILE_NAME;
        }
    }

    [System.Serializable]
    public class AssetBundleVersionInfo : IProtoData
    {
        public int MarjorVersion;
        public int MinorVersion;

        public void Load(string path)
        {
            path = Path.Combine(path, GetName());
            if (!File.Exists(path))
            {
                return;
            }

            using (var fs = new FileStream(path, FileMode.Open))
            {
                var bf = new BinaryFormatter();
                var bundleVersionInfo = bf.Deserialize(fs) as AssetBundleVersionInfo;
                if (bundleVersionInfo != null)
                {
                    MarjorVersion = bundleVersionInfo.MarjorVersion;
                    MinorVersion = bundleVersionInfo.MinorVersion;
                }
            }
        }

        public void Save(string path)
        {
            path = Path.Combine(path, GetName());
            using (var fs = new FileStream(path, FileMode.Create))
            {
                var bf = new BinaryFormatter();
                bf.Serialize(fs, this);
            }
        }

        private static string FILE_NAME = "version.byte";
        public string GetName()
        {
            return FILE_NAME;
        }
    }
}



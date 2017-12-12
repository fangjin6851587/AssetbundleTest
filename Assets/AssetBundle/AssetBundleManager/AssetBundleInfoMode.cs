using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace AssetBundles
{
    public interface IProtoData
    {
        void Load(string path, bool isEncrypt);
        void Save(string path, bool isEncrypt);
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
    public class AssetBundleUpdateInfo : IProtoData
    {
        public const string FILE_NAME = "update.byte";
        public int CurrentVersion;
        public Dictionary<string, AssetBundleInfo> PendingList;
        public int TargetVersion;
        public string[] AllAssetBundlesWithVariant;

        public AssetBundleUpdateInfo()
        {
            PendingList = new Dictionary<string, AssetBundleInfo>();
        }

        public AssetBundleUpdateInfo(int version, string outPutPath, AssetBundleManifest manifest)
        {
            CurrentVersion = version;
            TargetVersion = version;
            var allAssetBundles = manifest.GetAllAssetBundles();
            AllAssetBundlesWithVariant = manifest.GetAllAssetBundlesWithVariant();
            PendingList = new Dictionary<string, AssetBundleInfo>();
            PendingList.Add(Utility.GetPlatformName(), GetManifestAssetBundleInfo(outPutPath));
            foreach (string assetBundle in allAssetBundles)
            {
                PendingList.Add(assetBundle, new AssetBundleInfo(assetBundle, outPutPath, manifest));
            }
        }

        private AssetBundleInfo GetManifestAssetBundleInfo(string outPutPath)
        {
            AssetBundleInfo assetBundle = null;
            using (var fs = new FileStream(Path.Combine(outPutPath, Utility.GetPlatformName()), FileMode.Open))
            {
                assetBundle = new AssetBundleInfo();
                assetBundle.AssetBundleName = Utility.GetPlatformName();
                byte[] data = new byte[fs.Length];
                fs.Write(data, 0, data.Length);
                assetBundle.Hash = Utility.ComputeMd5Hash(data);
                assetBundle.Size = fs.Length;
            }

            return assetBundle;
        }

        public void Load(string path, bool isEncrypt)
        {
            path = Path.Combine(path, FILE_NAME);
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

        public void Load(byte[] buffer, bool isEncrypt)
        {
            if (isEncrypt)
            {
                buffer = Crypto.AesDecryptBytes(buffer);
            }
            using (var m = new MemoryStream(buffer))
            {
                var bf = new BinaryFormatter();
                var updateInfo = bf.Deserialize(m) as AssetBundleUpdateInfo;
                if (updateInfo == null)
                {
                    return;
                }

                CurrentVersion = updateInfo.CurrentVersion;
                TargetVersion = updateInfo.TargetVersion;
                PendingList = updateInfo.PendingList;
                AllAssetBundlesWithVariant = updateInfo.AllAssetBundlesWithVariant;
            }
        }

        public void Save(string path, bool isEncrypt)
        {
            path = Path.Combine(path, FILE_NAME);
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

        public AssetBundleInfo GetAssetBundleInfo(string assetBundleName)
        {
            AssetBundleInfo assetBundleInfo;
            PendingList.TryGetValue(assetBundleName, out assetBundleInfo);
            return assetBundleInfo;
        }

        public AssetBundleInfo GetAssetBundleInfoHash(string hash)
        {
            return PendingList.Values.FirstOrDefault(p => p.Hash == hash);
        }

        public long GetPendingListSize()
        {
            return PendingList.Values.Sum(assetBundleInfo => assetBundleInfo.Size);
        }
    }

    [Serializable]
    public class AssetBundleVersionInfo : IProtoData
    {
        public const string FILE_NAME = "version.byte";
        public int MarjorVersion;
        public int MinorVersion;

        public void Load(string path, bool isEncrypt)
        {
            path = Path.Combine(path, FILE_NAME);
            if (!File.Exists(path))
            {
                return;
            }

            using (var fs = new FileStream(path, FileMode.Open))
            {
                if (isEncrypt)
                {
                    var buffer = new byte[fs.Length];
                    fs.Read(buffer, 0, buffer.Length);
                    buffer = Crypto.AesDecryptBytes(buffer);
                    using (var m = new MemoryStream(buffer))
                    {
                        var bf = new BinaryFormatter();
                        var bundleVersionInfo = bf.Deserialize(m) as AssetBundleVersionInfo;
                        if (bundleVersionInfo != null)
                        {
                            MarjorVersion = bundleVersionInfo.MarjorVersion;
                            MinorVersion = bundleVersionInfo.MinorVersion;
                        }
                    }
                }
                else
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
        }

        public void Save(string path, bool isEncrypt)
        {
            path = Path.Combine(path, FILE_NAME);
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
}
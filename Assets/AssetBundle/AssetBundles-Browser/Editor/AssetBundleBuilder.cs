using System;
using System.Collections.Generic;
using System.IO;
using AssetBundleBrowser.AssetBundleDataSource;
using AssetBundles;
using UnityEditor;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class AssetNode
    {
        public readonly List<AssetNode> Parents = new List<AssetNode>();
        public int Depth;
        public string Path;
    }

    public class AssetBundleBuilder
    {
        private static readonly string ASSSETS_STRING = "Assets";
        private readonly Dictionary<string, AssetNode> mAllAssetNodes = new Dictionary<string, AssetNode>();
        private readonly List<string> mBuildMap = new List<string>();

        private readonly List<AssetNode> mLeafNodes = new List<AssetNode>();
        private ABBuildInfo mAbBuildInfo;

        private List<BuildFolder> mDependciesFolder;
        private List<BuildFolder> mSingleFolder;

        public void BuildAssetBundle(ABBuildInfo buildInfo)
        {
            mAbBuildInfo = buildInfo;

            mDependciesFolder = mAbBuildInfo.buildFolderList;
            mSingleFolder = mAbBuildInfo.buildFolderList.FindAll(bf => bf.SingleAssetBundle);

            mBuildMap.Clear();
            mLeafNodes.Clear();
            mAllAssetNodes.Clear();

            CollectDependcy();
            BuildResourceBuildMap();
            CollectSingle();
            BuildAssetBundleWithBuildMap();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public void SetAssetBundleNames(ABBuildInfo buildInfo)
        {
            mAbBuildInfo = buildInfo;

            mDependciesFolder = mAbBuildInfo.buildFolderList;
            mSingleFolder = mAbBuildInfo.buildFolderList.FindAll(bf => bf.SingleAssetBundle);

            mBuildMap.Clear();
            mLeafNodes.Clear();
            mAllAssetNodes.Clear();

            CollectDependcy();
            BuildResourceBuildMap();
            CollectSingle();
            SetAssetBundleNames();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void BuildAssetBundleWithBuildMap()
        {
            ClearAssetBundleNames();

            if (mBuildMap.Count == 0)
            {
                return;
            }

            SetAssetBundleNames();

            if (!Directory.Exists(mAbBuildInfo.outputDirectory))
            {
                Directory.CreateDirectory(mAbBuildInfo.outputDirectory);
            }

            Caching.ClearCache();

            var buildManifest = BuildPipeline.BuildAssetBundles(mAbBuildInfo.outputDirectory, mAbBuildInfo.options,
                mAbBuildInfo.buildTarget);

            if (buildManifest == null)
            {
                Debug.Log("Error in build");
                return;
            }

            if (mAbBuildInfo.isEncrypt)
            {
                EncryptAssetBundle(buildManifest);
            }
            var assetBundleList = GenerateAssetBundleList(buildManifest);
            ClearExtensionManifestFile();

            if (mAbBuildInfo.mergeOneFile)
            {
                if (!Directory.Exists(mAbBuildInfo.GetExtraOutPutDirectory()))
                {
                    Directory.CreateDirectory(mAbBuildInfo.GetExtraOutPutDirectory());
                }

                MergeAssetBundle(assetBundleList);
                File.Copy(mAbBuildInfo.outputDirectory + "/" + AssetBundleVersionInfo.FILE_NAME, mAbBuildInfo.GetExtraOutPutDirectory() + "/" + AssetBundleVersionInfo.FILE_NAME, true);
                File.Copy(mAbBuildInfo.outputDirectory + "/" + AssetBundleList.FILE_NAME, mAbBuildInfo.GetExtraOutPutDirectory() + "/" + AssetBundleList.FILE_NAME, true);

                var localAssetsPath = Application.dataPath + "/Resources/" + Utility.GetLocalAssetsInfo();
                if (mAbBuildInfo.copyLocalAssets)
                {
                    if (!Directory.Exists(localAssetsPath))
                    {
                        Directory.CreateDirectory(localAssetsPath);
                    }
                    File.Copy(mAbBuildInfo.outputDirectory + "/" + AssetBundleVersionInfo.FILE_NAME, localAssetsPath + "/" + AssetBundleVersionInfo.FILE_NAME, true);
                    File.Copy(mAbBuildInfo.outputDirectory + "/" + AssetBundleList.FILE_NAME, localAssetsPath + "/" + AssetBundleList.FILE_NAME, true);
                }
                else
                {
                    if (Directory.Exists(localAssetsPath))
                    {
                        Directory.Delete(localAssetsPath, true);
                    }
                }
            }

            foreach (string assetBundleName in buildManifest.GetAllAssetBundles())
            {
                if (mAbBuildInfo.onBuild != null)
                {
                    mAbBuildInfo.onBuild(assetBundleName);
                }
            }
        }

        private void BuildResourceBuildMap()
        {
            int maxDepth = GetMaxDepthOfLeafNodes();
            while (mLeafNodes.Count > 0)
            {
                var curDepthNodesList = new List<AssetNode>();
                for (int i = 0; i < mLeafNodes.Count; i++)
                {
                    if (mLeafNodes[i].Depth == maxDepth)
                    {
                        if (mLeafNodes[i].Parents.Count != 1)
                        {
                            if (!ShouldIgnoreFile(mLeafNodes[i].Path))
                            {
                                mBuildMap.Add(mLeafNodes[i].Path);
                            }
                        }
                        curDepthNodesList.Add(mLeafNodes[i]);
                    }
                }
                for (int i = 0; i < curDepthNodesList.Count; i++)
                {
                    mLeafNodes.Remove(curDepthNodesList[i]);
                    foreach (var node in curDepthNodesList[i].Parents)
                    {
                        if (!mLeafNodes.Contains(node))
                        {
                            mLeafNodes.Add(node);
                        }
                    }
                }

                maxDepth -= 1;
            }
        }

        private bool CheckFileSuffixNeedIgnore(string fileName)
        {
            if (fileName.EndsWith(".meta") || fileName.EndsWith(".DS_Store") || fileName.EndsWith(".cs"))
            {
                return true;
            }

            return false;
        }

        private void ClearAssetBundleNames()
        {
            var assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
            foreach (string name in assetBundleNames)
            {
                AssetDatabase.RemoveAssetBundleName(name, true);
            }
        }

        private void ClearExtensionManifestFile()
        {
            var files = Directory.GetFiles(mAbBuildInfo.outputDirectory, "*.manifest", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                File.Delete(files[i]);
            }
        }

        private void CollectDependcy()
        {
            for (int i = 0; i < mDependciesFolder.Count; i++)
            {
                string path = Application.dataPath + "/" +
                              mDependciesFolder[i].Path.Substring(ASSSETS_STRING.Length + 1);
                if (!Directory.Exists(path))
                {
                    Debug.LogError(string.Format("abResourcePath {0} not exist", mDependciesFolder[i].Path));
                }
                else
                {
                    var dir = new DirectoryInfo(path);
                    var files = dir.GetFiles("*", SearchOption.AllDirectories);
                    for (int j = 0; j < files.Length; j++)
                    {
                        if (CheckFileSuffixNeedIgnore(files[j].Name))
                        {
                            continue;
                        }

                        string fileRelativePath = GetReleativeToAssets(files[j].FullName);
                        AssetNode root;
                        mAllAssetNodes.TryGetValue(fileRelativePath, out root);
                        if (root == null)
                        {
                            root = new AssetNode();
                            root.Path = fileRelativePath;
                            mAllAssetNodes[root.Path] = root;
                            GetDependcyRecursive(fileRelativePath, root);
                        }
                    }
                }
            }
        }

        private void CollectSingle()
        {
            for (int i = 0; i < mSingleFolder.Count; i++)
            {
                string path = Application.dataPath + "/" + mSingleFolder[i].Path.Substring(ASSSETS_STRING.Length + 1);
                if (!Directory.Exists(path))
                {
                    Debug.LogError(string.Format("abResourcePath {0} not exist", mSingleFolder[i].Path));
                }
                else
                {
                    var dir = new DirectoryInfo(path);
                    var files = dir.GetFiles("*", SearchOption.AllDirectories);
                    for (int j = 0; j < files.Length; j++)
                    {
                        if (CheckFileSuffixNeedIgnore(files[j].Name))
                        {
                            continue;
                        }

                        string fileRelativePath = GetReleativeToAssets(files[j].FullName);
                        if (!mBuildMap.Contains(fileRelativePath))
                        {
                            mBuildMap.Add(fileRelativePath);
                        }
                    }
                }
            }
        }

        private void EncryptAssetBundle(AssetBundleManifest manifest)
        {
            foreach (string assetBundle in manifest.GetAllAssetBundles())
            {
                EncryptAssetBundle(Path.Combine(mAbBuildInfo.outputDirectory, assetBundle));
            }

            EncryptAssetBundle(Path.Combine(mAbBuildInfo.outputDirectory, Utility.GetPlatformName()));
        }

        private void EncryptAssetBundle(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                var buffer = new byte[fs.Length];
                fs.Read(buffer, 0, buffer.Length);
                buffer = Crypto.AesEncryptBytes(buffer);
                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(buffer, 0, buffer.Length);
                fs.SetLength(buffer.Length);
            }
        }

        private AssetBundleList GenerateAssetBundleList(AssetBundleManifest manifest)
        {
            var versionInfo =
                new AssetBundleVersionInfo
                {
                    MinorVersion = int.Parse(DateTime.Now.ToString("yyMMddHHmm")),
                    MarjorVersion = AssetBundleUpdater.CURRENT_VERSION_MAJOR
                };
            versionInfo.Save(mAbBuildInfo.outputDirectory, mAbBuildInfo.isEncrypt);
            var assetBundleList =
                new AssetBundleList(mAbBuildInfo.outputDirectory, manifest);
            assetBundleList.Save(mAbBuildInfo.outputDirectory, mAbBuildInfo.isEncrypt);
            return assetBundleList;
        }

        private BuildFolder GetBuildFolder(string path)
        {
            path = path.Replace("\\", "/");
            return mAbBuildInfo.buildFolderList.Find(sf => path.StartsWith(sf.Path));
        }

        private void GetDependcyRecursive(string path, AssetNode parentNode)
        {
            var dependcy = AssetDatabase.GetDependencies(path, false);
            for (int i = 0; i < dependcy.Length; i++)
            {
                AssetNode node;
                mAllAssetNodes.TryGetValue(dependcy[i], out node);
                if (node == null)
                {
                    node = new AssetNode();
                    node.Path = dependcy[i];
                    node.Depth = parentNode.Depth + 1;
                    node.Parents.Add(parentNode);
                    mAllAssetNodes[node.Path] = node;
                    GetDependcyRecursive(dependcy[i], node);
                }
                else
                {
                    if (!node.Parents.Contains(parentNode))
                    {
                        node.Parents.Add(parentNode);
                    }
                    if (node.Depth < parentNode.Depth + 1)
                    {
                        node.Depth = parentNode.Depth + 1;
                        GetDependcyRecursive(dependcy[i], node);
                    }
                }
                //Debug.Log("dependcy path is " +dependcy[i] + " parent is " + parentNode.path);
            }

            if (dependcy.Length == 0)
            {
                if (!mLeafNodes.Contains(parentNode))
                {
                    mLeafNodes.Add(parentNode);
                }
            }
        }

        private int GetMaxDepthOfLeafNodes()
        {
            if (mLeafNodes.Count == 0)
            {
                return 0;
            }

            mLeafNodes.Sort((x, y) => y.Depth - x.Depth);
            return mLeafNodes[0].Depth;
        }

        private string GetReleativeToAssets(string fullName)
        {
            string fileRelativePath = fullName.Substring(Application.dataPath.Length - ASSSETS_STRING.Length);
            fileRelativePath = fileRelativePath.Replace("\\", "/");
            return fileRelativePath;
        }

        private void MergeAssetBundle(AssetBundleList bundleList)
        {
            AssetBundleMerge.Pack(Application.dataPath.Substring(0, Application.dataPath.Length - ASSSETS_STRING.Length) + mAbBuildInfo.outputDirectory, Path.Combine(mAbBuildInfo.GetExtraOutPutDirectory(), Utility.GetPackPlatfomrName()), bundleList);
            bundleList.Save(mAbBuildInfo.outputDirectory, mAbBuildInfo.isEncrypt);
        }

        private void SetAssetBundleNames()
        {
            if (mBuildMap.Count == 0)
            {
                return;
            }

            foreach (string path in mBuildMap)
            {
                var assetImporter = AssetImporter.GetAtPath(path);

                if (assetImporter == null)
                {
                    continue;
                }

                string assetBundleName = path.Substring(ASSSETS_STRING.Length + 1);
                int extensionIndex = assetBundleName.LastIndexOf(".", StringComparison.Ordinal);
                assetBundleName = assetBundleName.Substring(0, extensionIndex);
                var buildFolder = GetBuildFolder(path);

                if (buildFolder != null)
                {
                    if (buildFolder.SingleAssetBundle)
                    {
                        assetImporter.SetAssetBundleNameAndVariant(
                            string.IsNullOrEmpty(buildFolder.AssetBundleName)
                                ? assetBundleName
                                : buildFolder.AssetBundleName, buildFolder.VariantType);
                    }
                    else
                    {
                        assetImporter.SetAssetBundleNameAndVariant(assetBundleName, buildFolder.VariantType);
                    }
                }
                else
                {
                    assetImporter.SetAssetBundleNameAndVariant(assetBundleName, string.Empty);
                }
            }
        }

        private bool ShouldIgnoreFile(string path)
        {
            if (path.EndsWith(".cs"))
            {
                return true;
            }

            return false;
        }
    }
}
using System.Collections.Generic;
using System.IO;
using AssetBundleBrowser.AssetBundleDataSource;
using UnityEditor;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class AssetNode
    {
        public readonly List<AssetNode> Parents = new List<AssetNode>();
        public string Path;
        public int Depth;
    }

    [System.Serializable]
    public class BuildAsset
    {
        public string Path { get; private set; }

        public bool CollectDependency { get; private set; }

        public BuildAsset(string path, bool collectDependency)
        {
            Path = path;
            CollectDependency = collectDependency;
        }
    }

    public class AssetBundleBuilder
    {
        private static string ASSSETS_STRING = "Assets";

        private static readonly List<AssetNode> sLeafNodes = new List<AssetNode>();
        private static readonly Dictionary<string, AssetNode> sAllAssetNodes = new Dictionary<string, AssetNode>();
        private static readonly List<string> sBuildMap = new List<string>();
        public static List<BuildAsset> sBuildAssets = new List<BuildAsset>();

        public static void SetAassetBundleNames(ABBuildInfo buildAssets)
        {
            //sBuildAssets = buildAssets;

            sBuildMap.Clear();
            sLeafNodes.Clear();
            sAllAssetNodes.Clear();

            CollectDependcy();
            BuildResourceBuildMap();
            BuildAssetBundleWithBuildMap();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CollectDependcy()
        {
            for (int i = 0; i < sBuildAssets.Count; i++)
            {
                string path = Application.dataPath + "/" + sBuildAssets[i].Path;
                if (!Directory.Exists(path))
                {
                    Debug.LogError(string.Format("abResourcePath {0} not exist", sBuildAssets[i].Path));
                }
                else
                {
                    DirectoryInfo dir = new DirectoryInfo(path);
                    FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
                    for (int j = 0; j < files.Length; j++)
                    {
                        if (CheckFileSuffixNeedIgnore(files[j].Name))
                            continue;
                        string fileRelativePath = GetReleativeToAssets(files[j].FullName);
                        AssetNode root;
                        sAllAssetNodes.TryGetValue(fileRelativePath, out root);
                        if (root == null)
                        {
                            root = new AssetNode();
                            root.Path = fileRelativePath;
                            sAllAssetNodes[root.Path] = root;
                            GetDependcyRecursive(fileRelativePath, root);
                        }
                    }
                }
            }
            //PrintDependcy();
        }

        private static void GetDependcyRecursive(string path, AssetNode parentNode)
        {
            string[] dependcy = AssetDatabase.GetDependencies(path, false);
            for (int i = 0; i < dependcy.Length; i++)
            {
                AssetNode node;
                sAllAssetNodes.TryGetValue(dependcy[i], out node);
                if (node == null)
                {
                    node = new AssetNode();
                    node.Path = dependcy[i];
                    node.Depth = parentNode.Depth + 1;
                    node.Parents.Add(parentNode);
                    sAllAssetNodes[node.Path] = node;
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
                if (!sLeafNodes.Contains(parentNode))
                {
                    sLeafNodes.Add(parentNode);
                }
            }
        }

        private static void BuildResourceBuildMap()
        {
            int maxDepth = GetMaxDepthOfLeafNodes();
            while (sLeafNodes.Count > 0)
            {
                List<AssetNode> curDepthNodesList = new List<AssetNode>();
                for (int i = 0; i < sLeafNodes.Count; i++)
                {
                    if (sLeafNodes[i].Depth == maxDepth)
                    {
                        //如果叶子节点有多个父节点或者没有父节点,打包该叶子节点
                        if (sLeafNodes[i].Parents.Count != 1)
                        {
                            if (!ShouldIgnoreFile(sLeafNodes[i].Path))
                            {
                                sBuildMap.Add(sLeafNodes[i].Path);
                            }
                        }
                        curDepthNodesList.Add(sLeafNodes[i]);
                    }
                }
                for (int i = 0; i < curDepthNodesList.Count; i++)
                {
                    sLeafNodes.Remove(curDepthNodesList[i]);
                    foreach (AssetNode node in curDepthNodesList[i].Parents)
                    {
                        if (!sLeafNodes.Contains(node))
                        {
                            sLeafNodes.Add(node);
                        }
                    }
                }
                maxDepth -= 1;
            }
        }

        private static bool ShouldIgnoreFile(string path)
        {
            if (path.EndsWith(".cs"))
                return true;
            return false;
        }

        private static int GetMaxDepthOfLeafNodes()
        {
            if (sLeafNodes.Count == 0)
                return 0;
            sLeafNodes.Sort((x, y) => y.Depth - x.Depth);
            return sLeafNodes[0].Depth;
        }

        private static void BuildAssetBundleWithBuildMap()
        {
            AssetBundleBuild[] buildMapArray = new AssetBundleBuild[sBuildMap.Count];
            for (int i = 0; i < sBuildMap.Count; i++)
            {
                buildMapArray[i].assetBundleName = sBuildMap[i].Substring(ASSSETS_STRING.Length + 1);
                buildMapArray[i].assetNames = new[] { sBuildMap[i] };
            }

            //if (!Directory.Exists(AssetBundle_Path))
            //    Directory.CreateDirectory(AssetBundle_Path);
            //BuildAssetBundles(AssetBundle_Path, buildMapArray, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.DeterministicAssetBundle, EditorUserBuildSettings.activeBuildTarget);
        }

        private static string GetReleativeToAssets(string fullName)
        {
            string fileRelativePath = fullName.Substring(Application.dataPath.Length - ASSSETS_STRING.Length);
            fileRelativePath = fileRelativePath.Replace(@"\\", "/");
            return fileRelativePath;
        }

        private static bool CheckFileSuffixNeedIgnore(string fileName)
        {
            if (fileName.EndsWith(".meta") || fileName.EndsWith(".DS_Store") || fileName.EndsWith(".cs"))
                return true;
            return false;
        }
    }
}



using System.Collections.Generic;
using AssetBundles;
using UnityEditor;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class InspectAssetBundleList
    {
        internal InspectAssetBundleList() { }

        private AssetBundleList mBundleList;

        private Rect mPosition;

        [SerializeField]
        private Vector2 mScrollPosition;

        private bool mAllVariant;
        private bool mBundleFolder;

        internal class AssetBundleInfoFoldout
        {
            public bool Foldout;
            public bool DependenciesFoldout;
        }

        private Dictionary<string, AssetBundleInfoFoldout> mAssetBundleInfoFoldouts = new Dictionary<string, AssetBundleInfoFoldout>();

        internal void SetBundleList(AssetBundleList updateInfo)
        {
            mAssetBundleInfoFoldouts.Clear();
            //members
            mBundleList = updateInfo;
            if (updateInfo != null)
            {
                foreach (var keyPairValue in mBundleList.BundleList)
                {
                    mAssetBundleInfoFoldouts.Add(keyPairValue.Key, new AssetBundleInfoFoldout());
                }
            }
        }

        internal void OnGUI(Rect pos)
        {
            mPosition = pos;

            DrawBundleData();
        }

        private void DrawBundleData()
        {
            if (mBundleList != null)
            {
                GUILayout.BeginArea(mPosition);
                mScrollPosition = EditorGUILayout.BeginScrollView(mScrollPosition);
                using (new EditorGUI.DisabledScope(true))
                {
                    mAllVariant = EditorGUILayout.Foldout(mAllVariant, "All Variant");
                    if (mAllVariant)
                    {
                        int indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 1;
                        int size = mBundleList.AllAssetBundlesWithVariant.Length;
                        EditorGUILayout.LabelField("Size", size.ToString());
                        for (int i = 0; i < size; i++)
                        {
                            EditorGUILayout.LabelField((i + 1).ToString(), mBundleList.AllAssetBundlesWithVariant[i]);
                        }
                        EditorGUI.indentLevel = indent;
                    }

                    mBundleFolder = EditorGUILayout.Foldout(mBundleFolder, "Pending List");
                    if (mBundleFolder)
                    {
                        int indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 1;
                        int size = mBundleList.BundleList.Count;
                        EditorGUILayout.LabelField("Size", size.ToString());
                        foreach (var keyPairValue in mBundleList.BundleList)
                        {
                            var foldout = mAssetBundleInfoFoldouts[keyPairValue.Key];
                            foldout.Foldout = EditorGUILayout.Foldout(foldout.Foldout, keyPairValue.Key);
                            if (foldout.Foldout)
                            {
                                EditorGUI.indentLevel = 2;
                                EditorGUILayout.LabelField("AssetBundleName", keyPairValue.Value.AssetBundleName);
                                EditorGUILayout.LabelField("Hash", keyPairValue.Value.Hash);
                                EditorGUILayout.LabelField("Size", keyPairValue.Value.Size.ToString());

                                int dependencySize = 0;
                                if (keyPairValue.Value.Dependencies != null)
                                {
                                    dependencySize = keyPairValue.Value.Dependencies.Length;
                                }

                                if (dependencySize > 0)
                                {
                                    foldout.DependenciesFoldout = EditorGUILayout.Foldout(foldout.DependenciesFoldout, "AssetBundleList");
                                    if (foldout.DependenciesFoldout)
                                    {
                                        EditorGUI.indentLevel = 3;
                                        EditorGUILayout.LabelField("Size", dependencySize.ToString());
                                        int i = 1;
                                        foreach (var dependency in keyPairValue.Value.Dependencies)
                                        {
                                            EditorGUILayout.LabelField(i.ToString(), dependency);
                                            i++;
                                        }
                                    }
                                }
                                
                                EditorGUI.indentLevel = 2;
                            }
                            EditorGUI.indentLevel = 1;
                        }

                        EditorGUI.indentLevel = indent;
                    }
                }
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }
    }
}
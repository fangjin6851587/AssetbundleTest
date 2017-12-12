using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetBundles;
using UnityEngine;

namespace AssetBundleBrowser
{
    public class AssetBundleMerge
    {
        public static void Pack(string path, string outPath, AssetBundleList bundleList)
        {
            int id = 0;
            int totalSize = 0;
            var allFileInfoDic = new Dictionary<int, AssetBundleFileInfo>();

            path = path.Replace("\\", "/");
            foreach (var assetBundle in bundleList.BundleList.Values)
            {
                var fileInfo = new FileInfo(Path.Combine(path, assetBundle.AssetBundleName));
                if (!fileInfo.Exists)
                {
                    continue;
                }

                string filename = fileInfo.FullName.Replace("\\", "/");
                filename = filename.Replace(path + "/", "");
                int filesize = (int) fileInfo.Length;

                Debug.Log(id + " : " + filename + " 文件大小: " + filesize);

                var info = new AssetBundleFileInfo();
                info.Id = id;
                info.Size = filesize;
                info.Path = filename;
                info.PathLength = new UTF8Encoding().GetBytes(filename).Length;
                info.Hash = assetBundle.Hash;

                using (var fs = new FileStream(fileInfo.FullName, FileMode.Open))
                {
                    info.Data = new byte[fs.Length];
                    fs.Read(info.Data, 0, filesize);
                }

                allFileInfoDic.Add(id, info);
                id++;
                totalSize += filesize;
            }

            /**  遍历一个文件夹的所有文件 结束  **/

            Debug.Log("文件数量 : " + id);
            Debug.Log("文件总大小 : " + totalSize);

            /**  UPK中前面是写每个包的ID,StartPos,size,pathLength,path.
            /**  更新文件在UPK中的起始点  **/
            int firstfilestartpos = 0 + 4;
            for (int index = 0; index < allFileInfoDic.Count; index++)
            {
                firstfilestartpos += 4 + 4 + 4 + 4 + allFileInfoDic[index].PathLength + 24;
            }

            for (int index = 0; index < allFileInfoDic.Count; index++)
            {
                int startpos;
                if (index == 0)
                {
                    startpos = firstfilestartpos;
                }
                else
                {
                    startpos = allFileInfoDic[index - 1].StartPos + allFileInfoDic[index - 1].Size; //上一个文件的开始+文件大小;
                }

                allFileInfoDic[index].StartPos = startpos;
            }

            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }

            using (var fileStream = new FileStream(outPath, FileMode.Create))
            {
                /**  文件总数量  **/
                var totaliddata = BitConverter.GetBytes(id);
                fileStream.Write(totaliddata, 0, totaliddata.Length);

                for (int index = 0; index < allFileInfoDic.Count; index++)
                {
                    /** 写入ID **/
                    var iddata = BitConverter.GetBytes(allFileInfoDic[index].Id);
                    fileStream.Write(iddata, 0, iddata.Length);

                    /**  写入StartPos  **/
                    var startposdata = BitConverter.GetBytes(allFileInfoDic[index].StartPos);
                    fileStream.Write(startposdata, 0, startposdata.Length);

                    /**  写入size  **/
                    var sizedata = BitConverter.GetBytes(allFileInfoDic[index].Size);
                    fileStream.Write(sizedata, 0, sizedata.Length);

                    /**  写入pathLength  **/
                    var pathLengthdata = BitConverter.GetBytes(allFileInfoDic[index].PathLength);
                    fileStream.Write(pathLengthdata, 0, pathLengthdata.Length);

                    /**  写入path  **/
                    var mypathdata = new UTF8Encoding().GetBytes(allFileInfoDic[index].Path);
                    fileStream.Write(mypathdata, 0, mypathdata.Length);

                    /**  写入md5  **/
                    var md5Data = new UTF8Encoding().GetBytes(allFileInfoDic[index].Hash);
                    fileStream.Write(md5Data, 0, md5Data.Length);

                    Debug.Log(allFileInfoDic[index].ToString());

                    var abi = bundleList.GetAssetBundleInfoHash(allFileInfoDic[index].Hash);
                    if (abi != null)
                    {
                        abi.StartOffset = allFileInfoDic[index].StartPos;
                    }
                }

                /**  写入文件数据  **/
                foreach (var infopair in allFileInfoDic)
                {
                    var info = infopair.Value;
                    int size = info.Size;
                    int processSize = 0;
                    while (processSize < size)
                    {
                        var tmpdata = size - processSize < 1024 ? new byte[size - processSize] : new byte[1024];
                        fileStream.Write(info.Data, processSize, tmpdata.Length);

                        processSize += tmpdata.Length;
                    }
                }
            }
            Debug.Log("打包结束");
        }

        private class AssetBundleFileInfo
        {
            public byte[] Data = new byte[0];
            public string Hash;
            public int Id;
            public string Path;
            public int PathLength;
            public int Size;
            public int StartPos;

            public override string ToString()
            {
                return "id=" + Id + " startPos=" + StartPos + " size=" + Size + " pathLength=" + PathLength + " path=" +
                       Path + " hash=" + Hash;
            }
        }
    }
}
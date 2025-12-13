using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class AssetBundleValidator : MonoBehaviour
{
    [ContextMenu("Validate AssetBundle Tags")]
    public void ValidateAssetBundleTags()
    {
        string assetsPath = Application.dataPath;
        List<string> metaFiles = new List<string>();
        
        // 递归查找所有.meta文件
        FindMetaFiles(assetsPath, metaFiles);
        
        Debug.Log("Found " + metaFiles.Count + " meta files");
        
        Dictionary<string, int> bundleCounts = new Dictionary<string, int>();
        
        foreach (string metaFile in metaFiles)
        {
            string content = File.ReadAllText(metaFile);
            if (content.Contains("assetBundleName:"))
            {
                // 提取assetBundleName值
                int startIndex = content.IndexOf("assetBundleName:") + "assetBundleName:".Length;
                int endIndex = content.IndexOf("\n", startIndex);
                string bundleName = content.Substring(startIndex, endIndex - startIndex).Trim();
                
                if (!string.IsNullOrEmpty(bundleName))
                {
                    Debug.Log("Asset: " + metaFile.Replace(".meta", "") + " -> Bundle: " + bundleName);
                    
                    if (bundleCounts.ContainsKey(bundleName))
                    {
                        bundleCounts[bundleName]++;
                    }
                    else
                    {
                        bundleCounts[bundleName] = 1;
                    }
                }
            }
        }
        
        Debug.Log("\n=== AssetBundle Summary ===");
        foreach (var kvp in bundleCounts)
        {
            Debug.Log("Bundle: " + kvp.Key + " -> Assets: " + kvp.Value);
        }
    }
    
    private void FindMetaFiles(string directory, List<string> metaFiles)
    {
        try
        {
            // 添加当前目录的.meta文件
            string[] files = Directory.GetFiles(directory, "*.meta");
            metaFiles.AddRange(files);
            
            // 递归处理子目录
            string[] subDirectories = Directory.GetDirectories(directory);
            foreach (string subDir in subDirectories)
            {
                FindMetaFiles(subDir, metaFiles);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error finding meta files: " + e.Message);
        }
    }
}

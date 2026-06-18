using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    internal static class AssetQueryResultBuilder
    {
        public static AssetSummaryRecord BuildSummary(string guid, int index)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var normalizedPath = AssetQueryPathValidator.NormalizeAssetPath(assetPath);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return new AssetSummaryRecord
            {
                index = index,
                guid = guid,
                name = mainAsset != null ? mainAsset.name : Path.GetFileNameWithoutExtension(normalizedPath),
                locator = normalizedPath,
                path = normalizedPath,
                type = mainAsset != null ? mainAsset.GetType().Name : string.Empty,
                extension = AssetQueryPathValidator.GetExtension(normalizedPath),
                isFolder = AssetDatabase.IsValidFolder(assetPath)
            };
        }

        public static JObject BuildDetail(string guid, int index, bool includeDetails)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var normalizedPath = AssetQueryPathValidator.NormalizeAssetPath(assetPath);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var dependencySample = new JArray();
            var subAssetSample = new JArray();
            var dependencyCount = includeDetails ? 0 : (int?)null;
            var subAssetCount = includeDetails ? 0 : (int?)null;
            var dependencySampleTruncated = false;
            var subAssetSampleTruncated = false;
            string importerType = null;
            long? fileSizeBytes = null;

            if (includeDetails)
            {
                var importer = AssetImporter.GetAtPath(assetPath);
                importerType = importer != null ? importer.GetType().Name : null;

                var absolutePath = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    fileSizeBytes = new FileInfo(absolutePath).Length;
                }

                var dependencies = AssetDatabase.GetDependencies(assetPath, false)
                    .Where(path => !string.Equals(path, assetPath, StringComparison.Ordinal))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                dependencyCount = dependencies.Length;
                foreach (var dependency in dependencies.Take(AssetQueryContract.DetailSampleLimit))
                {
                    dependencySample.Add(dependency.Replace('\\', '/'));
                }

                dependencySampleTruncated = dependencies.Length > dependencySample.Count;

                var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)
                    .Where(asset => asset != null)
                    .Select(asset => asset.name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                subAssetCount = subAssets.Length;
                foreach (var subAsset in subAssets.Take(AssetQueryContract.DetailSampleLimit))
                {
                    subAssetSample.Add(subAsset);
                }

                subAssetSampleTruncated = subAssets.Length > subAssetSample.Count;
            }

            return new JObject
            {
                ["index"] = index,
                ["guid"] = guid,
                ["name"] = mainAsset != null ? mainAsset.name : Path.GetFileNameWithoutExtension(normalizedPath),
                ["locator"] = normalizedPath,
                ["path"] = normalizedPath,
                ["assetPath"] = normalizedPath,
                ["kind"] = "asset",
                ["type"] = mainAsset != null ? mainAsset.GetType().Name : string.Empty,
                ["mainObjectType"] = mainAsset != null ? mainAsset.GetType().FullName : string.Empty,
                ["extension"] = AssetQueryPathValidator.GetExtension(normalizedPath),
                ["isFolder"] = AssetDatabase.IsValidFolder(assetPath),
                ["importerType"] = importerType != null ? JToken.FromObject(importerType) : JValue.CreateNull(),
                ["fileSizeBytes"] = fileSizeBytes.HasValue ? JToken.FromObject(fileSizeBytes.Value) : JValue.CreateNull(),
                ["dependencyCount"] = dependencyCount.HasValue ? JToken.FromObject(dependencyCount.Value) : JValue.CreateNull(),
                ["dependencySample"] = dependencySample,
                ["dependencySampleTruncated"] = dependencySampleTruncated,
                ["subAssetCount"] = subAssetCount.HasValue ? JToken.FromObject(subAssetCount.Value) : JValue.CreateNull(),
                ["subAssetSample"] = subAssetSample,
                ["subAssetSampleTruncated"] = subAssetSampleTruncated
            };
        }
    }
}

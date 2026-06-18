using System;
using UnityMcp.Plugin;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class SerializedComponentSampler
    {
        private static readonly PropertyInfo InspectorModeProperty = typeof(SerializedObject).GetProperty("inspectorMode", BindingFlags.Instance | BindingFlags.NonPublic);

        internal sealed class SampleResult
        {
            public int PropertyCount { get; set; }
            public int ReturnedPropertyCount { get; set; }
            public bool Truncated { get; set; }
            public SerializedPropertyRecord[] Properties { get; set; }
        }

        public static SampleResult Sample(Component component, string propertyMode, int propertyLimit, int arrayElementLimit, int stringMaxLength, IUnityMcpCancellation cancellation)
        {
            var properties = new List<SerializedPropertyRecord>();
            var serializedObject = new SerializedObject(component);
            ConfigureInspectorMode(serializedObject, propertyMode);
            var iterator = serializedObject.GetIterator();
            var propertyCount = 0;
            var returnedCount = 0;
            var truncated = false;
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                cancellation?.ThrowIfCancellationRequested();
                enterChildren = true;
                propertyCount++;

                if (ShouldSkipArrayElement(iterator, arrayElementLimit, out var skippedArrayElement))
                {
                    truncated |= skippedArrayElement;
                    continue;
                }

                if (propertyLimit == 0 || returnedCount >= propertyLimit)
                {
                    truncated = true;
                    continue;
                }

                properties.Add(CreateRecord(iterator, arrayElementLimit, stringMaxLength));
                returnedCount++;
            }

            return new SampleResult
            {
                PropertyCount = propertyCount,
                ReturnedPropertyCount = returnedCount,
                Truncated = truncated,
                Properties = properties.ToArray()
            };
        }

        private static void ConfigureInspectorMode(SerializedObject serializedObject, string propertyMode)
        {
            if (serializedObject == null || InspectorModeProperty == null)
            {
                return;
            }

            try
            {
                var enumType = InspectorModeProperty.PropertyType;
                var enumName = string.Equals(propertyMode, AssetQueryContract.SerializedPropertyMode, StringComparison.Ordinal)
                    ? "Normal"
                    : "Debug";
                var enumValue = Enum.Parse(enumType, enumName, false);
                InspectorModeProperty.SetValue(serializedObject, enumValue, null);
            }
            catch
            {
            }
        }

        public static JObject ToJson(SerializedPropertyRecord property)
        {
            return JObject.FromObject(property);
        }

        private static bool ShouldSkipArrayElement(SerializedProperty property, int arrayElementLimit, out bool skippedArrayElement)
        {
            skippedArrayElement = false;
            if (property == null || arrayElementLimit < 0)
            {
                return false;
            }

            var path = property.propertyPath;
            var markerIndex = path.LastIndexOf(".Array.data[", StringComparison.Ordinal);
            if (markerIndex < 0 || !path.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            var indexStart = markerIndex + ".Array.data[".Length;
            var indexLength = path.Length - indexStart - 1;
            if (indexLength <= 0)
            {
                return false;
            }

            if (!int.TryParse(path.Substring(indexStart, indexLength), NumberStyles.None, CultureInfo.InvariantCulture, out var elementIndex))
            {
                return false;
            }

            if (elementIndex < arrayElementLimit)
            {
                return false;
            }

            skippedArrayElement = true;
            return true;
        }

        private static SerializedPropertyRecord CreateRecord(SerializedProperty property, int arrayElementLimit, int stringMaxLength)
        {
            var isUnityObject = property.propertyType == SerializedPropertyType.ObjectReference;
            var unityObjectValue = isUnityObject ? CreateUnityObjectValue(property.objectReferenceValue) : null;
            var isContainer = IsContainer(property);
            return new SerializedPropertyRecord
            {
                path = property.propertyPath,
                propertyType = property.propertyType.ToString(),
                type = property.type,
                isUnityObject = isUnityObject,
                isNull = isUnityObject && (property.objectReferenceValue == null || unityObjectValue == null || unityObjectValue.isDestroyed),
                isContainer = isContainer,
                value = isContainer
                    ? null
                    : isUnityObject
                        ? (object)unityObjectValue
                        : GetPrimitiveValue(property, stringMaxLength)
            };
        }

        private static UnityObjectValueRecord CreateUnityObjectValue(UnityEngine.Object value)
        {
            if (value == null)
            {
                return null;
            }

            var isDestroyed = value.Equals(null);
            if (isDestroyed)
            {
                return new UnityObjectValueRecord
                {
                    name = null,
                    path = null,
                    guid = null,
                    instanceId = null,
                    isDestroyed = true
                };
            }

            var assetPath = AssetDatabase.GetAssetPath(value);
            var guid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
            return new UnityObjectValueRecord
            {
                name = value.name,
                path = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath.Replace('\\', '/'),
                guid = guid,
                instanceId = value.GetInstanceID(),
                isDestroyed = false
            };
        }

        private static bool IsContainer(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            if (property.propertyType == SerializedPropertyType.Generic)
            {
                return true;
            }

            return property.isArray && property.propertyType != SerializedPropertyType.String;
        }

        private static object GetPrimitiveValue(SerializedProperty property, int stringMaxLength)
        {
            if (IsPrimaryLeafPropertyType(property.propertyType))
            {
                return GetPrimaryLeafValue(property, stringMaxLength);
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Color:
                    return CreateColorValue(property.colorValue);
                case SerializedPropertyType.Vector2:
                    return CreateVector2Value(property.vector2Value);
                case SerializedPropertyType.Vector3:
                    return CreateVector3Value(property.vector3Value);
                case SerializedPropertyType.Vector4:
                    return CreateVector4Value(property.vector4Value);
                case SerializedPropertyType.Rect:
                    return CreateRectValue(property.rectValue);
                case SerializedPropertyType.AnimationCurve:
                    return null;
                case SerializedPropertyType.Bounds:
                    return CreateBoundsValue(property.boundsValue);
                case SerializedPropertyType.Gradient:
                    return null;
                case SerializedPropertyType.Quaternion:
                    return CreateQuaternionValue(property.quaternionValue);
                case SerializedPropertyType.ExposedReference:
                    return null;
                case SerializedPropertyType.Vector2Int:
                    return CreateVector2IntValue(property.vector2IntValue);
                case SerializedPropertyType.Vector3Int:
                    return CreateVector3IntValue(property.vector3IntValue);
                case SerializedPropertyType.RectInt:
                    return CreateRectIntValue(property.rectIntValue);
                case SerializedPropertyType.BoundsInt:
                    return CreateBoundsIntValue(property.boundsIntValue);
                case SerializedPropertyType.ManagedReference:
                    return null;
                case SerializedPropertyType.Hash128:
                    return property.hash128Value.ToString();
                default:
                    return property.propertyType == SerializedPropertyType.Generic ? null : TruncateString(property.ToString(), stringMaxLength);
            }
        }

        private static bool IsPrimaryLeafPropertyType(SerializedPropertyType propertyType)
        {
            switch (propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.FixedBufferSize:
                case SerializedPropertyType.Hash128:
                    return true;
                default:
                    return false;
            }
        }

        private static object GetPrimaryLeafValue(SerializedProperty property, int stringMaxLength)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.longValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.doubleValue;
                case SerializedPropertyType.String:
                    return TruncateString(property.stringValue, stringMaxLength);
                case SerializedPropertyType.LayerMask:
                    return property.intValue;
                case SerializedPropertyType.Enum:
                    return property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.ArraySize:
                    return property.intValue;
                case SerializedPropertyType.Character:
                    return property.intValue;
                case SerializedPropertyType.FixedBufferSize:
                    return property.intValue;
                case SerializedPropertyType.Hash128:
                    return property.hash128Value.ToString();
                default:
                    return null;
            }
        }

        private static string TruncateString(string value, int stringMaxLength)
        {
            var stringValue = value ?? string.Empty;
            return stringValue.Length <= stringMaxLength ? stringValue : stringValue.Substring(0, stringMaxLength);
        }

        private static JObject CreateColorValue(Color value)
        {
            return new JObject
            {
                ["r"] = value.r,
                ["g"] = value.g,
                ["b"] = value.b,
                ["a"] = value.a
            };
        }

        private static JObject CreateVector2Value(Vector2 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y
            };
        }

        private static JObject CreateVector3Value(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z
            };
        }

        private static JObject CreateVector4Value(Vector4 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z,
                ["w"] = value.w
            };
        }

        private static JObject CreateRectValue(Rect value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["width"] = value.width,
                ["height"] = value.height
            };
        }

        private static JObject CreateBoundsValue(Bounds value)
        {
            return new JObject
            {
                ["center"] = CreateVector3Value(value.center),
                ["size"] = CreateVector3Value(value.size)
            };
        }

        private static JObject CreateQuaternionValue(Quaternion value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z,
                ["w"] = value.w
            };
        }

        private static JObject CreateVector2IntValue(Vector2Int value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y
            };
        }

        private static JObject CreateVector3IntValue(Vector3Int value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z
            };
        }

        private static JObject CreateRectIntValue(RectInt value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["width"] = value.width,
                ["height"] = value.height
            };
        }

        private static JObject CreateBoundsIntValue(BoundsInt value)
        {
            return new JObject
            {
                ["position"] = CreateVector3IntValue(value.position),
                ["size"] = CreateVector3IntValue(value.size)
            };
        }
    }
}

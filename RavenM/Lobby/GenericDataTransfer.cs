using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using RavenM.Helpers;

namespace RavenM
{
    // Used to make properties not included in a data transfer.
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class DataTransferIgnoredAttribute : Attribute
    {

    }
    // used to make non-public properties included in the transfer.
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class DataTransferIncludedAttribute : Attribute
    {

    }

    public static class GenericDataTransfer
    {
        public static string HandlePrefix(string prefix, string key)
        {
            return string.IsNullOrWhiteSpace(prefix) == false ? $"{prefix}.{key}" : $"{key}";
        }

        const string PrefixSeparator = ".";

        public delegate string ImportDataDelegate(string key);

        public delegate void ExportDataDelegate(string key, string value);

        private static bool ImportPropertyFrom(out object ret, PropertyInfo propertyInfo, ImportDataDelegate importDelegate, string prefix)
        {
            ret = null;
            if (!propertyInfo.CanWrite || propertyInfo.GetCustomAttribute<DataTransferIgnoredAttribute>() != null)
            {
                return false; 
            }

            string dataKey = HandlePrefix(prefix, propertyInfo.Name);

            if (propertyInfo.PropertyType.GetInterface(nameof(TransferableNestedClass)) != null)
            {
                // This is pretty jank. dotnet 4.6 doesn't allow interfaces to have static methods so we have to instantiate this class and then set it again anyway.
                TransferableNestedClass propObj = (TransferableNestedClass)Activator.CreateInstance(propertyInfo.PropertyType);
                ret = propObj.ImportNested(importDelegate, dataKey);
            }
            else
            {
                // Pull data from the import delegate and attempt to populate this property.
                string importedData = importDelegate(dataKey);
                if (importedData == null) 
                {
                    return false; // TODO: Figure out if this should actually be a throw type scenario. It probably is and we should have an attribute for optional/non-throw.
                }

                TypeConverter typeConverter = TypeDescriptor.GetConverter(propertyInfo.PropertyType);
                ret = typeConverter.ConvertFromString(importedData);
            }
            return true;
        }

        private static void ExportPropertyTo(object exportObject, PropertyInfo propertyInfo, ExportDataDelegate exportDelegate, string prefix)
        {
            if (!propertyInfo.CanRead || propertyInfo.GetCustomAttribute<DataTransferIgnoredAttribute>() != null)
                return;

            object dataToExport = propertyInfo.GetValue(exportObject);
            if (dataToExport == null) 
                return; // TODO: Again, figure out if we should throw here or not.
            
            string dataKey = HandlePrefix(prefix, propertyInfo.Name);
            
            if (propertyInfo.PropertyType.GetInterface(nameof(TransferableNestedClass)) != null)
            {
                ((TransferableNestedClass)dataToExport).ExportNested(exportDelegate, dataKey);
            }
            else
            {
                string dataValue = dataToExport.ToString();
                exportDelegate(dataKey, dataValue);
            }
        }

        public static bool ImportFrom<T>(out T ret, ImportDataDelegate importDelegate, string prefix = null) where T : new()
        {
            try
            {
                ret = new T();

                Type type = typeof(T);
                PropertyInfo[] publicProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (PropertyInfo property in publicProperties)
                {
                    if (ImportPropertyFrom(out object propValue, property, importDelegate, prefix))
                    {
                        property.SetValue(ret, propValue);
                    }
                }
                
                PropertyInfo[] privateProperties = type.GetProperties(BindingFlags.NonPublic| BindingFlags.Instance);

                foreach (PropertyInfo property in privateProperties)
                {
                    if (property.GetCustomAttribute<DataTransferIncludedAttribute>() == null)
                        continue;

                    if (ImportPropertyFrom(out object propValue, property, importDelegate, prefix))
                    {
                        property.SetValue(ret, propValue);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                // TODO: This try catch block should probably be moved to a separate no-throw version of this function. In some cases we'd definitely rather not throw but others we probably should.
                Plugin.logger.LogError(e);
                ret = default(T);
                return false;
            }
        }

        public static void ExportTo<T>(T classToExport, ExportDataDelegate exportDelegate, string prefix = null)
        {
            if (classToExport == null) throw new ArgumentNullException(nameof(classToExport));

            Type type = typeof(T);
            PropertyInfo[] publicProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo property in publicProperties)
            {
                ExportPropertyTo(classToExport, property, exportDelegate, prefix);
            }
            
            PropertyInfo[] privateProperties = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance);


            foreach (PropertyInfo property in privateProperties)
            {
                if (property.GetCustomAttribute<DataTransferIncludedAttribute>() == null)
                    continue;

                ExportPropertyTo(classToExport, property, exportDelegate, prefix);
            }
        }
    }


    internal static class GenericDataCache<TKey, TValue> where TKey : IEquatable<TKey> where TValue : IEquatable<TValue>, new()
    {
        struct CacheInfo
        {
            public TValue cachedData;
            public DateTime lastUpdate;
        }

        static Dictionary<TKey, CacheInfo> cache = new Dictionary<TKey, CacheInfo>();


        // TODO: This used to be more complicated, do we even want to keep it now that it's nearly a one liner?
        private static bool TryGetCacheData(TKey cacheKey, out CacheInfo info)
        {
            return cache.TryGetValue(cacheKey, out info);
        }

        private static bool TryGetCacheData(TKey cacheKey, out TValue importedData, float staleSecondsAllowed)
        {
            if (TryGetCacheData(cacheKey, out CacheInfo cachedInfo))
            {
                if ((DateTime.Now - cachedInfo.lastUpdate).TotalSeconds <= staleSecondsAllowed)
                {
                    importedData = cachedInfo.cachedData;
                    return true;
                }
                // No else here, if the data is too stale we can treat it identically as if we don't have cached data at all.
            }

            importedData = default(TValue);
            return false;
        }

        private static void CacheData(TKey key, TValue data)
        {
            CacheInfo info;
            info.cachedData = data;
            info.lastUpdate = DateTime.Now;
            cache[key] = info;
        }

        public static void ClearCache()
        {
            cache.Clear();
        }

        internal static bool ImportFrom(TKey cacheKey, out TValue importedData, GenericDataTransfer.ImportDataDelegate importDelegate, float staleSecondsAllowed = 1.0f)
        {
            bool freshCachedData = TryGetCacheData(cacheKey, out importedData, staleSecondsAllowed);

            if (freshCachedData)
                return true;

            bool ret = GenericDataTransfer.ImportFrom(out importedData, importDelegate);

            if (ret)
            {
                // We got data so let's cache it for later.
                CacheData(cacheKey, importedData);
            }

            return ret;
        }

        internal static void ExportTo(TKey cacheKey, TValue dataToExport, GenericDataTransfer.ExportDataDelegate exportDelegate, float staleSecondsAllowed = 1.0f)
        {
            if (TryGetCacheData(cacheKey, out CacheInfo cachedInfo))
            {
                // TODO: Consider shifting the cached time forward if we only skipped sending because of matching previous or perhaps forcing a send after some multiple of stale is reached.
                if (cachedInfo.cachedData.Equals(dataToExport) || (DateTime.Now - cachedInfo.lastUpdate).TotalSeconds <= staleSecondsAllowed)
                // if ((DateTime.Now - cachedInfo.lastUpdate).TotalSeconds <= staleSecondsAllowed)
                    return; // If the cached data is fresh enough or exactly matches what we last sent, skip sending. 
            }

            GenericDataTransfer.ExportTo(dataToExport, exportDelegate);

            // We got data so let's cache it for later.
            CacheData(cacheKey, dataToExport);
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace RavenM
{
    public class DataTransferIgnoredAttribute : Attribute
    {

    }

    public abstract class GenericEquatable<T> : IEquatable<T>
    {
        public bool Equals(T other)
        {
            if (other == null)
                return false;

            PropertyInfo[] properties = this.GetType().GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead) continue;

                if (!property.GetValue(this).Equals(property.GetValue(other)))
                {
                    return false;
                }
            }

            return true;
        }

        override public string ToString()
        {
            StringBuilder stringBuilder = new StringBuilder();

            PropertyInfo[] properties = this.GetType().GetProperties();

            stringBuilder.Append("\n");

            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead) continue; // TODO: Consider ignoring things with the attribute here and in the equals function above.

                string data = $"    {property.Name} :  {property.GetValue(this).ToString()}\n";

                stringBuilder.Append(data);
            }

            return stringBuilder.ToString();
        }
    }

    internal static class GenericDataTransfer
    {
        public delegate string ImportDataDelegate(string key);

        public delegate void ExportDataDelegate(string key, string value);

        public static bool ImportFrom<T>(out T ret, ImportDataDelegate importDelegate) where T : new()
        {
            try
            {
                ret = new T();

                Type type = typeof(T);
                PropertyInfo[] properties = type.GetProperties();

                foreach (PropertyInfo property in properties)
                {
                    if (!property.CanWrite || property.GetCustomAttribute<DataTransferIgnoredAttribute>() != null) continue;

                    string dataKey = property.Name;

                    // Pull data from the import delegate and attempt to populate this property.

                    string lobbyData = importDelegate(dataKey);
                    if (lobbyData == null) continue; // TODO: Figure out if this should actually be a throw type scenario. It probably is and we should have an attribute for optional/non-throw.

                    TypeConverter typeConverter = TypeDescriptor.GetConverter(property.PropertyType);
                    object propValue = typeConverter.ConvertFromString(lobbyData);
                    property.SetValue(ret, propValue);
                }

                return true;

            }
            catch (Exception e)
            {
                Plugin.logger.LogError(e);
                ret = default(T);
                return false;
            }
        }

        public static void ExportTo<T>(T classToExport, ExportDataDelegate exportDelegate)
        {
            if (classToExport == null) throw new ArgumentNullException(nameof(classToExport));

            Type type = typeof(T);
            PropertyInfo[] properties = type.GetProperties();

            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead || property.GetCustomAttribute<DataTransferIgnoredAttribute>() != null) continue;

                object dataToExport = property.GetValue(classToExport);
                if (dataToExport == null) continue; // TODO: Again, figure out if we should throw here or not.

                string dataKey = property.Name;
                exportDelegate(dataKey, dataToExport.ToString());
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

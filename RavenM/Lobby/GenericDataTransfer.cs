using System;
using System.ComponentModel;
using System.Reflection;

namespace RavenM.Lobby.DataTransfer;


/// <summary>
/// Used to mark properties that shoul not be included in a data transfer.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DataTransferIgnoredAttribute : Attribute
{

}

/// <summary>
/// Used to mark non-public properties that should be included in the transfer.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DataTransferIncludedAttribute : Attribute
{

}

public delegate string ImportDataDelegate(string key);
public delegate void ExportDataDelegate(string key, string value);

/// <summary>
/// A set of functions that iterate over a classes via reflection for generically sending data back and forth.
/// Probably over-engineered for our needs but lets us swap out where we send/get the data from.
/// </summary>
public static class GenericDataTransfer
{
    public static string HandlePrefix(string prefix, string key)
    {
        return string.IsNullOrWhiteSpace(prefix) == false ? $"{prefix}{PrefixSeparator}{key}" : $"{key}";
    }

    const string PrefixSeparator = ".";

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
                return false; // TODO-Throw? Figure out if this should actually be a throw type scenario. It probably is and we should have an attribute for optional/non-throw.
            }

            try
            {
                TypeConverter typeConverter = TypeDescriptor.GetConverter(propertyInfo.PropertyType);
                ret = typeConverter.ConvertFromString(importedData);
            } 
            catch (FormatException ex)
            {
                // TODO-EXCEPT: We should probably convert this into a custom exception type with a better exception message rather than just logging here and rethrowing.
                Plugin.logger.LogError($"Failed to convert imported data to the correct type. Key = \"{dataKey}\" Value = \"{importedData}\"");
                throw ex;
            }
        }
        return true;
    }

    private static void ExportPropertyTo(object exportObject, PropertyInfo propertyInfo, ExportDataDelegate exportDelegate, string prefix)
    {
        if (!propertyInfo.CanRead || propertyInfo.GetCustomAttribute<DataTransferIgnoredAttribute>() != null)
            return;

        object dataToExport = propertyInfo.GetValue(exportObject);
        if (dataToExport == null)
            return; // TODO-Throw?: Again, decide if we should throw here or not.

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

    /// <summary>
    /// Iterates over ever public member of the specificed type and requests data from the import delegate
    /// to populate those fields.
    /// </summary>
    /// <typeparam name="T">The type to populate the data into.</typeparam>
    /// <param name="ret">The imported data populated into the provided type.</param>
    /// <param name="importDelegate">A delegate that will have key value pairs of data requested from it.</param>
    /// <param name="prefix">A prefix to prepend to all imported keys if provided.</param>
    /// <returns>True if we were able to import the requested class without issue, false otherwise.</returns>
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

            PropertyInfo[] privateProperties = type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance);

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
            Plugin.logger.LogError($" Error while importing: {e}");
            ret = default(T);
            return false;
        }
    }

    /// <summary>
    /// Iterates over the data stored in the passed in class and provides it as key value pairs for the export delegate to deal with.
    /// </summary>
    /// <typeparam name="T">The type the data to export will be stored in.</typeparam>
    /// <param name="classToExport">The data to export.</param>
    /// <param name="exportDelegate">The function to call when we have a new key value pair to export.</param>
    /// <param name="prefix">An optional prefix that will be prepended to the front of all keys.</param>
    /// <exception cref="ArgumentNullException">Thrown when the passed in data class is null.</exception>
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
using System.Collections.Generic;
using System.ComponentModel;
using SimpleJSON;

namespace RavenM.Helpers
{
    static class DataPacking
    {
        private const string SeparatorString = "~~"; // Picking something that should never be in most data types.

        public static string EncodeToString<T>(List<T> items)
        {
            return string.Join(SeparatorString, items);
        }

        public static List<T> DecodeFromString<T>(string str)
        {
            string[] elementStrings = typeof(T) == typeof(string) ? DecodeStringList(str).ToArray() : str.Split(new string[] {SeparatorString}, System.StringSplitOptions.RemoveEmptyEntries);
            List<T> ret = new (elementStrings.Length);
            foreach (string elementString in elementStrings)
            {
                TypeConverter typeConverter = TypeDescriptor.GetConverter(typeof(T));
                ret.Add((T)typeConverter.ConvertFromString(elementString));
            }

            return ret;
        }

        
        // Strings fundamentally can have any separator string we could think to use. We need to escape our separator string 
        // or we can just pack it all into an encoding that will do it for us. In this case let's just use JSON.
        public static string EncodeToString(List<string> items)
        {
            JSONArray jsonArray = new JSONArray();
            foreach (string item in items)
            {
                jsonArray.Add(new JSONString(item));
            }
            return jsonArray.ToString();
        }

        public static List<string> DecodeStringList(string str)
        {
            JSONArray jsonArray = JSON.Parse(str).AsArray;
            List<string> ret = new List<string>();

            foreach (var item in jsonArray)
            {
                ret.Add(item.Value.ToString());
            }

            return ret;
        }
    }

    public interface TransferableNestedClass
    {
        object ImportNested(GenericDataTransfer.ImportDataDelegate importDelegate, string propertyName);
        
        public void ExportNested(GenericDataTransfer.ExportDataDelegate exportDelegate, string propertyName);
    }

    public class GenericNested<T> : TransferableNestedClass where T : new()
    {
        private T self;

        public object ImportNested(GenericDataTransfer.ImportDataDelegate importDelegate, string propertyName)
        {
            GenericDataTransfer.ImportFrom<T>(out T ret, importDelegate, propertyName);
            return ret;
        }
        
        public void ExportNested(GenericDataTransfer.ExportDataDelegate exportDelegate, string propertyName)
        {
            GenericDataTransfer.ExportTo(self, exportDelegate, propertyName);
        }

        public void SetSelf(T obj)
        {
            self = obj;
        }
    }
}
using System;
using System.Reflection;
using System.Text;

namespace RavenM.Helpers
{
    public abstract class GenericEquatable<T> : IEquatable<T>
    {
        public bool Equals(T other)
        {
            if (other == null)
                return this == null;

            PropertyInfo[] properties = this.GetType().GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead) continue;

                object propertyVal = property.GetValue(this); 
                if ((propertyVal == null && property.GetValue(other) != null) 
                || (propertyVal != null &&  propertyVal.Equals(property.GetValue(other)) == false))
                    return false;
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
}
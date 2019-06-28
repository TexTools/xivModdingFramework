using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using xivModdingFramework.Resources;

namespace xivModdingFramework.General.Enums
{
    public static class XivCategorys
    {
        static List<string> _keys = null;
        static XivCategorys()
        {
            _keys = typeof(XivStrings).GetProperties(BindingFlags.Static|BindingFlags.NonPublic).Select(it => it.Name).ToList();
        }

        public static string GetDisplayName(this string value)
        {
            var rm = new ResourceManager(typeof(XivStrings));
            string displayName = value;
            foreach(var key in _keys)
            {
                var name = rm.GetString(key,new CultureInfo("en"));
                if ( name== value)
                {
                    displayName = rm.GetString(key);
                    break;
                }
            }
            return displayName;
        }
        public static string GetEnDisplayName(this string value)
        {
            var rm = new ResourceManager(typeof(XivStrings));
            string displayName = value;
            foreach (var key in _keys)
            {
                var name = rm.GetString(key);
                if (name == value)
                {
                    displayName = rm.GetString(key, new CultureInfo("en"));
                    break;
                }
            }
            return displayName;
        }
    }
}

using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Model.Scene2D;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace xivModdingFramework.Helpers
{
    // I'm not sure why we didn't have a constants class to refer to before, but oh well.
    public static class Constants
    {
        /// <summary>
        /// The alphabet.  Now in character array form.
        /// </summary>
        public static readonly char[] Alphabet = { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z' };
        public static string BinaryOffsetMarker = "::";



        // This commedically long regex validates URL strings.
        // Came from - https://mathiasbynens.be/demo/url-regex (@scottgonzales version)
        public static readonly Regex UrlValidationRegex = new Regex("([a-z]([a-z]|\\d|\\+|-|\\.)*):(\\/\\/(((([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:)*@)?((\\[(|(v[\\da-f]{1,}\\.(([a-z]|\\d|-|\\.|_|~)|[!\\$&'\\(\\)\\*\\+,;=]|:)+))\\])|((\\d|[1-9]\\d|1\\d\\d|2[0-4]\\d|25[0-5])\\.(\\d|[1-9]\\d|1\\d\\d|2[0-4]\\d|25[0-5])\\.(\\d|[1-9]\\d|1\\d\\d|2[0-4]\\d|25[0-5])\\.(\\d|[1-9]\\d|1\\d\\d|2[0-4]\\d|25[0-5]))|(([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=])*)(:\\d*)?)(\\/(([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)*)*|(\\/((([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)+(\\/(([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)*)*)?)|((([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)+(\\/(([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)*)*)|((([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)){0})(\\?((([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)|[\\xE000-\\xF8FF]|\\/|\\?)*)?(\\#((([a-z]|\\d|-|\\.|_|~|[\\x00A0-\\xD7FF\\xF900-\\xFDCF\\xFDF0-\\xFFEF])|(%[\\da-f]{2})|[!\\$&'\\(\\)\\*\\+,;=]|:|@)|\\/|\\?)*)?", RegexOptions.IgnoreCase);


        public const string InternalModSourceName = "_INTERNAL_";
    }
}

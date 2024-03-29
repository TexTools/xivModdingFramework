﻿using HelixToolkit.SharpDX.Core;
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

        public const string InternalModSourceName = "_INTERNAL_";
    }
}

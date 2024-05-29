using System;
using System.Collections.Generic;
using System.Text;

namespace xivModdingFramework.Cache
{
    internal class FrameworkExceptions
    {
        public class ModdingFrameworkException : Exception
        {
            public ModdingFrameworkException()
            {
            }

            public ModdingFrameworkException(string message)
                : base(message)
            {
            }

            public ModdingFrameworkException(string message, Exception inner)
                : base(message, inner)
            {
            }
        }
    }
}

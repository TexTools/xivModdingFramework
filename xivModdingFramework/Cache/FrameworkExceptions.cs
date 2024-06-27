using System;
using System.Collections.Generic;
using System.Text;
using static xivModdingFramework.Cache.FrameworkExceptions;

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

        public class OffsetException : ModdingFrameworkException
        {
            public OffsetException()
            {
            }

            public OffsetException(string message)
                : base(message)
            {
            }

            public OffsetException(string message, Exception inner)
                : base(message, inner)
            {
            }
        }
    }
}

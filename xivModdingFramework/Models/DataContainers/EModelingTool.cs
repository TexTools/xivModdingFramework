using System;
using System.Collections.Generic;
using System.Text;

namespace xivModdingFramework.Models.DataContainers
{
    public enum EModelingTool
    {
        Blender,
        Max,
        Maya,
        Unreal,
        Unity,
    };

    public static class ModelingToolExtensions
    {
        public static bool UsesDirectXNormals(this EModelingTool modelingTool)
        {
            if(modelingTool == EModelingTool.Max || modelingTool == EModelingTool.Unreal)
            {
                return true;
            }
            return false;
        }
    }
}

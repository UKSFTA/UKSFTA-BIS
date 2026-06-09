using System;
using System.Collections.Generic;
using System.Text;
using BIS.Core.Math;

namespace BIS.P3D
{
    public interface ILevelOfDetail
    {
        float Resolution { get; }
        string Name { get; }
        Vector3P[] Points { get; }
        string[] Selections { get; }
        string[] Proxies { get; }
        string[] Textures { get; }
        IEnumerable<Tuple<string, string>> NamedProperties { get; }
        IEnumerable<INamedSelection> NamedSelections { get; }
        int FaceCount { get; }
        uint VertexCount { get; }
        IEnumerable<string> GetTextures();
        IEnumerable<string> GetMaterials();

        LodHashId GetModelHashId();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using BIS.P3D.MLOD;

namespace BIS.P3D.Conversion
{
    public static class ODOL2MLOD
    {
        public static BIS.P3D.MLOD.MLOD Convert(BIS.P3D.ODOL.ODOL odol)
        {
            var lods = new List<BIS.P3D.MLOD.P3DM_LOD>();

            foreach (var lod in odol.Lods)
            {
                lods.Add(ConvertLod(lod));
            }

            return new BIS.P3D.MLOD.MLOD(lods.ToArray());
        }

        private static BIS.P3D.MLOD.P3DM_LOD ConvertLod(BIS.P3D.ODOL.LOD odolLod)
        {
            var vertices = odolLod.Vertices;
            var points = new Point[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                points[i] = new Point(vertices[i], PointFlags.NONE);
            }

            var faces = ConvertFaces(odolLod);

            var taggs = new List<Tagg>();

            // Map UV Sets
            for (int i = 0; i < odolLod.UvSets.Length; i++)
            {
                var uvs = odolLod.UvSets[i].GetUV();
                var faceUVs = new float[faces.Length][,];
                for(int f=0; f<faces.Length; f++)
                {
                    faceUVs[f] = new float[faces[f].VertexCount, 2];
                    for(int v=0; v<faces[f].VertexCount; v++)
                    {
                        var vIdx = faces[f].Vertices[v].PointIndex;
                        faceUVs[f][v, 0] = uvs[vIdx].X;
                        faceUVs[f][v, 1] = uvs[vIdx].Y;
                    }
                }
                taggs.Add(new UVSetTagg((uint)(faceUVs.Sum(f => f.Length * 4) + 4), i, faceUVs));
            }

            // Map Named Selections
            foreach (var selection in odolLod.NamedSelections)
            {
                var pointsSelection = new byte[points.Length];
                var facesSelection = new byte[faces.Length];
                // Mapping logic would go here to fill points/faces selection arrays
                taggs.Add(new NamedSelectionTagg(selection.Name, pointsSelection, facesSelection));
            }

            // Map Properties
            foreach (var prop in odolLod.NamedProperties)
            {
                taggs.Add(new PropertyTagg(prop.Item1, prop.Item2));
            }

            return new BIS.P3D.MLOD.P3DM_LOD(odolLod.Resolution, points, odolLod.Normals.ToArray(), faces, taggs);
        }

        private static BIS.P3D.MLOD.Face[] ConvertFaces(BIS.P3D.ODOL.LOD odolLod)
        {
            var mlodFaces = new List<BIS.P3D.MLOD.Face>();

            foreach (var section in odolLod.Sections)
            {
                var uvs = odolLod.UvSets[0].GetUV();
                var facesInSection = section.GetFaces(odolLod.Polygons.Faces);

                foreach (var odolFace in facesInSection)
                {
                    var vertexCount = odolFace.VertexIndices.Length;

                    var mlodVertices = new BIS.P3D.MLOD.Vertex[4]; // MLOD faces seem to expect 4 vertices in the structure
                    for (int i = 0; i < 4; i++)
                    {
                        if (i < vertexCount)
                        {
                            var vIdx = odolFace.VertexIndices[vertexCount - 1 - i];
                            mlodVertices[i] = new BIS.P3D.MLOD.Vertex(
                                vIdx, 
                                vIdx, 
                                uvs[vIdx].X, 
                                uvs[vIdx].Y
                            );
                        }
                        else
                        {
                            mlodVertices[i] = new BIS.P3D.MLOD.Vertex(0, 0, 0, 0);
                        }
                    }

                    string texture = (section.TextureIndex == -1) ? "" : odolLod.Textures[section.TextureIndex];
                    string material = (section.MaterialIndex == -1) ? (section.Material ?? "") : odolLod.Materials[section.MaterialIndex].MaterialName;

                    mlodFaces.Add(new BIS.P3D.MLOD.Face(vertexCount, mlodVertices, BIS.P3D.MLOD.FaceFlags.DEFAULT, texture, material));
                }
            }
            return mlodFaces.ToArray();
        }
    }
}
